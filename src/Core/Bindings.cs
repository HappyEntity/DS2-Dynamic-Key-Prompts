namespace DynamicKeyPrompts;

/// One key assigned to a game input. Codes are the game's internal key codes
/// (keyboard ≥ 70: DirectInput scancode + 0x45 for most keys; mouse &lt; 70; 0xFF = unbound).
/// Modifier is 0 or one of the pseudo inputs 42 (Shift), 43 (Ctrl), 44 (Alt).
readonly record struct KeyBinding(int Code, int Modifier)
{
    public const int FirstKeyboardCode = 0x46;
    public const int Nothing = 0x0D, None = 0xFF;
    public bool IsBound => Code != Nothing && Code != None;
    public bool IsKeyboard => Code >= FirstKeyboardCode && Code != None;
}

/// Game input ids — the first column of the game's key-binding action table. For in-world
/// actions they equal the pad button (Interact = 12 = A); menu actions have their own ids.
static class Input
{
    public const int CameraUp = 22, CameraDown = 23, CameraLeft = 24, CameraRight = 25;
    public const int RunForward = 26, RunBackward = 27, RunLeft = 28, RunRight = 29;
    public const int CursorUp = 33, CursorDown = 34, CursorLeft = 35, CursorRight = 36;
    public const int Shift = 42, Ctrl = 43, Alt = 44;
}

static class Bindings
{
    static readonly object s_lock = new();
    static Dictionary<int, List<KeyBinding>> s_byInput = new();
    static Game.ActionEntry[]? s_actions;

    /// Bumped whenever the bindings change; the text cache is keyed on it.
    public static int Generation { get; private set; }
    public static string Source { get; private set; } = "none";

    public static void Set(IEnumerable<(int Input, KeyBinding Key)> entries, string source)
    {
        var map = new Dictionary<int, List<KeyBinding>>();
        foreach (var (input, key) in entries)
        {
            if (!map.TryGetValue(input, out var list)) map[input] = list = new();
            list.Add(key);
        }
        lock (s_lock)
        {
            if (Same(s_byInput, map)) return;
            s_byInput = map;
            Source = source;
            Generation++;
        }
        Loader.Log($"bindings: {map.Sum(kv => kv.Value.Count)} keys from {source} (generation {Generation})");
    }

    /// Forces every cached label to be rebuilt (e.g. once the keycap font is in use).
    public static void Invalidate()
    {
        lock (s_lock) Generation++;
    }

    static bool Same(Dictionary<int, List<KeyBinding>> a, Dictionary<int, List<KeyBinding>> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v.SequenceEqual(kv.Value));

    // ---------------------------------------------------------------- pad glyph -> inputs

    public static string? LabelFor(Glyph glyph, PromptContext ctx) => glyph switch
    {
        Glyph.Button b => InputForPad(b.Pad, ctx) is int input ? KeyLabel(input) : null,
        // On PC the camera is the mouse unless the player only wants keyboard labels.
        Glyph.Directions { IsCamera: true } when Config.Labels != LabelMode.Keyboard =>
            UseIcons ? Icons.KeycapSet.MouseMoveChar.ToString() : KeyNames.MouseMovement,
        Glyph.Directions d => DirectionsLabel(ctx == PromptContext.World ? d.WorldInputs : d.MenuInputs),
        _ => null,
    };

    /// Label for a specific game input (used for per-message overrides).
    public static string? LabelForInput(int input) => KeyLabel(input);

    static string? DirectionsLabel(int[] inputs)
    {
        var labels = inputs.Select(KeyLabel).ToArray();
        if (labels.Any(l => l == null)) return null;
        bool compact = labels.All(l => l!.Length == 1);
        return string.Join(compact ? "" : Config.Separator, labels);
    }

    /// World: the pad button id is the input id. Menu: the action whose menu column is the
    /// pad button, then its input id. Buttons without a menu meaning fall back to the world one.
    static int? InputForPad(Pad pad, PromptContext ctx)
    {
        if (ctx == PromptContext.World) return (int)pad;
        s_actions ??= Game.ReadActionTable();
        foreach (var a in s_actions)
        {
            if (a.Name.Category != (int)MsgCategory.Win32OnlyMessage || a.MenuInput != (int)pad) continue;
            // The static initialiser leaves "Move cursor (up)" at 0/0; its input id is 33.
            return a.Name.Id == 10332 ? Input.CursorUp : a.GameInput;
        }
        return (int)pad;
    }

    // ---------------------------------------------------------------- labels

    static string? KeyLabel(int input)
    {
        if (Config.BindingOverrides.TryGetValue(input.ToString(), out var manual))
            return manual;
        List<KeyBinding>? keys;
        lock (s_lock) s_byInput.TryGetValue(input, out keys);
        if (keys == null) return null;

        // Careful: default(KeyBinding) is code 0 = left mouse button, so "not found" must be null.
        var bound = keys.Where(k => k.IsBound).ToList();
        KeyBinding? keyboard = bound.Where(k => k.IsKeyboard).Cast<KeyBinding?>().FirstOrDefault();
        KeyBinding? mouse = bound.Where(k => !k.IsKeyboard).Cast<KeyBinding?>().FirstOrDefault();
        KeyBinding? plainMouse = bound.Where(k => !k.IsKeyboard && k.Modifier == 0).Cast<KeyBinding?>().FirstOrDefault();

        KeyBinding?[] chosen = Config.Labels switch
        {
            // A plain mouse button is what a mouse player actually presses (attack = LMB, lock-on = MMB);
            // a mouse chord like Shift+LMB is usually a fallback, so the key wins then (Interact = E).
            LabelMode.Auto => [plainMouse ?? keyboard ?? mouse],
            LabelMode.Keyboard => [keyboard ?? mouse],
            LabelMode.Mouse => [mouse ?? keyboard],
            _ => [keyboard, mouse],
        };
        var labels = chosen.OfType<KeyBinding>().Select(Format).OfType<string>().ToList();
        return labels.Count == 0 ? null : string.Join(Config.Separator, labels);
    }

