namespace DynamicKeyPrompts;

/// Maps an FMG object pointer back to its message category using the game's MsgRepository
/// (the same table GetMessage indexes: Fmg* at repo + 0x1D8 + category * 0x20).
static class MessageRepository
{
    const int CategoryCount = 27, TableOffset = 0x1D8, Stride = 0x20;
    static readonly object s_lock = new();
    static Dictionary<nint, int> s_map = new();
    static nint s_repo;

    /// Category of the FMG, or -1 if it is not one of the repository's (DLC / other files).
    public static int CategoryOf(nint fmg)
    {
        lock (s_lock)
        {
            if (s_map.TryGetValue(fmg, out int c)) return c;
            // Languages can be reloaded, which rebuilds the repository: refresh on a miss.
            Refresh();
            return s_map.TryGetValue(fmg, out c) ? c : -1;
        }
    }

    static void Refresh()
    {
        if (!Memory.TryReadPtr(Game.At(Rva.MsgRepository), out nint repo) || repo == 0) return;
        var table = Memory.Read(repo + TableOffset, CategoryCount * Stride);
        if (table == null) return;
        var map = new Dictionary<nint, int>();
        for (int c = 0; c < CategoryCount; c++)
        {
            nint p = (nint)BitConverter.ToInt64(table, c * Stride);
            if (p != 0) map[p] = c;
        }
        if (repo != s_repo || map.Count != s_map.Count)
            Loader.Log($"messages: repository {repo:X}, {map.Count} categories");
        s_repo = repo;
        s_map = map;
    }
}
