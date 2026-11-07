using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBongoCat
{
    /// <summary>
    /// Original Bongo Cat Mver pixels, separated into glass alpha and original ink.
    /// Pose bit 0 is left down; bit 1 is right down. No cat outline is redrawn here.
    /// </summary>
    internal sealed class CatArtwork
    {
        internal const int CanvasWidth = 612;
        internal const int CanvasHeight = 354;
        private readonly byte[][] masks = new byte[4][];
        private readonly BitmapSource[] inks = new BitmapSource[4];
        private readonly BitmapSource desk;
        internal Rect BodyBounds { get; private set; }
        internal int MaskWidth { get; private set; }
        internal int MaskHeight { get; private set; }

        private CatArtwork()
        {
            // Exact common pixel crop, produced by assets/cat/ExtractArtwork.cs.
            // The original 612 x 354 coordinate system is retained for every pose.
            BodyBounds = ReadBounds();
            MaskWidth = (int)BodyBounds.Width;
            MaskHeight = (int)BodyBounds.Height;
            for (int pose = 0; pose < 4; pose++)
            {
                BitmapSource mask = ReadBitmap("body-mask-" + pose + ".png");
                inks[pose] = ReadBitmap("body-ink-" + pose + ".png");
                if (mask.PixelWidth != MaskWidth || mask.PixelHeight != MaskHeight ||
                    inks[pose].PixelWidth != MaskWidth || inks[pose].PixelHeight != MaskHeight)
                    throw new InvalidDataException("Bongo Cat pose dimensions do not match their source crop.");
                FormatConvertedBitmap converted = new FormatConvertedBitmap(mask, PixelFormats.Bgra32, null, 0);
                byte[] pixels = new byte[MaskWidth * MaskHeight * 4];
                converted.CopyPixels(pixels, MaskWidth * 4, 0);
                masks[pose] = new byte[MaskWidth * MaskHeight];
                for (int i = 0; i < masks[pose].Length; i++) masks[pose][i] = pixels[i * 4 + 3];
            }
            desk = ReadBitmap("desk-ink.png");
        }

        internal static CatArtwork Load() { return new CatArtwork(); }
        internal byte[] GetMask(int pose) { return masks[pose & 3]; }
        internal BitmapSource GetInk(int pose) { return inks[pose & 3]; }
        internal void DrawBase(DrawingContext drawing)
        {
            drawing.DrawImage(desk, new Rect(0, 0, CanvasWidth, CanvasHeight));
        }

        private static Rect ReadBounds()
        {
            using (Stream stream = Open("geometry.txt"))
            using (StreamReader reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("BodyBounds=", StringComparison.Ordinal)) continue;
                    string[] v = line.Substring(11).Split(',');
                    if (v.Length != 4) break;
                    return new Rect(Int32.Parse(v[0]), Int32.Parse(v[1]), Int32.Parse(v[2]), Int32.Parse(v[3]));
                }
            }
            throw new InvalidDataException("Missing Bongo Cat source geometry.");
        }

        private static BitmapSource ReadBitmap(string file)
        {
            using (Stream stream = Open(file))
            {
                BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }

        private static Stream Open(string file)
        {
            Assembly assembly = typeof(CatArtwork).Assembly;
            string suffix = ".cat." + file;
            foreach (string name in assembly.GetManifestResourceNames())
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return assembly.GetManifestResourceStream(name);
            throw new FileNotFoundException("Missing embedded original Bongo Cat artwork: " + file);
        }
    }
}
