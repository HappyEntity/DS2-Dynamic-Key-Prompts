using System.Runtime.InteropServices;
using System.Text;

namespace DynamicKeyPrompts;

/// Hooks the two FMG lookups every text goes through — by id (RVA 0x503410, used by
/// GetMessage) and by index (RVA 0x5034E0, used by e.g. menu help bars) — and returns a copy
/// of the text in which every pad glyph is replaced by the key bound to the same action.
static unsafe class TextHook
{
    static readonly object s_lock = new();
    // (category, id, original text pointer, bindings generation) -> our native copy.
    // Copies are never freed: the game may keep the pointer for as long as it likes, and
    // there are only a few hundred prompt strings.
    static readonly Dictionary<(int, int, nint, int), nint> s_cache = new();
    static readonly HashSet<(int, int, nint)> s_logged = new();
    static bool s_failed;

    static delegate* unmanaged<nint, uint, char*> s_byIdOrig;
    static delegate* unmanaged<nint, int, char*> s_byIndexOrig;

    public static bool Install()
    {
        // Our own lookups (key names) call the game's GetMessage, which ends in the hooked lookup;
        // key names contain no pad glyphs, so that is harmless.
        Game.GetMessageOriginal = (delegate* unmanaged<int, int, char*>)Game.At(Rva.GetMessage);

        bool ok = false;
        if (CanHook("FmgLookupById", Rva.FmgLookupById, Rva.FmgLookupByIdPrologue))
        {
            nint o = Loader.Hook("FmgLookupById", Game.At(Rva.FmgLookupById), (nint)(delegate* unmanaged<nint, uint, char*>)&ByIdDetour);
            s_byIdOrig = (delegate* unmanaged<nint, uint, char*>)o;
            ok |= o != 0;
        }
        if (CanHook("FmgLookupByIndex", Rva.FmgLookupByIndex, Rva.FmgLookupByIndexPrologue))
        {
            nint o = Loader.Hook("FmgLookupByIndex", Game.At(Rva.FmgLookupByIndex), (nint)(delegate* unmanaged<nint, int, char*>)&ByIndexDetour);
            s_byIndexOrig = (delegate* unmanaged<nint, int, char*>)o;
            ok |= o != 0;
        }
        return ok;
    }

    static bool CanHook(string name, uint rva, byte[] prologue)
    {
        if (Game.IsDetoured(rva))
        {
            Loader.Log($"{name} is already hooked by another mod - chaining after it");
            return true;
        }
        if (Game.PrologueMatches(rva, prologue)) return true;
        Loader.Log($"{name} prologue mismatch - unsupported game version, hook skipped");
        return false;
    }

    [UnmanagedCallersOnly]
    static char* ByIdDetour(nint fmg, uint id) => Process(fmg, (int)id, s_byIdOrig(fmg, id));

    [UnmanagedCallersOnly]
    static char* ByIndexDetour(nint fmg, int index) => Process(fmg, -1, s_byIndexOrig(fmg, index));

    static char* Process(nint fmg, int id, char* text)
    {
        if (text == null || s_failed || !Config.Enabled || !HasGlyph(text))
            return text;
        try
        {
            return Substitute(MessageRepository.CategoryOf(fmg), id, text);
        }
        catch (Exception e)
        {
            s_failed = true; // never take the game down because of a label
            Loader.Log($"text hook disabled after exception: {e}");
            return text;
        }
    }

    static bool HasGlyph(char* s)
    {
        for (; *s != 0; s++)
            if (Glyphs.Any(new ReadOnlySpan<char>(s, 1)))
                return true;
        return false;
    }

    static char* Substitute(int category, int id, char* text)
    {
        lock (s_lock)
        {
            int gen = Bindings.Generation;
            var key = (category, id, (nint)text, gen);
            if (s_cache.TryGetValue(key, out nint cached))
                return (char*)cached;

            string original = new(text);
            string replaced = Replace(original, category, id);
            nint result = (nint)text;
            if (replaced != original)
            {
                result = (nint)NativeMemory.Alloc((nuint)(replaced.Length + 1) * 2);
                fixed (char* src = replaced)
                    Buffer.MemoryCopy(src, (void*)result, replaced.Length * 2, replaced.Length * 2);
                ((char*)result)[replaced.Length] = '\0';
            }
            s_cache[key] = result;

            if (Config.Diagnostics && s_logged.Add((category, id, (nint)text)))
                Loader.Log($"text {(MsgCategory)category}/{(id < 0 ? "by-index" : id.ToString())}: \"{Escape(original)}\" -> \"{Escape(replaced)}\"");
            return (char*)result;
        }
    }

    /// Messages whose icon means a different input than the generic menu meaning.
    /// The arrows beside the Start menu's category bar (LB/RB) are driven by the plain cursor
    /// keys there, even though "Toggle menu" is bound to Shift+arrow.
    static int? InputOverride(int category, int id) => (category, id) switch
    {
        ((int)MsgCategory.IngameMenu, 2101005) => Input.CursorLeft,
        ((int)MsgCategory.IngameMenu, 2101006) => Input.CursorRight,
        _ => null,
    };

    public static string Replace(string s, int category, int id)
    {
        var ctx = Glyphs.ContextOf(category);
        int? forced = InputOverride(category, id);
        var sb = new StringBuilder(s.Length + 16);
        bool inColor = s.Contains("#c[", StringComparison.Ordinal);
        foreach (char c in s)
        {
            var glyph = Glyphs.Get(c);
            string? label = glyph == null ? null
                : forced is int input ? Bindings.LabelForInput(input)
                : Bindings.LabelFor(glyph, ctx);
            if (label == null) { sb.Append(c); continue; }
            // Keycap glyphs stand on their own; brackets and colour are for text labels only.
            bool icons = label.Any(ch => ch >= Icons.KeycapSet.Base && ch <= Icons.KeycapSet.Base + 0xFF);
            string formatted = icons ? label : string.Format(Config.Format, label);
            // Colour tags do not nest: only colour when the string has no tags of its own.
            if (!icons && Config.Color.Length > 0 && !inColor)
                formatted = $"#c[{Config.Color}]{formatted}#c";
            sb.Append(formatted);
        }
        return sb.ToString();
    }

    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == '\n') sb.Append("\\n");
            else if (Glyphs.Get(c) != null || c < 0x20) sb.Append($"{{U+{(int)c:X4}}}");
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
