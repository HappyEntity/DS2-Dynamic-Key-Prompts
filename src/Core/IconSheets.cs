using DynamicKeyPrompts.Icons;

namespace DynamicKeyPrompts;

/// The editable icon sheets next to the mod: DynamicKeyPrompts\icons\&lt;theme&gt;.png + .txt.
/// Built-in themes are written there on first use; any other name is a user theme.
static class IconSheets
{
    static string Dir => Path.Combine(Loader.ModDir, "icons");

    public static (string Png, string Txt) Files(string theme) =>
        (Path.Combine(Dir, theme + ".png"), Path.Combine(Dir, theme + ".txt"));

    public static string Fingerprint(string path)
    {
        var f = new FileInfo(path);
        return f.Exists ? $"{f.Length}:{f.LastWriteTimeUtc.Ticks}" : "-";
    }

    /// Writes the sheets of all built-in themes that are missing (never overwrites user edits).
    public static void EnsureBuiltIns()
    {
        foreach (var theme in KeycapRenderer.Themes)
        {
            var (png, txt) = Files(theme.Name);
            if (File.Exists(png) && File.Exists(txt)) continue;
            try
            {
                Directory.CreateDirectory(Dir);
                var (bytes, layout) = IconSheet.Generate(theme);
                File.WriteAllBytes(png, bytes);
                File.WriteAllText(txt, layout);
                Loader.Log($"icons: wrote {png}");
            }
            catch (Exception e) { Loader.Log($"icons: cannot write {png}: {e.Message}"); }
        }
    }

    /// The sheet of the given theme, or null (icons are then rendered from the built-in theme).
    public static IconSheet? Load(string theme)
    {
        EnsureBuiltIns();
        var (png, txt) = Files(theme);
        if (!File.Exists(png) || !File.Exists(txt))
        {
            Loader.Log($"icons: theme '{theme}' not found in {Dir} - using built-in '{KeycapRenderer.ThemeByName(theme).Name}'");
            return null;
        }
        try
        {
            var sheet = IconSheet.Load(File.ReadAllBytes(png), File.ReadAllText(txt));
            Loader.Log($"icons: loaded {sheet.Icons.Count} icons from {png}");
            return sheet;
        }
        catch (Exception e)
        {
            Loader.Log($"icons: cannot read {png}: {e.Message} - using built-in icons");
            return null;
        }
    }
}
