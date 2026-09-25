using System.Runtime.InteropServices;

namespace DynamicKeyPrompts;

/// Addresses in DarkSoulsII.exe 1.0.3, Steam build 9527516 (RVAs; ASLR is on).
static class Rva
{
    public const uint ExpectedTimeDateStamp = 0x6322EF1D;

    /// const wchar_t* GetMessage(int category, int id) — every FMG text lookup goes through here.
    public const uint GetMessage = 0x503620;
    public static readonly byte[] GetMessagePrologue = [0x48, 0x89, 0x6C, 0x24, 0x18, 0x48, 0x89, 0x74, 0x24, 0x20, 0x41, 0x56];

    /// const wchar_t* FmgLookupById(Fmg* fmg, uint id) — binary search; GetMessage ends here.
    public const uint FmgLookupById = 0x503410;
    public static readonly byte[] FmgLookupByIdPrologue = [0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x7C, 0x24, 0x10];

    /// const wchar_t* FmgLookupByIndex(Fmg* fmg, int index) — n-th string; used via RVA 0x503740.
    public const uint FmgLookupByIndex = 0x5034E0;
    public static readonly byte[] FmgLookupByIndexPrologue = [0x40, 0x56, 0x8B, 0xF2, 0x4C, 0x8B, 0xD9];

    /// MsgRepository* — 27 categories; Fmg* of category c at +0x1D8 + c * 0x20.
    public const uint MsgRepository = 0x1616CB0;

    /// MsgRef* KeyBindingRow_GetKeyName(Row* row, MsgRef* out) — Key Bindings screen, row.keyIndex -> key name.
    public const uint KeyBindingRowKeyName = 0x84050;
    public static readonly byte[] KeyBindingRowKeyNamePrologue = [0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20];

    /// Key table: 0x58 entries × 0x1C { MsgRef us, de, fr; int code }.
    public const uint KeyTable = 0x160CA80;
    public const int KeyTableCount = 0x58, KeyTableStride = 0x1C;

    /// Action table: 0x2F entries × 0x10 { MsgRef name; int gameInput; int menuInput }.
    public const uint ActionTable = 0x160C790;
    public const int ActionTableCount = 0x2F, ActionTableStride = 0x10;

    public const uint GameManager = 0x16148F0;
    public const uint InputManager = 0x16751F8;

    // .data section, for pointer-path searches in diagnostics.
    public const uint DataSection = 0x155C000, DataSectionSize = 0x33D3F0;
}

/// FMG message categories (index into MsgRepository), same order as the file names in the exe.
enum MsgCategory
{
    Common = 0, IngameSystem = 1, KeyGuide = 2, CharaMaking = 3, TalkCharaName = 4, MapName = 5,
    MapEvent = 6, IngameMenu = 7, ItemName = 8, SimpleExplanation = 9, DetailedExplanation = 10,
    Bonfire = 11, NpcMenu = 12, PluralSelect = 13, BloodMessageSentence = 14, BloodMessageWordCategory = 15,
    BloodMessageWord = 16, BloodMessageConjunction = 17, BonfireName = 18, Shop = 19, WeaponType = 20,
    IconHelp = 21, DCOnlyMessage = 22, Win32OnlyMessage = 23, TitleMenu = 24, TitleFlow = 25, Prologue = 26,
}

readonly record struct MsgRef(int Category, int Id);

unsafe static class Game
{
    public static nint Base => Loader.GameBase;
    public static nint At(uint rva) => Base + (nint)rva;

    public static uint TimeDateStamp()
    {
        byte* b = (byte*)Base;
        int peOff = *(int*)(b + 0x3C);
        return *(uint*)(b + peOff + 8);
    }

    public static bool PrologueMatches(uint rva, byte[] expected)
    {
        byte* p = (byte*)At(rva);
        for (int i = 0; i < expected.Length; i++)
            if (p[i] != expected[i]) return false;
        return true;
    }

    /// True if the function starts with a jump another mod placed there (jmp rel32 / jmp [rip+x]).
    public static bool IsDetoured(uint rva)
    {
        byte* p = (byte*)At(rva);
        return p[0] == 0xE9 || p[0] == 0xFF && p[1] == 0x25;
    }

    // ---- original (un-hooked) GetMessage, set by TextHook
    public static delegate* unmanaged<int, int, char*> GetMessageOriginal;

    public static string? GetMessage(MsgRef m)
    {
        if (GetMessageOriginal == null || m.Category == 0 && m.Id == 0) return null;
        char* s = GetMessageOriginal(m.Category, m.Id);
        return s == null ? null : new string(s);
    }

    // ---- static tables (filled by the game's CRT static initialisers; read lazily)

    public readonly record struct KeyEntry(int Index, int Code, MsgRef Us, MsgRef De, MsgRef Fr);
    public readonly record struct ActionEntry(int Index, MsgRef Name, int GameInput, int MenuInput);

    public static KeyEntry[] ReadKeyTable()
    {
        var res = new KeyEntry[Rva.KeyTableCount];
        byte* t = (byte*)At(Rva.KeyTable);
        for (int i = 0; i < res.Length; i++)
        {
            int* e = (int*)(t + i * Rva.KeyTableStride);
            res[i] = new KeyEntry(i, e[6], new MsgRef(e[0], e[1]), new MsgRef(e[2], e[3]), new MsgRef(e[4], e[5]));
        }
        return res;
    }

    public static ActionEntry[] ReadActionTable()
    {
        var res = new ActionEntry[Rva.ActionTableCount];
        byte* t = (byte*)At(Rva.ActionTable);
        for (int i = 0; i < res.Length; i++)
        {
            int* e = (int*)(t + i * Rva.ActionTableStride);
            res[i] = new ActionEntry(i, new MsgRef(e[0], e[1]), e[2], e[3]);
        }
        return res;
    }

    /// Same logic as the game (RVA 0xAF3EB0): 0 = US names, 1 = German layout, 2 = French layout.
    public static int KeyboardLayoutVariant()
    {
        int lang = (int)(GetKeyboardLayout(0) & 0xFF);
        return lang == 0x07 ? 1 : lang == 0x0C ? 2 : 0;
    }

    [DllImport("user32.dll")] static extern nint GetKeyboardLayout(uint thread);
}
