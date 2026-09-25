namespace DynamicKeyPrompts;

enum LabelMode { Auto, Keyboard, Mouse, Both }
enum LabelStyle { Icons, Text }

/// DynamicKeyPrompts.ini next to the core DLL. Minimal INI reader: [section] key=value, ; comments.
static class Config
{
    public static bool Enabled { get; private set; } = true;
    public static bool Diagnostics { get; private set; } = false;
    /// {0} = key label, e.g. "[{0}]" -> "[E]".
    public static string Format { get; private set; } = "[{0}]";
    /// Separator between keys of a multi-key icon (stick / d-pad) when a label is longer than one letter.
    public static string Separator { get; private set; } = "/";
    /// Keycap icons drawn into the game font, or plain text labels.
    public static LabelStyle Style { get; private set; } = LabelStyle.Icons;
    /// Look of the keycap icons: dark | minimal | silver.
    public static string IconTheme { get; private set; } = "dark";
    /// Which of an action's bindings to show (keyboard key vs mouse button).
    public static LabelMode Labels { get; private set; } = LabelMode.Auto;
    /// Short key labels ("LMB" instead of "Left click", "Shift" instead of "Left Shift").
    public static bool ShortNames { get; private set; } = true;
    /// RRGGBB colour for key labels, empty = game default text colour.
    public static string Color { get; private set; } = "";
    /// Manual bindings (action FMG id or name -> key name), used when the live bindings cannot be read.
    public static Dictionary<string, string> BindingOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            Loader.Log($"config: {path} not found, using defaults");
            return;
        }
        string section = "";
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line[^1] == ']') { section = line[1..^1].Trim(); continue; }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string k = line[..eq].Trim(), v = line[(eq + 1)..].Trim();
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1];

            if (section.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                switch (k.ToLowerInvariant())
                {
                    case "enabled": Enabled = v != "0"; break;
                    case "diagnostics": Diagnostics = v != "0"; break;
                    case "format": if (v.Contains("{0}")) Format = v; break;
                    case "separator": Separator = v; break;
                    case "shortnames": ShortNames = v != "0"; break;
                    case "icontheme": if (v.Length > 0 && v.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) IconTheme = v; break;
                    case "style": if (Enum.TryParse<LabelStyle>(v, true, out var style)) Style = style; break;
                    case "labels": if (Enum.TryParse<LabelMode>(v, true, out var mode)) Labels = mode; break;
                    case "color": Color = IsHex(v) ? v : ""; break;
                }
            }
            else if (section.Equals("Bindings", StringComparison.OrdinalIgnoreCase))
                BindingOverrides[k] = v;
        }
        Loader.Log($"config: enabled={Enabled} diagnostics={Diagnostics} style={Style} theme={IconTheme} labels={Labels} short={ShortNames} format=\"{Format}\" color=\"{Color}\" overrides={BindingOverrides.Count}");
    }

    static bool IsHex(string v) => v.Length == 6 && v.All(Uri.IsHexDigit);
}
