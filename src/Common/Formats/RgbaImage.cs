namespace DynamicKeyPrompts.Formats;

/// An 8-bit RGBA image (straight alpha), rows top to bottom.
public sealed record RgbaImage(int Width, int Height, byte[] Rgba);
