using System;
using System.Windows;

namespace GlassBongoCat
{
    /// <summary>
    /// A tightly packed BGRA desktop sample owned by the native capture pipeline.
    /// Dimensions and storage never change. The sampler updates ScreenBounds only
    /// when reusing this frame. A delivered UI snapshot is stable until the next
    /// successful TryGetSnapshot; worker publication never writes into it.
    /// </summary>
    internal sealed class BackdropFrame
    {
        internal readonly int Width, Height, Stride;
        internal readonly byte[] Pixels;
        internal Rect ScreenBounds;

        internal BackdropFrame(int width, int height)
        {
            if (width < 1 || height < 1 || (long)width * height > 2 * 1024 * 1024)
                throw new ArgumentOutOfRangeException("width", "Desktop sample exceeds the 2 megapixel budget.");
            Width = width; Height = height; Stride = checked(width * 4);
            Pixels = new byte[checked(Stride * height)];
            ScreenBounds = Rect.Empty;
        }
    }
}
