using System;
using System.Diagnostics;
using GlassBongoCat;

internal static class GlassMaterialTests
{
    private static int assertions;
    private static int Main(string[] args)
    {
        try
        {
            Reject(delegate { new GlassMaterial(0, 10, new byte[0]); }, "invalid dimensions");
            Reject(delegate { new GlassMaterial(2048, 2048, new byte[0]); }, "pixel budget");
            Reject(delegate { new GlassMaterial(10, 10, new byte[99]); }, "dense mask length");
            byte[] mask = SyntheticCatMask(190, 190);
            var glass = new GlassMaterial(190, 190, mask);
            byte[] ramp = Ramp(256, 256, 1040);
            glass.Render(ramp, 256, 256, 1040, 0, 0, 1, 1, 0, 0, 1);
            for (int i = 0; i < mask.Length; i++)
            {
                int x = i % 190, y = i / 190;
                Assert(glass.Pixels[i * 4 + 3] == mask[i], "input silhouette alpha is preserved exactly");
                Assert(glass.Pixels[i * 4 + 2] == (x * mask[i] + 127) / 255, "disabled optics preserves source including antialiasing");
                Assert(glass.Pixels[i * 4 + 1] == (y * mask[i] + 127) / 255, "padded source rows are respected");
            }
            int initialVersion = glass.MapVersion;
            glass.Render(ramp, 256, 256, 1040, 0, 0, 1, 1, 1, 0, 1);
            Assert(glass.MapVersion == initialVersion, "changing refraction reuses geometry");
            Assert(glass.MaximumDisplacement >= 20, "default crystal has a substantial optical path");
            int visibleBend = glass.Pixels[(115 * 190 + 25) * 4 + 2] - 25;
            Assert(visibleBend >= 17, "the shoulder visibly displaces the real image, including its rear layer");
            Assert(Math.Abs(glass.Pixels[(115 * 190 + 95) * 4 + 2] - 95) <= 1, "central text is only gently magnified");
            glass.SetMask((byte[])mask.Clone());
            Assert(glass.MapVersion == initialVersion, "identical alpha does not rebuild the distance field");
            glass.Render(ramp, 256, 256, 1040, .3, .5, 1, 1, 2, 1.5, .8);
            Assert(glass.MapVersion == initialVersion, "position and material strength preserve the cached mapping");

            glass.Render(ramp, 256, 256, 1040, 0, 0, 1, 1, 1, 0, 1, .65);
            double shallow = glass.MaximumDisplacement;
            glass.Render(ramp, 256, 256, 1040, 0, 0, 1, 1, 1, 0, 1, 1.8);
            Assert(glass.MaximumDisplacement > shallow * 1.7, "thickness changes ray travel, rather than merely whitening glass");
            Assert(glass.MapVersion == initialVersion + 2, "only thickness transitions rebuilt optics");

            var neutral = new byte[] { 110, 110, 110, 255 };
            glass.Render(neutral, 1, 1, 4, -10000, 10000, 1, 1, 2.5, 2, 1, 1.8);
            for (int i = 0; i < mask.Length; i++)
            {
                int p = i * 4, a = glass.Pixels[p + 3];
                Assert(glass.Pixels[p] == glass.Pixels[p + 1] && glass.Pixels[p + 1] == glass.Pixels[p + 2], "neutral glass never acquires a blue tint");
                Assert(glass.Pixels[p] <= a && glass.Pixels[p + 1] <= a && glass.Pixels[p + 2] <= a, "output is premultiplied at every shoulder pixel");
            }
            var strip = new byte[] { 10, 10, 10, 255, 230, 230, 230, 255 };
            glass.Render(strip, 1, 2, 4, 100, -30, 2, 2);
            Assert(glass.MeanBrightness >= 0 && glass.MeanBrightness <= 1, "single-column capture remains valid");
            byte[] rendered = (byte[])glass.Pixels.Clone();
            glass.RenderFallback();
            int fallbackVersion = glass.FallbackVersion;
            glass.RenderFallback();
            Assert(glass.FallbackVersion == fallbackVersion, "unchanged fallback parameters reuse the cached overlay");
            Assert(glass.FallbackPixels[(115 * 190 + 95) * 4 + 3] <= 5, "fallback interior remains truly transparent");
            int strongestEdge = 0;
            for (int i = 0; i < glass.Pixels.Length; i += 4)
            {
                Assert(glass.Pixels[i] == rendered[i] && glass.Pixels[i + 3] == rendered[i + 3], "fallback generation cannot overwrite refracted pixels");
                Assert(glass.FallbackPixels[i] <= glass.FallbackPixels[i + 3], "fallback is also premultiplied");
                strongestEdge = Math.Max(strongestEdge, glass.FallbackPixels[i + 3]);
            }
            Assert(strongestEdge >= 100 && strongestEdge < 245, "fallback retains a substantial but transparent crystal edge");
            var full = new GlassMaterial(3, 3, new byte[] {255,255,255,255,0,255,255,255,255});
            full.Render(neutral, 1, 1, 4, 0, 0, 1, 1);
            Assert(full.Pixels[19] == 0, "holes inside arbitrary masks remain transparent");
            var single = new GlassMaterial(1, 1, new byte[] {128});
            single.Render(neutral, 1, 1, 4, 0, 0, 1, 1);
            Assert(single.Pixels[3] == 128, "one-pixel silhouettes are safe");
            glass.SetMask(new byte[190 * 190]);
            glass.Render(neutral, 1, 1, 4, 0, 0, 1, 1);
            for (int i = 0; i < glass.Pixels.Length; i++) Assert(glass.Pixels[i] == 0, "replacing the mask clears every old pixel");
            Assert(glass.MeanBrightness == 0, "empty silhouette has zero luminance");
            Reject(delegate { glass.Render(null, 1, 1, 4, 0, 0, 1, 1); }, "null source");
            Reject(delegate { glass.Render(neutral, 2, 1, 4, 0, 0, 1, 1); }, "invalid source row");
            Reject(delegate { glass.Render(neutral, 1, 2, 4, 0, 0, 1, 1); }, "invalid source length");
            Reject(delegate { glass.Render(neutral, 1, 1, 4, 0, 0, 0, 1); }, "invalid sampling scale");
            Reject(delegate { glass.Render(neutral, 1, 1, 4, 0, 0, 1, 1, Double.NaN); }, "NaN refraction");
            Reject(delegate { glass.Render(neutral, 1, 1, 4, 0, 0, 1, 1, 1, 1, 1, Double.PositiveInfinity); }, "infinite thickness");
            Console.WriteLine("PASS: " + assertions + " mask/optics assertions; visible shoulder bend=" + visibleBend + " px at 190 px.");
            if (Array.IndexOf(args, "--perf") >= 0) Performance();
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    internal static byte[] SyntheticCatMask(int width, int height)
    {
        var result = new byte[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int inside = 0;
            for (int yy = 0; yy < 4; yy++) for (int xx = 0; xx < 4; xx++)
            {
                double u = (x + (xx + .5) / 4) / width, v = (y + (yy + .5) / 4) / height;
                double dx = (u - .5) / .43, dy = (v - .61) / .35;
                bool body = dx * dx + dy * dy <= 1;
                bool left = Triangle(u, v, .12, .46, .20, .055, .40, .31);
                bool right = Triangle(u, v, .88, .46, .80, .055, .60, .31);
                if (body || left || right) inside++;
            }
            result[y * width + x] = (byte)((inside * 255 + 8) / 16);
        }
        return result;
    }
    private static bool Triangle(double x, double y, double ax, double ay, double bx, double by, double cx, double cy)
    {
        double a = (x - bx) * (ay - by) - (ax - bx) * (y - by);
        double b = (x - cx) * (by - cy) - (bx - cx) * (y - cy);
        double c = (x - ax) * (cy - ay) - (cx - ax) * (y - ay);
        return !((a < 0 || b < 0 || c < 0) && (a > 0 || b > 0 || c > 0));
    }
    private static byte[] Ramp(int width, int height, int stride)
    {
        var data = new byte[stride * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        { int i = y * stride + x * 4; data[i] = 80; data[i + 1] = (byte)y; data[i + 2] = (byte)x; data[i + 3] = 255; }
        return data;
    }
    private static void Performance()
    {
        foreach (int size in new[] {190, 320})
        {
            var glass = new GlassMaterial(size, size, SyntheticCatMask(size, size));
            byte[] source = Ramp(512, 512, 2048);
            for (int i = 0; i < 60; i++) glass.Render(source, 512, 512, 2048, 50, 50, 1, 1);
            int collections = GC.CollectionCount(0);
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 300; i++) glass.Render(source, 512, 512, 2048, 50 + (i % 5) * .1, 50, 1, 1);
            timer.Stop();
            Console.WriteLine("PERF " + size + "px: " + (timer.Elapsed.TotalMilliseconds / 300).ToString("F3") + " ms/frame; Gen0=" + (GC.CollectionCount(0) - collections));
        }
    }
    private static void Assert(bool value, string message)
    { if (!value) throw new Exception("FAIL: " + message); assertions++; }
    private static void Reject(Action action, string message)
    { try { action(); } catch (ArgumentException) { assertions++; return; } throw new Exception("FAIL: expected rejection: " + message); }
}
