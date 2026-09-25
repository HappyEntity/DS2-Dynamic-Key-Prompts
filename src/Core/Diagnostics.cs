using System.Runtime.InteropServices;

namespace DynamicKeyPrompts;

/// Diagnostic helpers for finding out where the game keeps things. Enabled by Diagnostics=1.
///  - F9: dump of the live key config and of the bindings the labels are built from.
///  - Logs every row the in-game Key Bindings screen shows (key index / action), so the
///    decoded layout can be compared with what the game itself displays.
static unsafe class Diagnostics
{
    static delegate* unmanaged<nint, nint, nint> s_rowKeyNameOrig;
    static readonly HashSet<(nint, int, int)> s_rowsLogged = new();

    public static void Install()
    {
        if (Game.PrologueMatches(Rva.KeyBindingRowKeyName, Rva.KeyBindingRowKeyNamePrologue))
        {
            nint orig = Loader.Hook("KeyBindingRowKeyName", Game.At(Rva.KeyBindingRowKeyName),
                (nint)(delegate* unmanaged<nint, nint, nint>)&RowKeyNameDetour);
            s_rowKeyNameOrig = (delegate* unmanaged<nint, nint, nint>)orig;
        }
        else
            Loader.Log("KeyBindingRowKeyName prologue mismatch - row logging disabled");

        var t = new Thread(HotkeyLoop) { IsBackground = true, Name = "DKP hotkeys" };
        t.Start();
    }

    // MsgRef* fn(Row* row, MsgRef* out): row+0 = key table index, row+4 = action input id (a).
    [UnmanagedCallersOnly]
    static nint RowKeyNameDetour(nint row, nint outRef)
    {
        nint r = s_rowKeyNameOrig(row, outRef);
        try
        {
            int keyIndex = *(int*)row, input = *(int*)(row + 4);
            lock (s_rowsLogged)
            {
                if (s_rowsLogged.Add((row, keyIndex, input)))
                {
                    var m = new MsgRef(*(int*)r, *(int*)(r + 4));
                    var raw = Memory.Read(row, 0x20);
                    Loader.Log($"keyscreen row {row:X}: keyIndex={keyIndex} input={input} -> {m.Category}/{m.Id} \"{Game.GetMessage(m)}\" raw={(raw == null ? "?" : Memory.Hex(raw))}");
                }
            }
        }
        catch { }
        return r;
    }

    const int VK_F9 = 0x78;
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    static void HotkeyLoop()
    {
        uint self = (uint)Environment.ProcessId;
        bool wasDown = false;
        while (true)
        {
            Thread.Sleep(50);
            bool down = (GetAsyncKeyState(VK_F9) & 0x8000) != 0;
            if (down && !wasDown)
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                if (pid == self)
                {
                    Loader.Log("F9 pressed: bindings from " + Bindings.Source + ":\n" + Bindings.Describe());
                    Loader.Log(LiveBindings.Dump());
                }
            }
            wasDown = down;
        }
    }
}
