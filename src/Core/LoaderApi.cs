using System.Runtime.InteropServices;
using System.Text;

namespace DynamicKeyPrompts;

// Mirror of DkpLoaderApi in src/Loader/loader_api.h.
[StructLayout(LayoutKind.Sequential)]
unsafe struct LoaderApiNative
{
    public uint Size;
    public uint Version;
    public nint GameBase;
    public char* ModDir;
    public int ArxanDetected;
    public int ArxanStatus;
    public delegate* unmanaged<byte*, void> Log;
    public delegate* unmanaged<nint, nint, nint*, int> CreateHook;
    public delegate* unmanaged<nint, int> EnableHook;
    public delegate* unmanaged<nint, int> DisableHook;
}

static unsafe class Loader
{
    public const uint ExpectedVersion = 1;
    static LoaderApiNative s_api;

    public static nint GameBase => s_api.GameBase;
    public static string ModDir { get; private set; } = "";
    public static bool ArxanDetected => s_api.ArxanDetected != 0;
    public static int ArxanStatus => s_api.ArxanStatus;

    public static bool Attach(LoaderApiNative* api)
    {
        if (api == null || api->Size < (uint)sizeof(LoaderApiNative) || api->Version != ExpectedVersion)
            return false;
        s_api = *api;
        ModDir = new string(api->ModDir);
        return true;
    }

    public static void Log(string line)
    {
        if (s_api.Log == null) return;
        int n = Encoding.UTF8.GetMaxByteCount(line.Length) + 1;
        byte[] buf = n <= 4096 ? new byte[n] : new byte[n];
        int len = Encoding.UTF8.GetBytes(line, buf);
        buf[len] = 0;
        fixed (byte* p = buf) s_api.Log(p);
    }

    /// Installs and enables a MinHook detour. Returns the trampoline to the original, or 0.
    public static nint Hook(string name, nint target, nint detour)
    {
        nint original;
        int rc = s_api.CreateHook(target, detour, &original);
        if (rc != 0)
        {
            Log($"hook {name} @ {target:X}: create failed ({rc})");
            return 0;
        }
        rc = s_api.EnableHook(target);
        if (rc != 0)
        {
            Log($"hook {name} @ {target:X}: enable failed ({rc})");
            return 0;
        }
        Log($"hook {name} @ {target:X}: installed");
        return original;
    }
}
