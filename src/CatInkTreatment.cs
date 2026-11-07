using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBongoCat
{
    // Optional original-pixel treatment. Call once per pose and cache the result;
    // the original CatArtwork.GetInk() remains available for exact-source rendering.
    internal static class CatInkTreatment
    {
        internal static BitmapSource SoftenOuterInk(BitmapSource ink, byte[] mask,
            int width, int height, double opacity)
        {
            if (ink == null || mask == null || mask.Length != width * height ||
                ink.PixelWidth != width || ink.PixelHeight != height)
                throw new ArgumentException("Ink and original cat alpha mask must have the same dimensions.");
            opacity = Math.Max(0, Math.Min(1, opacity));
            byte[] pixels = new byte[width * height * 4];
            new FormatConvertedBitmap(ink, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, width * 4, 0);
            const int radius = 6;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int at = (y * width + x) * 4;
                    if (pixels[at + 3] == 0) continue;
                    int min = Math.Min(pixels[at], Math.Min(pixels[at + 1], pixels[at + 2]));
                    int max = Math.Max(pixels[at], Math.Max(pixels[at + 1], pixels[at + 2]));
                    // Colored original paw pads always retain their exact pixels.
                    if (max - min > 15) continue;
                    bool edge = false;
                    for (int dy = -radius; dy <= radius && !edge; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (dx * dx + dy * dy > radius * radius) continue;
                            int px = x + dx, py = y + dy;
                            if (px < 0 || py < 0 || px >= width || py >= height || mask[py * width + px] < 128)
                            { edge = true; break; }
                        }
                    if (edge) pixels[at + 3] = (byte)Math.Round(pixels[at + 3] * opacity);
                }
            BitmapSource result = BitmapSource.Create(width, height, 96, 96,
                PixelFormats.Bgra32, null, pixels, width * 4);
            result.Freeze();
            return result;
        }
    }
}
