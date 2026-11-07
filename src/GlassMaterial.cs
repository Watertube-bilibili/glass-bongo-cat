using System;

namespace GlassBongoCat
{
    /// <summary>
    /// An independently authored, mask-driven thick crystal material.
    /// A distance field gives the silhouette a rounded shoulder and a flat,
    /// readable interior. Front and rear paths sample different background
    /// positions in the shoulder; neutral reflection and extinction describe
    /// the two surfaces without painting a colored or opaque body fill.
    /// Source pixels are opaque BGRA. Pixels is premultiplied BGRA.
    /// </summary>
    internal sealed class GlassMaterial
    {
        internal readonly int Width, Height;
        internal readonly byte[] Pixels;
        internal readonly byte[] FallbackPixels;
        internal double MeanBrightness { get; private set; }
        internal int MapVersion { get; private set; }
        internal int FallbackVersion { get; private set; }
        internal double MaximumDisplacement { get; private set; }

        private readonly byte[] mask;
        private readonly float[] distance, gradientX, gradientY;
        private readonly float[] offsetX, offsetY, rearX, rearY;
        private readonly float[] rearWeight, reflection, extinction;
        private readonly int[] active;
        private int activeCount;
        private bool hasMask;
        private double centerX, centerY, shapeScale, builtThickness = -1;
        private int fallbackMapVersion = -1;
        private double fallbackHighlight = -1, fallbackClarity = -1;
        private const double DefaultThickness = 1.25;

        internal GlassMaterial(int width, int height, byte[] alphaMask)
        {
            if (width < 1 || height < 1 || (long)width * height > 1048576)
                throw new ArgumentOutOfRangeException("width", "The mask must fit the one-million-pixel material budget.");
            Width = width; Height = height;
            int count = width * height;
            Pixels = new byte[count * 4]; FallbackPixels = new byte[count * 4]; mask = new byte[count]; active = new int[count];
            distance = new float[count]; gradientX = new float[count]; gradientY = new float[count];
            offsetX = new float[count]; offsetY = new float[count]; rearX = new float[count]; rearY = new float[count];
            rearWeight = new float[count]; reflection = new float[count]; extinction = new float[count];
            SetMask(alphaMask);
        }

        internal void SetMask(byte[] alphaMask)
        {
            if (alphaMask == null) throw new ArgumentNullException("alphaMask");
            if (alphaMask.Length != mask.Length) throw new ArgumentException("The alpha mask must contain exactly width * height bytes.", "alphaMask");
            bool same = hasMask;
            if (same) for (int i = 0; i < mask.Length; i++) if (mask[i] != alphaMask[i]) { same = false; break; }
            if (same) return;
            Buffer.BlockCopy(alphaMask, 0, mask, 0, mask.Length);
            Array.Clear(Pixels, 0, Pixels.Length);
            Array.Clear(FallbackPixels, 0, FallbackPixels.Length);
            activeCount = 0;
            double weightedX = 0, weightedY = 0, weight = 0;
            int left = Width, top = Height, right = -1, bottom = -1;
            for (int y = 0, i = 0; y < Height; y++)
                for (int x = 0; x < Width; x++, i++)
                {
                    int alpha = mask[i];
                    if (alpha == 0) { distance[i] = 0; continue; }
                    active[activeCount++] = i;
                    weightedX += (x + .5) * alpha; weightedY += (y + .5) * alpha; weight += alpha;
                    if (x < left) left = x; if (x > right) right = x;
                    if (y < top) top = y; if (y > bottom) bottom = y;
                }
            centerX = weight == 0 ? Width * .5 : weightedX / weight;
            centerY = weight == 0 ? Height * .5 : weightedY / weight;
            shapeScale = weight == 0 ? 1 : Math.Max(1, Math.Min(right - left + 1, bottom - top + 1));

            BuildDistance();
            for (int a = 0; a < activeCount; a++)
            {
                int i = active[a], x = i % Width, y = i / Width;
                // Sobel averages suppress stair steps in diagonals and ears.
                double gx = At(x + 1, y - 1) + 2 * At(x + 1, y) + At(x + 1, y + 1)
                          - At(x - 1, y - 1) - 2 * At(x - 1, y) - At(x - 1, y + 1);
                double gy = At(x - 1, y + 1) + 2 * At(x, y + 1) + At(x + 1, y + 1)
                          - At(x - 1, y - 1) - 2 * At(x, y - 1) - At(x + 1, y - 1);
                // Preserve a small gradient at medial ridges. Normalizing tiny
                // vectors to unit length would magnify pixel noise in the ears.
                double scale = 1 / Math.Max(8, Math.Sqrt(gx * gx + gy * gy));
                gradientX[i] = (float)(gx * scale);
                gradientY[i] = (float)(gy * scale);
            }
            hasMask = true;
            BuildOptics(builtThickness < 0 ? DefaultThickness : builtThickness);
        }