    /// True when labels are keycap glyphs from the patched font rather than text.
    public static bool UseIcons => Config.Style == LabelStyle.Icons && FontRedirect.IconsActive;

    static readonly HashSet<int> s_iconCodes = Icons.KeycapSet.All().Select(k => k.Code).ToHashSet();

    static string? Icon(int code) => s_iconCodes.Contains(code) ? Icons.KeycapSet.CharFor(code).ToString() : null;

    static string? Format(KeyBinding b)
    {
        if (UseIcons && Icon(b.Code) is string keyIcon)
        {
            string? modIcon = b.Modifier switch
            {
                Input.Shift => Icon(111), Input.Ctrl => Icon(98), Input.Alt => Icon(125), _ => null,
            };
            return modIcon == null ? keyIcon : modIcon + "+" + keyIcon;
        }
        string? key = KeyNames.Get(b.Code);
        if (key == null) return null;
        string? mod = b.Modifier switch
        {
            Input.Shift => KeyNames.Modifier(10213),
            Input.Ctrl => KeyNames.Modifier(10216),
            Input.Alt => KeyNames.Modifier(10219),
            _ => null,
        };
        return mod == null ? key : $"{mod}+{key}";
    }

    public static string Describe()
    {
        List<(int, List<KeyBinding>)> all;
        lock (s_lock) all = s_byInput.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
        s_actions ??= Game.ReadActionTable();
        var lines = new List<string>();
        foreach (var (input, keys) in all)
        {
            var names = string.Join(", ", s_actions.Where(a => a.GameInput == input && a.Name.Id != 0)
                .Select(a => Game.GetMessage(a.Name) ?? a.Name.Id.ToString()));
            lines.Add($"  input {input,2} [{names}]: " + string.Join("  ", keys.Select(k => $"{k.Code:X2}/{k.Modifier} \"{KeyNames.Get(k.Code)}\"")));
        }
        return string.Join("\n", lines);
    }

    // ---------------------------------------------------------------- defaults (fallback)

    /// Default bindings shipped with the game (Game\Param\KeyConfigParam.param), used until the
    /// live configuration can be read. Row 0 = keyboard, row 1 = mouse; layout in DefaultRowSlots below.
    public static bool LoadDefaults(string gameDir)
    {
        string path = Path.Combine(gameDir, "Param", "KeyConfigParam.param");
        try
        {
            byte[] f = File.ReadAllBytes(path);
            int rows = BitConverter.ToUInt16(f, 0x0A);
            var entries = new List<(int, KeyBinding)>();
            for (int r = 0; r < rows && r < 2; r++)
            {
                int off = (int)BitConverter.ToInt64(f, 0x40 + r * 0x18 + 8);
                foreach (var (input, slot, hasMod) in DefaultRowSlots)
                {
                    int code = f[off + slot];
                    int mod = hasMod ? f[off + slot + 1] : 0;
                    int modInput = mod switch { 0x6F or 0x7B => Input.Shift, 0x62 or 0xB0 => Input.Ctrl, 0x7D or 0xBB => Input.Alt, _ => 0 };
                    if (code != KeyBinding.None) entries.Add((input, new KeyBinding(code, modInput)));
                }
            }
            Set(entries, "defaults (KeyConfigParam.param)");
            return true;
        }
        catch (Exception e)
        {
            Loader.Log($"bindings: cannot read {path}: {e.Message}");
            return false;
        }
    }

    // (input id, byte offset in the 76-byte row, followed by a modifier byte)
    static readonly (int Input, int Offset, bool HasModifier)[] DefaultRowSlots =
    [
        (Input.RunForward, 0, true), (Input.RunBackward, 2, true), (Input.RunRight, 4, true), (Input.RunLeft, 6, true),
        (13, 9, false),  // dash
        (10, 10, true),  // jump
        (Input.CameraUp, 18, true), (Input.CameraDown, 20, true), (Input.CameraRight, 22, true), (Input.CameraLeft, 24, true),
        (11, 26, true),  // reset camera / lock on
        (0, 28, true), (1, 30, true), (3, 32, true), (2, 34, true), // switch spells / items / right / left weapon
        (7, 36, true), (9, 38, true), (6, 40, true), (8, 42, true), // attacks
        (14, 44, true),  // use item
        (12, 46, true),  // interact
        (15, 48, true),  // two-hand
        (4, 50, true), (5, 52, true), // start menu, gestures
        (Input.CursorUp, 54, false), (Input.CursorDown, 55, false), (Input.CursorRight, 56, false), (Input.CursorLeft, 57, false),
        (16, 58, false), (17, 59, false), // confirm, cancel
        (37, 60, true), (38, 62, true),   // toggle menu left / right
        (39, 64, false), (40, 65, false), // function 1 / 2
    ];
}
