using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassBongoCat;

internal static class GlassMaterialPreview
{
    private static BitmapSource ink;
    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory("artifacts");
        int width = 320, height = 220;
        byte[] mask;
        string prefix = "synthetic";
        if (args.Length > 0)
        {
            var original = new BitmapImage();
            original.BeginInit(); original.UriSource = new Uri(Path.GetFullPath(args[0])); original.CacheOption = BitmapCacheOption.OnLoad; original.EndInit();
            height = Math.Max(1, (int)Math.Round(width * original.PixelHeight / (double)original.PixelWidth));
            var scaled = new DrawingVisual();
            using (var d = scaled.RenderOpen()) d.DrawImage(original, new Rect(0, 0, width, height));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(scaled);
            byte[] rgba = new byte[width * height * 4]; bitmap.CopyPixels(rgba, width * 4, 0);
            mask = new byte[width * height];
            for (int i = 0; i < mask.Length; i++) mask[i] = rgba[i * 4 + 3];
            prefix = args.Length > 2 ? args[2] : "bongo";
            if (args.Length > 1)
            {
                var lines = new BitmapImage();
                lines.BeginInit(); lines.UriSource = new Uri(Path.GetFullPath(args[1])); lines.CacheOption = BitmapCacheOption.OnLoad; lines.EndInit();
                ink = lines;
            }
        }
        else mask = GlassMaterialTests.SyntheticCatMask(width, height);
        var glass = new GlassMaterial(width, height, mask);
        foreach (string scene in new[] { "text", "geometry", "dark" })
        {
            BitmapSource background = Background(width + 96, height + 96, scene);
            byte[] pixels = new byte[background.PixelWidth * background.PixelHeight * 4];
            background.CopyPixels(pixels, background.PixelWidth * 4, 0);
            BitmapSource native = Composite(glass, background, pixels, 1.25, false);
            Save(native, "artifacts/" + prefix + "-glass-" + scene + "-native.png");
            var zoom = new TransformedBitmap(native, new ScaleTransform(4, 4));
            Save(zoom, "artifacts/" + prefix + "-glass-" + scene + "-4x.png");

            var sheet = new DrawingVisual();
            using (var d = sheet.RenderOpen())
            {
                d.DrawRectangle(Brushes.White, null, new Rect(0, 0, width * 3 + 64, height + 108));
                int column = 0;
                foreach (double thickness in new[] { .65, 1.25, 1.8 })
                {
                    double x = 16 + column * (width + 16);
                    Text(d, thickness == 1.25 ? "DEFAULT CRYSTAL / 1.25" : "THICKNESS / " + thickness.ToString("F2", CultureInfo.InvariantCulture), new Point(x, 18), 13, Brushes.Black);
                    BitmapSource material = Composite(glass, background, pixels, thickness, false);
                    d.DrawImage(material, new Rect(x, 46, width, height));
                    Text(d, "Ray travel: " + glass.MaximumDisplacement.ToString("F1") + " px", new Point(x, height + 66), 12, Brushes.DimGray);
                    column++;
                }
            }
            var comparison = new RenderTargetBitmap(width * 3 + 64, height + 108, 96, 96, PixelFormats.Pbgra32); comparison.Render(sheet);
            Save(comparison, "artifacts/" + prefix + "-glass-" + scene + "-thickness.png");
            Save(Composite(glass, background, pixels, 1.25, true), "artifacts/" + prefix + "-glass-" + scene + "-fallback.png");
        }
        Console.WriteLine("PASS: offscreen " + prefix + " crystal previews written at native/4x scale, three thicknesses, and transparent fallback.");
        return 0;
    }
    private static BitmapSource Composite(GlassMaterial glass, BitmapSource background, byte[] pixels, double thickness, bool fallback)
    {
        byte[] output;
        if (fallback) { glass.RenderFallback(1, 1, thickness); output = glass.FallbackPixels; }
        else
        {
            glass.Render(pixels, background.PixelWidth, background.PixelHeight, background.PixelWidth * 4, 48, 48, 1, 1, 1, 1, 1, thickness);
            output = glass.Pixels;
        }
        BitmapSource material = BitmapSource.Create(glass.Width, glass.Height, 96, 96, PixelFormats.Pbgra32, null, output, glass.Width * 4);
        var visual = new DrawingVisual();
        using (var d = visual.RenderOpen())
        {
            d.DrawImage(background, new Rect(-48, -48, background.PixelWidth, background.PixelHeight));
            d.DrawImage(material, new Rect(0, 0, glass.Width, glass.Height));
            if (ink != null) d.DrawImage(ink, new Rect(0, 0, glass.Width, glass.Height));
        }
        var result = new RenderTargetBitmap(glass.Width, glass.Height, 96, 96, PixelFormats.Pbgra32); result.Render(visual);
        return result;
    }
    private static BitmapSource Background(int width, int height, string scene)
    {
        var v = new DrawingVisual();
        using (var d = v.RenderOpen())
        {
            bool dark = scene == "dark";
            d.DrawRectangle(dark ? B(27, 34, 41) : B(247, 245, 239), null, new Rect(0, 0, width, height));
            if (scene == "geometry")
            {
                d.DrawRectangle(B(61, 161, 151), null, new Rect(35, 0, 65, height));
                d.DrawRectangle(B(232, 109, 60), null, new Rect(160, 0, 22, height));
                d.DrawEllipse(B(86, 122, 194), null, new Point(width - 100, 102), 57, 57);
                d.DrawRectangle(B(238, 190, 69), null, new Rect(width - 145, height - 110, 150, 56));
            }
            for (int y = 10; y < height; y += 24)
                d.DrawLine(new Pen(dark ? B(67, 86, 99) : B(171, 179, 183), 1), new Point(0, y), new Point(width, y));
            for (int x = 11; x < width; x += 28)
                d.DrawLine(new Pen(dark ? B(67, 86, 99) : B(206, 211, 209), 1), new Point(x, 0), new Point(x, height));
            Brush ink = dark ? B(229, 236, 231) : B(32, 43, 48);
            Text(d, "THICK CRYSTAL", new Point(60, 72), 22, ink);
            Text(d, "A clear center.", new Point(72, 118), 18, ink);
            Text(d, "0123456789", new Point(65, 160), 24, ink);
            Text(d, "REAL BACKGROUND", new Point(59, 211), 17, ink);
        }
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); result.Render(v); return result;
    }
    private static Brush B(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }
    private static void Text(DrawingContext d, string text, Point point, double size, Brush brush)
    { d.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.0), point); }
    private static void Save(BitmapSource image, string path)
    { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var file = File.Create(path)) encoder.Save(file); }
}
