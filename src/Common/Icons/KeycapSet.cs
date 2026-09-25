using DynamicKeyPrompts.Formats;

namespace DynamicKeyPrompts.Icons;

/// The glyphs added to the game font: one per bindable key code (the game's internal key codes), placed in
/// the Private Use Area at U+E000 + key code, plus "move the mouse" at U+E0FE.
/// Name = file name (without .png) of a user icon that replaces the generated one.
public static class KeycapSet
{
    public const char Base = (char)0xE000;
    public const char MouseMoveChar = (char)0xE0FE;

    public static char CharFor(int code) => (char)(Base + code);

    /// Bumped whenever the generated look or the set changes, so cached fonts are rebuilt.
    public const int Version = 4;

    public readonly record struct Entry(int Code, string Name, KeycapSpec Spec);

    public static IEnumerable<Entry> All()
    {
        yield return new(0, "Mouse_Left", new KeycapSpec.Mouse(MouseButton.Left));
        yield return new(1, "Mouse_Right", new KeycapSpec.Mouse(MouseButton.Right));
        yield return new(2, "Mouse_Middle", new KeycapSpec.Mouse(MouseButton.Middle));
        yield return new(6, "Wheel_Up", new KeycapSpec.Mouse(MouseButton.Middle, WheelDirection: +1));
        yield return new(7, "Wheel_Down", new KeycapSpec.Mouse(MouseButton.Middle, WheelDirection: -1));
        yield return new(11, "Mouse_Left_Double", new KeycapSpec.Mouse(MouseButton.Left, DoubleClick: true));
        yield return new(12, "Mouse_Right_Double", new KeycapSpec.Mouse(MouseButton.Right, DoubleClick: true));
        yield return new(MouseMoveChar - Base, "Mouse_Move", new KeycapSpec.MouseMove());

        foreach (var (code, name, label) in Keys())
            yield return new(code, name, new KeycapSpec.Key(label));
    }

    static IEnumerable<(int Code, string Name, string Label)> Keys()
    {
        // Letters and digits: internal code = DIK scancode + 0x45.
        const string rowQ = "QWERTYUIOP", rowA = "ASDFGHJKL", rowZ = "ZXCVBNM";
        for (int i = 0; i < rowQ.Length; i++) yield return (0x10 + i + 0x45, rowQ[i].ToString(), rowQ[i].ToString());
        for (int i = 0; i < rowA.Length; i++) yield return (0x1E + i + 0x45, rowA[i].ToString(), rowA[i].ToString());
        for (int i = 0; i < rowZ.Length; i++) yield return (0x2C + i + 0x45, rowZ[i].ToString(), rowZ[i].ToString());
        for (int i = 1; i <= 9; i++) yield return (0x01 + i + 0x45, i.ToString(), i.ToString());
        yield return (0x0B + 0x45, "0", "0");

        yield return (70, "Esc", "Esc");
        yield return (83, "Backspace", "Bksp");
        yield return (84, "Tab", "Tab");
        yield return (97, "Enter", "Enter");
        yield return (98, "Ctrl", "Ctrl");
        yield return (111, "Shift", "Shift");
        yield return (123, "RShift", "Shift");
        yield return (125, "Alt", "Alt");
        yield return (126, "Space", "Space");
        yield return (176, "RCtrl", "Ctrl");
        yield return (187, "AltGr", "AltGr");
        yield return (189, "Home", "Home");
        yield return (190, "Up", "↑");
        yield return (191, "PageUp", "PgUp");
        yield return (192, "Left", "←");
        yield return (193, "Right", "→");
        yield return (194, "End", "End");
        yield return (195, "Down", "↓");
        yield return (196, "PageDown", "PgDn");
        yield return (197, "Insert", "Ins");
        yield return (198, "Delete", "Del");
        int[] numpad = [151, 148, 149, 150, 144, 145, 146, 140, 141, 142];
        for (int i = 0; i < 10; i++) yield return (numpad[i], $"Num{i}", $"N{i}");
        yield return (175, "NumEnter", "NEnt");
        yield return (185, "NumDivide", "N/");
        yield return (124, "NumMultiply", "N*");
        yield return (143, "NumMinus", "N-");
        yield return (147, "NumPlus", "N+");
        yield return (152, "NumDecimal", "N.");
    }
}
