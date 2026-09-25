namespace DynamicKeyPrompts;

/// Key code -> label. Uses the game's own localised key names (win32OnlyMessage), shortened
/// where the full name would not fit into a prompt line (ShortNames=1).
static class KeyNames
{
    static readonly object s_lock = new();
    static readonly Dictionary<int, string?> s_cache = new();
    static Game.KeyEntry[]? s_keys;
    static int s_layout = -1;
    static bool? s_russian;

    public static string? Get(int code)
    {
        int layout = Game.KeyboardLayoutVariant();
        lock (s_lock)
        {
            if (layout != s_layout) { s_cache.Clear(); s_layout = layout; }
            if (s_cache.TryGetValue(code, out var cached)) return cached;
        }
        string? name = Config.ShortNames ? Short(code) : null;
        name ??= GameName(code, layout);
        lock (s_lock) s_cache[code] = name;
        return name;
    }

    /// Shift / Ctrl / Alt as the game writes them (FMG ids 10213 / 10216 / 10219).
    public static string Modifier(int fmgId) =>
        Game.GetMessage(new MsgRef((int)MsgCategory.Win32OnlyMessage, fmgId))?.Trim() ?? fmgId switch
        {
            10213 => "Shift", 10216 => "Ctrl", _ => "Alt",
        };

    static string? GameName(int code, int layout)
    {
        s_keys ??= Game.ReadKeyTable();
        foreach (var k in s_keys)
        {
            if (k.Code != code) continue;
            MsgRef m = layout == 1 && k.De.Id != 0 ? k.De : layout == 2 && k.Fr.Id != 0 ? k.Fr : k.Us;
            return Game.GetMessage(m)?.Trim();
        }
        return null;
    }

    /// Label for "move the mouse" (the camera stick on PC).
    public static string MouseMovement => Russian ? "Мышь" : "Mouse";

    static bool Russian => s_russian ??= GameName(0, 0)?.Contains("ЛКМ") == true;

    /// Short labels for keys whose game names are long. null = use the game's name.
    static string? Short(int code) => code switch
    {
        0 => Russian ? "ЛКМ" : "LMB",
        1 => Russian ? "ПКМ" : "RMB",
        2 => Russian ? "СКМ" : "MMB",
        6 => Russian ? "Колесо↑" : "Wheel↑",
        7 => Russian ? "Колесо↓" : "Wheel↓",
        11 => Russian ? "2×ЛКМ" : "2×LMB",
        12 => Russian ? "2×ПКМ" : "2×RMB",
        83 => "BkSp",
        126 => "Space",
        111 => "Shift",
        123 => "RShift",
        98 => "Ctrl",
        176 => "RCtrl",
        125 => "Alt",
        187 => "AltGr",
        191 => "PgUp",
        196 => "PgDn",
        197 => "Ins",
        198 => "Del",
        151 => "Num0", 148 => "Num1", 149 => "Num2", 150 => "Num3", 144 => "Num4",
        145 => "Num5", 146 => "Num6", 140 => "Num7", 141 => "Num8", 142 => "Num9",
        175 => "NumEnter", 185 => "Num/", 124 => "Num*", 143 => "Num-", 147 => "Num+", 152 => "Num.",
        _ => null,
    };
}
