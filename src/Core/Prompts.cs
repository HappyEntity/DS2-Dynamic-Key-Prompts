namespace DynamicKeyPrompts;

/// Pad input ids as used by the game's action table (gameInput / menuInput columns).
enum Pad
{
    DUp = 0, DDown = 1, DLeft = 2, DRight = 3, Start = 4, Back = 5,
    LB = 6, RB = 7, LT = 8, RT = 9, LS = 10, RS = 11, A = 12, B = 13, X = 14, Y = 15,
}

enum PromptContext { World, Menu }

/// What a pad glyph in FMG text stands for.
abstract record Glyph
{
    /// One pad button; resolved to a game input through the game's action table.
    public sealed record Button(Pad Pad) : Glyph;
    /// A stick or d-pad drawn as one icon; stands for several inputs shown together
    /// (listed up, left, down, right — the WASD order).
    public sealed record Directions(int[] WorldInputs, int[] MenuInputs, bool IsCamera = false) : Glyph;
}

static class Glyphs
{
    static readonly Glyph Move = new Glyph.Directions(
        [Input.RunForward, Input.RunLeft, Input.RunBackward, Input.RunRight],
        [Input.CursorUp, Input.CursorLeft, Input.CursorDown, Input.CursorRight]);
    static readonly Glyph Camera = new Glyph.Directions(
        [Input.CameraUp, Input.CameraLeft, Input.CameraDown, Input.CameraRight],
        [Input.CameraUp, Input.CameraLeft, Input.CameraDown, Input.CameraRight], IsCamera: true);
    static readonly Glyph DPad = new Glyph.Directions(
        [(int)Pad.DUp, (int)Pad.DLeft, (int)Pad.DDown, (int)Pad.DRight],
        [Input.CursorUp, Input.CursorLeft, Input.CursorDown, Input.CursorRight]);
    static readonly Glyph DPadLR = new Glyph.Directions(
        [(int)Pad.DLeft, (int)Pad.DRight],
        [Input.CursorLeft, Input.CursorRight]);
    static readonly Glyph DPadUD = new Glyph.Directions(
        [(int)Pad.DUp, (int)Pad.DDown],
        [Input.CursorUp, Input.CursorDown]);

    /// Codepoints of the pad icons in FeFont_Small/Big (verified against the font atlas).
    public static Glyph? Get(char c) => c switch
    {
        '①' => new Glyph.Button(Pad.Y),
        '②' => new Glyph.Button(Pad.X),
        '③' or '⑳' => new Glyph.Button(Pad.B),
        '④' or '⑲' => new Glyph.Button(Pad.A),
        '⑤' => DPad,
        '⑥' => new Glyph.Button(Pad.Back),
        '⑦' or '㌧' => new Glyph.Button(Pad.RB),
        '⑧' => new Glyph.Button(Pad.RT),
        '⑨' or '㌦' => new Glyph.Button(Pad.LB),
        '⑩' => new Glyph.Button(Pad.LT),
        '⑪' => new Glyph.Button(Pad.RS),
        '⑫' => new Glyph.Button(Pad.LS),
        '⑭' => DPadLR,
        '⑮' => DPadUD,
        '⑯' => new Glyph.Button(Pad.Start),
        '⑰' or '㎏' => Camera,
        '⑱' or '㎎' => Move,
        '㊤' => new Glyph.Button(Pad.DUp),
        '㊦' => new Glyph.Button(Pad.DDown),
        '㊧' => new Glyph.Button(Pad.DLeft),
        '㊨' => new Glyph.Button(Pad.DRight),
        _ => null,
    };

    public static bool Any(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
            if (c >= '①' && c <= '⑳' || c >= '㊤' && c <= '㊨' || c is '㌦' or '㌧' or '㎎' or '㎏')
                return true;
        return false;
    }

    /// World prompts: interaction prompts (keyguide) and tutorial messages (mapevent).
    public static PromptContext ContextOf(int category) =>
        category is (int)MsgCategory.KeyGuide or (int)MsgCategory.MapEvent ? PromptContext.World : PromptContext.Menu;
}