        private float At(int x, int y)
        { return x < 0 || y < 0 || x >= Width || y >= Height ? 0 : distance[y * Width + x]; }

        private void BuildDistance()
        {
            // Exact squared Euclidean distance by two separable lower-envelope
            // sweeps. A transparent padding cell also handles solid masks that
            // touch every texture edge. This work is never part of Render.
            int width = Width + 2, height = Height + 2, longest = Math.Max(width, height);
            var intermediate = new double[width * height];
            var line = new double[longest]; var result = new double[longest];
            var sites = new int[longest]; var boundary = new double[longest + 1];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int alpha = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? 0 : mask[(y - 1) * Width + x - 1];
                    line[x] = alpha == 255 ? 1e20 : alpha * alpha / 65025.0;
                }
                DistanceLine(line, result, width, sites, boundary);
                for (int x = 0; x < width; x++) intermediate[y * width + x] = result[x];
            }
            for (int x = 1; x < width - 1; x++)
            {
                for (int y = 0; y < height; y++) line[y] = intermediate[y * width + x];
                DistanceLine(line, result, height, sites, boundary);
                for (int y = 1; y < height - 1; y++) distance[(y - 1) * Width + x - 1] = (float)Math.Sqrt(result[y]);
            }
            // A mask is sampled on a pixel grid, whereas its optical surface
            // should be smooth. A small separable binomial filter removes the
            // nearest-boundary ridges before they can amplify into ray stripes.
            // The original alpha remains untouched and owns the exact contour.
            var smooth = new float[distance.Length];
            int[] weights = { 1, 6, 15, 20, 15, 6, 1 };
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                double sum = 0;
                for (int k = -3; k <= 3; k++) if (x + k >= 0 && x + k < Width) sum += distance[y * Width + x + k] * weights[k + 3];
                smooth[y * Width + x] = (float)(sum / 64);
            }
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                double sum = 0;
                for (int k = -3; k <= 3; k++) if (y + k >= 0 && y + k < Height) sum += smooth[(y + k) * Width + x] * weights[k + 3];
                distance[y * Width + x] = (float)(sum / 64);
            }
        }

        private static void DistanceLine(double[] input, double[] output, int length, int[] sites, double[] boundary)
        {
            int count = 0; sites[0] = 0; boundary[0] = Double.NegativeInfinity; boundary[1] = Double.PositiveInfinity;
            for (int q = 1; q < length; q++)
            {
                double crossing;
                do
                {
                    int p = sites[count];
                    crossing = ((input[q] + (double)q * q) - (input[p] + (double)p * p)) / (2.0 * (q - p));
                    if (crossing > boundary[count]) break;
                    count--;
                } while (count >= 0);
                count++; sites[count] = q; boundary[count] = crossing; boundary[count + 1] = Double.PositiveInfinity;
            }
            int segment = 0;
            for (int q = 0; q < length; q++)
            {
                while (boundary[segment + 1] < q) segment++;
                double delta = q - sites[segment];
                output[q] = delta * delta + input[sites[segment]];
            }
        }

        private void BuildOptics(double thickness)
        {
            builtThickness = thickness; MapVersion++; MaximumDisplacement = 0;
            double bevel = Math.Max(1.5, shapeScale * .115 * (.88 + .12 * thickness));
            double depth = shapeScale * .17 * thickness;
            const double index = 1.52, eta = 1 / index;
            for (int a = 0; a < activeCount; a++)
            {
                int i = active[a], x = i % Width, y = i / Width;
                double d = Math.Max(.015, distance[i] - .5);
                double q = Math.Min(1, d / bevel);
                double gx = gradientX[i], gy = gradientY[i];
                // A rounded shoulder rises above a finite-thickness vertical
                // sidewall. Its derivative supplies the actual surface normal.
                double angle = q * Math.PI * .5;
                double height = depth * (.42 + .58 * Math.Sin(angle));
                double slope = .58 * depth / bevel * Math.PI * .5 * Math.Cos(angle);
                double nz = 1 / Math.Sqrt(1 + slope * slope * (gx * gx + gy * gy));
                double nx = -gx * slope * nz, ny = -gy * slope * nz;
                double transmittedCos = Math.Sqrt(1 - eta * eta * (1 - nz * nz));
                double bend = eta * nz - transmittedCos;
                double tx = bend * nx, ty = bend * ny, tz = -eta + bend * nz;
                // The rear plane sits below the front shoulder. A small space
                // behind it makes the bevel bend the real desktop visibly.
                double through = (height + depth * .45) / Math.Max(.2, -tz);
                double exitX = tx * index, exitY = ty * index;
                double exitZ = Math.Sqrt(Math.Max(.12, 1 - exitX * exitX - exitY * exitY));
                double mainX = tx * through + exitX * depth * .24 / exitZ;
                double mainY = ty * through + exitY * depth * .24 / exitZ;
                // Gentle interior magnification joins the strong shoulder.
                double interior = .105 * thickness / DefaultThickness;
                double magnifyX = (centerX - (x + .5)) * interior;
                double magnifyY = (centerY - (y + .5)) * interior;
                offsetX[i] = (float)(mainX + magnifyX);
                offsetY[i] = (float)(mainY + magnifyY);
                // A second, weaker image appears only in the bevel. Keeping its
                // ray separate produces thickness, rather than a drawn outline.
                rearX[i] = (float)(magnifyX + gx * depth * (.13 - .20 * q));
                rearY[i] = (float)(magnifyY + gy * depth * (.13 - .20 * q));
                double rearBand = (q - .24) / .23;
                rearWeight[i] = (float)(.27 * Math.Exp(-rearBand * rearBand));
                if (q >= .82) rearWeight[i] = 0;

                double facing = -.62 * (-gx) - .7846 * (-gy);
                double lit = Math.Max(0, facing), shaded = Math.Max(0, -facing);
                double outerBand = Math.Exp(-d / (bevel * .43));
                double innerPosition = (q - .70) / .12;
                double innerBand = Math.Exp(-innerPosition * innerPosition);
                double f = 1 - nz; double fresnel = f * f * f * f * f;
                reflection[i] = (float)(outerBand * (.075 + .52 * lit) + innerBand * (.025 + .16 * lit) + .32 * fresnel);
                extinction[i] = (float)(outerBand * (.085 + .38 * shaded) + innerBand * (.025 + .09 * shaded));
                double displacement = Math.Sqrt(offsetX[i] * offsetX[i] + offsetY[i] * offsetY[i]);
                if (displacement > MaximumDisplacement) MaximumDisplacement = displacement;
            }
        }

        internal void Render(byte[] source, int sourceWidth, int sourceHeight, int stride,
            double sourceX, double sourceY, double stepX, double stepY,
            double refraction = 1, double highlight = 1, double clarity = 1, double thickness = DefaultThickness)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (sourceWidth < 1 || sourceHeight < 1 || stride < (long)sourceWidth * 4 ||
                (long)(sourceHeight - 1) * stride + (long)sourceWidth * 4 > source.Length)
                throw new ArgumentException("The opaque BGRA source does not contain the declared rows.", "source");
            Finite(sourceX, "sourceX"); Finite(sourceY, "sourceY"); Finite(stepX, "stepX"); Finite(stepY, "stepY");
            Finite(refraction, "refraction"); Finite(highlight, "highlight"); Finite(clarity, "clarity"); Finite(thickness, "thickness");
            if (stepX <= 0 || stepY <= 0) throw new ArgumentOutOfRangeException("stepX");
            refraction = Clamp(refraction, 0, 2.5); highlight = Clamp(highlight, 0, 2);
            clarity = Clamp(clarity, 0, 1); thickness = Clamp(thickness, .65, 1.8);
            if (thickness != builtThickness) BuildOptics(thickness);
            float veil = (float)((1 - clarity) * .08), light = (float)highlight;
            long luminance = 0, alphaTotal = 0;
            for (int a = 0; a < activeCount; a++)
            {
                int i = active[a], x = i % Width, y = i / Width;
                double px = x + .5, py = y + .5;
                int blue, green, red;
                Sample(source, sourceWidth, sourceHeight, stride,
                    sourceX + (px + offsetX[i] * refraction) * stepX - .5,
                    sourceY + (py + offsetY[i] * refraction) * stepY - .5,
                    out blue, out green, out red);
                float mix = rearWeight[i];
                if (mix > .006f && refraction > 0)
                {
                    int rearBlue, rearGreen, rearRed;
                    Sample(source, sourceWidth, sourceHeight, stride,
                        sourceX + (px + rearX[i] * refraction) * stepX - .5,
                        sourceY + (py + rearY[i] * refraction) * stepY - .5,
                        out rearBlue, out rearGreen, out rearRed);
                    blue += (int)((rearBlue - blue) * mix);
                    green += (int)((rearGreen - green) * mix);
                    red += (int)((rearRed - red) * mix);
                }
                float white = Math.Min(.86f, reflection[i] * light + veil);
                float gain = (1 - white) * (1 - Math.Min(.68f, extinction[i] * light));
                float add = white * 255;
                blue = (int)(blue * gain + add + .5f);
                green = (int)(green * gain + add + .5f);
                red = (int)(red * gain + add + .5f);
                int alpha = mask[i], pixel = i * 4;
                Pixels[pixel] = (byte)((blue * alpha + 127) / 255);
                Pixels[pixel + 1] = (byte)((green * alpha + 127) / 255);
                Pixels[pixel + 2] = (byte)((red * alpha + 127) / 255);
                Pixels[pixel + 3] = (byte)alpha;
                luminance += ((54 * red + 183 * green + 19 * blue) >> 8) * alpha;
                alphaTotal += alpha;
            }
            MeanBrightness = alphaTotal == 0 ? 0 : luminance / (255.0 * alphaTotal);
        }

        internal void RenderFallback(double highlight = 1, double clarity = 1, double thickness = DefaultThickness)
        {
            Finite(highlight, "highlight"); Finite(clarity, "clarity"); Finite(thickness, "thickness");
            highlight = Clamp(highlight, 0, 2); clarity = Clamp(clarity, 0, 1); thickness = Clamp(thickness, .65, 1.8);
            if (thickness != builtThickness) BuildOptics(thickness);
            if (fallbackMapVersion == MapVersion && fallbackHighlight == highlight && fallbackClarity == clarity) return;
            fallbackMapVersion = MapVersion; fallbackHighlight = highlight; fallbackClarity = clarity; FallbackVersion++;
            double bodyAlpha = .012 + (1 - clarity) * .045;
            for (int a = 0; a < activeCount; a++)
            {
                int i = active[a], pixel = i * 4;
                double white = Math.Min(.86, reflection[i] * highlight);
                double dark = Math.Min(.68, extinction[i] * highlight);
                double through = (1 - white) * (1 - dark);
                // Premultiplied composition of white surface reflection, black
                // extinction, then a nearly transparent neutral body. Real
                // desktop pixels remain visible through this emergency layer.
                double alpha = 1 - through * (1 - bodyAlpha);
                double channel = white * 255 + through * bodyAlpha * 144;
                int coverage = mask[i];
                FallbackPixels[pixel] = FallbackPixels[pixel + 1] = FallbackPixels[pixel + 2] = (byte)(channel * coverage / 255 + .5);
                FallbackPixels[pixel + 3] = (byte)(alpha * coverage + .5);
            }
        }

        private static void Sample(byte[] source, int width, int height, int stride,
            double x, double y, out int blue, out int green, out int red)
        {
            x = Math.Max(0, Math.Min(width - 1, x)); y = Math.Max(0, Math.Min(height - 1, y));
            int x0 = (int)x, y0 = (int)y;
            int x1 = x0 + 1 < width ? x0 + 1 : x0, y1 = y0 + 1 < height ? y0 + 1 : y0;
            int fx = (int)((x - x0) * 256), fy = (int)((y - y0) * 256);
            int a = (256 - fx) * (256 - fy), b = fx * (256 - fy), c = (256 - fx) * fy, d = fx * fy;
            int p00 = y0 * stride + x0 * 4, p10 = y0 * stride + x1 * 4;
            int p01 = y1 * stride + x0 * 4, p11 = y1 * stride + x1 * 4;
            blue = (source[p00] * a + source[p10] * b + source[p01] * c + source[p11] * d + 32768) >> 16;
            green = (source[p00 + 1] * a + source[p10 + 1] * b + source[p01 + 1] * c + source[p11 + 1] * d + 32768) >> 16;
            red = (source[p00 + 2] * a + source[p10 + 2] * b + source[p01 + 2] * c + source[p11 + 2] * d + 32768) >> 16;
        }
        private static double Clamp(double value, double minimum, double maximum)
        { return Math.Max(minimum, Math.Min(maximum, value)); }
        private static void Finite(double value, string name)
        { if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentException("Material parameters must be finite.", name); }
    }
}
