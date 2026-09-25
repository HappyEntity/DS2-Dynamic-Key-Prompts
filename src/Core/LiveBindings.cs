using System.Runtime.InteropServices;
using System.Text;

namespace DynamicKeyPrompts;

/// Reads the player's live key configuration the same way the Key Bindings screen does
/// (RVA 0x83B20 → 0xB08400 / 0xB08410):
///
///   keyboardCfg = [[[InputManager] + 0x60] + 0]
///   data        = [keyboardCfg + 8]
///   entries     = [data + 0x60]    (array of { int input; int keyCode; int modifier })
///   count       = (int)[data + 0x68]
///
/// and keeps Bindings in sync (polled, since the player can rebind at any time).
static class LiveBindings
{
    const int EntrySize = 12, MaxEntries = 100;
    static Thread? s_thread;
    static string s_lastState = "";

    public static void Start()
    {
        s_thread = new Thread(Loop) { IsBackground = true, Name = "DKP bindings" };
        s_thread.Start();
    }

    static void Loop()
    {
        while (true)
        {
            try { Poll(); }
            catch (Exception e) { Log($"poll failed: {e.Message}"); }
            Thread.Sleep(500);
        }
    }

    static void Poll()
    {
        var entries = Read(out string state);
        Log(state);
        if (entries != null && entries.Count > 0)
            Bindings.Set(entries, "live key config");
    }

    /// Returns null (with a reason in state) while the object chain is not set up yet.
    public static List<(int Input, KeyBinding Key)>? Read(out string state)
    {
        nint mgrSlot = Game.At(Rva.InputManager);
        if (!Memory.TryReadPtr(mgrSlot, out nint mgr) || mgr == 0) { state = "input manager not created yet"; return null; }
        if (!Memory.TryReadPtr(mgr + 0x60, out nint list) || list == 0) { state = "input manager +0x60 empty"; return null; }
        if (!Memory.TryReadPtr(list, out nint cfg) || cfg == 0) { state = "keyboard config not created yet"; return null; }
        if (!Memory.TryReadPtr(cfg + 8, out nint data) || data == 0) { state = "keyboard config data empty"; return null; }
        if (!Memory.TryReadPtr(data + 0x60, out nint arr) || arr == 0) { state = "no binding array"; return null; }
        var countBytes = Memory.Read(data + 0x68, 4);
        int count = countBytes == null ? -1 : BitConverter.ToInt32(countBytes);
        if (count <= 0 || count > MaxEntries) { state = $"binding count {count} out of range"; return null; }
        var raw = Memory.Read(arr, count * EntrySize);
        if (raw == null) { state = "binding array unreadable"; return null; }

        var res = new List<(int, KeyBinding)>(count);
        for (int i = 0; i < count; i++)
        {
            int input = BitConverter.ToInt32(raw, i * EntrySize);
            int code = BitConverter.ToInt32(raw, i * EntrySize + 4);
            int mod = BitConverter.ToInt32(raw, i * EntrySize + 8);
            if (input < 0 || input > 64 || code < 0 || code > 0xFF) { state = $"entry {i} looks invalid ({input}, {code}, {mod})"; return null; }
            res.Add((input, new KeyBinding(code, mod)));
        }
        state = $"ok: {count} entries at {arr:X}";
        return res;
    }

    static void Log(string state)
    {
        // Only log transitions, the poll runs twice a second.
        if (state == s_lastState) return;
        s_lastState = state;
        Loader.Log($"live bindings: {state}");
    }

    public static string Dump()
    {
        var entries = Read(out string state);
        var sb = new StringBuilder($"live bindings ({state}):");
        if (entries != null)
            foreach (var (input, key) in entries)
                sb.Append($"\n  input {input,2} code {key.Code,3} ({key.Code:X2}) mod {key.Modifier,2} \"{KeyNames.Get(key.Code)}\"");
        return sb.ToString();
    }
}

/// Safe reads of our own process memory (never faults on bad pointers).
static unsafe class Memory
{
    [DllImport("kernel32.dll")] static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern int ReadProcessMemory(nint proc, nint addr, void* buf, nint size, out nint read);

    public static byte[]? Read(nint addr, int size)
    {
        var buf = new byte[size];
        fixed (byte* p = buf)
            return ReadProcessMemory(GetCurrentProcess(), addr, p, size, out nint n) != 0 && n == size ? buf : null;
    }

    public static bool TryReadPtr(nint addr, out nint value)
    {
        nint v = 0;
        bool ok = ReadProcessMemory(GetCurrentProcess(), addr, &v, sizeof(nint), out nint n) != 0 && n == sizeof(nint);
        value = v;
        return ok;
    }

    public static string Hex(ReadOnlySpan<byte> b)
    {
        var sb = new StringBuilder(b.Length * 3);
        for (int i = 0; i < b.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(b[i].ToString("X2"));
        }
        return sb.ToString();
    }
}
