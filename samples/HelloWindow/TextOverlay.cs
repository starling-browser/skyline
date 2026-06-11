namespace HelloWindow;

/// <summary>
/// CPU-rasterized text panel using a built-in 5x7 pixel font (uppercase,
/// digits, and a little punctuation). Produces BGRA8 pixels that the
/// renderer copies into the swapchain texture — no shader pipeline needed
/// for a sample-sized overlay.
/// </summary>
internal static class TextOverlay
{
    private const int GlyphW = 5;
    private const int GlyphH = 7;
    private const int Pad = 8;       // panel padding, pre-scale
    private const int LineGap = 3;   // rows between lines, pre-scale

    public static (int Width, int Height, byte[] Bgra) Render(string[] lines, int scale)
    {
        scale = Math.Max(1, scale);
        var cols = 0;
        foreach (var line in lines) cols = Math.Max(cols, line.Length);
        var w = (cols * (GlyphW + 1) + Pad * 2) * scale;
        var h = (lines.Length * (GlyphH + LineGap) - LineGap + Pad * 2) * scale;
        var px = new byte[w * h * 4];

        // Panel background: dark, mostly opaque (alpha is ignored by an
        // opaque swapchain, so pre-darken instead of relying on blending).
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = 24; px[i + 1] = 22; px[i + 2] = 20; px[i + 3] = 255;
        }

        for (var li = 0; li < lines.Length; li++)
        {
            var y0 = (Pad + li * (GlyphH + LineGap)) * scale;
            for (var ci = 0; ci < lines[li].Length; ci++)
            {
                var glyph = Glyph(lines[li][ci]);
                var x0 = (Pad + ci * (GlyphW + 1)) * scale;
                for (var gx = 0; gx < GlyphW; gx++)
                {
                    var colBits = glyph[gx];
                    for (var gy = 0; gy < GlyphH; gy++)
                    {
                        if ((colBits & (1 << gy)) == 0) continue;
                        for (var sy = 0; sy < scale; sy++)
                        for (var sx = 0; sx < scale; sx++)
                        {
                            var x = x0 + gx * scale + sx;
                            var y = y0 + gy * scale + sy;
                            var o = (y * w + x) * 4;
                            px[o] = 235; px[o + 1] = 240; px[o + 2] = 245; px[o + 3] = 255;
                        }
                    }
                }
            }
        }
        return (w, h, px);
    }

    // Classic 5x7 font, column-major, bit 0 = top row. Unknown chars render
    // as space.
    private static byte[] Glyph(char c) => char.ToUpperInvariant(c) switch
    {
        '0' => [0x3E, 0x51, 0x49, 0x45, 0x3E],
        '1' => [0x00, 0x42, 0x7F, 0x40, 0x00],
        '2' => [0x42, 0x61, 0x51, 0x49, 0x46],
        '3' => [0x21, 0x41, 0x45, 0x4B, 0x31],
        '4' => [0x18, 0x14, 0x12, 0x7F, 0x10],
        '5' => [0x27, 0x45, 0x45, 0x45, 0x39],
        '6' => [0x3C, 0x4A, 0x49, 0x49, 0x30],
        '7' => [0x01, 0x71, 0x09, 0x05, 0x03],
        '8' => [0x36, 0x49, 0x49, 0x49, 0x36],
        '9' => [0x06, 0x49, 0x49, 0x29, 0x1E],
        ':' => [0x00, 0x36, 0x36, 0x00, 0x00],
        '.' => [0x00, 0x60, 0x60, 0x00, 0x00],
        '-' => [0x08, 0x08, 0x08, 0x08, 0x08],
        'A' => [0x7E, 0x11, 0x11, 0x11, 0x7E],
        'B' => [0x7F, 0x49, 0x49, 0x49, 0x36],
        'C' => [0x3E, 0x41, 0x41, 0x41, 0x22],
        'D' => [0x7F, 0x41, 0x41, 0x22, 0x1C],
        'E' => [0x7F, 0x49, 0x49, 0x49, 0x41],
        'F' => [0x7F, 0x09, 0x09, 0x09, 0x01],
        'G' => [0x3E, 0x41, 0x49, 0x49, 0x7A],
        'H' => [0x7F, 0x08, 0x08, 0x08, 0x7F],
        'I' => [0x00, 0x41, 0x7F, 0x41, 0x00],
        'J' => [0x20, 0x40, 0x41, 0x3F, 0x01],
        'K' => [0x7F, 0x08, 0x14, 0x22, 0x41],
        'L' => [0x7F, 0x40, 0x40, 0x40, 0x40],
        'M' => [0x7F, 0x02, 0x0C, 0x02, 0x7F],
        'N' => [0x7F, 0x04, 0x08, 0x10, 0x7F],
        'O' => [0x3E, 0x41, 0x41, 0x41, 0x3E],
        'P' => [0x7F, 0x09, 0x09, 0x09, 0x06],
        'Q' => [0x3E, 0x41, 0x51, 0x21, 0x5E],
        'R' => [0x7F, 0x09, 0x19, 0x29, 0x46],
        'S' => [0x46, 0x49, 0x49, 0x49, 0x31],
        'T' => [0x01, 0x01, 0x7F, 0x01, 0x01],
        'U' => [0x3F, 0x40, 0x40, 0x40, 0x3F],
        'V' => [0x1F, 0x20, 0x40, 0x20, 0x1F],
        'W' => [0x3F, 0x40, 0x38, 0x40, 0x3F],
        'X' => [0x63, 0x14, 0x08, 0x14, 0x63],
        'Y' => [0x07, 0x08, 0x70, 0x08, 0x07],
        'Z' => [0x61, 0x51, 0x49, 0x45, 0x43],
        _ => [0, 0, 0, 0, 0],
    };
}
