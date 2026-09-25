using System.Runtime.InteropServices;

namespace DynamicKeyPrompts;

static unsafe class Entry
{
    /// Called by the loader at the game's entry point (main thread, Arxan neutered unless started
    /// by Seamless Co-op, game CRT not yet initialised — do not touch game globals here).
    [UnmanagedCallersOnly(EntryPoint = "DKP_Init")]
    static int Init(LoaderApiNative* api)
    {
        try
        {
            if (!Loader.Attach(api)) return -1;
            Loader.Log($"core: DynamicKeyPrompts {typeof(Entry).Assembly.GetName().Version} base={Loader.GameBase:X} arxan_detected={Loader.ArxanDetected} arxan_status={Loader.ArxanStatus}");

            uint ts = Game.TimeDateStamp();
            if (ts != Rva.ExpectedTimeDateStamp)
            {
                Loader.Log($"core: unsupported DarkSoulsII.exe (timestamp {ts:X8}, expected {Rva.ExpectedTimeDateStamp:X8}) - disabled");
                return -2;
            }

            Config.Load(Path.Combine(Loader.ModDir, "DynamicKeyPrompts.ini"));
            if (!Config.Enabled) return 0;

            string gameDir = Path.GetDirectoryName(Loader.ModDir)!;
            Bindings.LoadDefaults(gameDir);

            // Fonts are opened after the entry point, so the redirect must be in place now.
            if (Config.Style == LabelStyle.Icons) FontRedirect.Install();
            if (!TextHook.Install()) return -3;
            LiveBindings.Start();
            if (Config.Diagnostics) Diagnostics.Install();
            return 0;
        }
        catch (Exception e)
        {
            Loader.Log($"core: init failed: {e}");
            return -100;
        }
    }
}
