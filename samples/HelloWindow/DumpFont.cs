namespace HelloWindow;

internal static class DumpFont
{
    // --dump-hud: print the rendered overlay as ASCII art (font sanity check).
    public static int Run()
    {
        var (w, h, px) = TextOverlay.Render(["R 0.42 G 0.81", "SPACE: HUE"], 1);
        for (var y = 0; y < h; y++)
        {
            var line = new char[w];
            for (var x = 0; x < w; x++)
                line[x] = px[(y * w + x) * 4] > 100 ? '#' : ' ';
            Console.WriteLine(new string(line));
        }
        return 0;
    }
}
