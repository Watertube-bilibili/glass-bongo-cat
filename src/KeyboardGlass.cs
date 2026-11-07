using System;

namespace GlassBongoCat
{
    /// <summary>
    /// One screen-space material for a complete keyboard. The housing and all
    /// disconnected keycaps share one composite ray map and one bilinear sample
    /// per visible pixel. Legends are intentionally drawn by the caller above it.
    /// Input is opaque BGRA; both outputs are premultiplied BGRA.
    /// </summary>
    internal sealed class KeyboardGlass
    {
        internal readonly int Width, Height;
        internal readonly byte[] Pixels, FallbackPixels;
        internal double MeanBrightness { get; private set; }
        internal double MaximumDisplacement { get; private set; }
        internal int MapVersion { get; private set; }
        internal int FallbackVersion { get; private set; }
        private readonly byte[] housing, keys, alpha;
        private readonly float[] caseDistance, keyDistance, caseX, caseY, keyX, keyY;
        // Compact active-pixel arrays avoid row divisions and transparent work
        // in the hot loop. All storage is allocated once by the constructor.
        private readonly int[] outputIndex, gain, add, sampleX, sampleY;
        private readonly float[] positionX, positionY, offsetX, offsetY, reflection, extinction;
        private int activeCount;
        private long totalAlpha;
        private bool hasMasks;
        private double caseScale, keyRadius, centerX, centerY, builtThickness = -1;
        private int shadeMap = -1, fallbackMap = -1;
        private double shadeHighlight, shadeClarity, fallbackHighlight, fallbackClarity;
        private int samplingMap = -1;
        private double samplingRefraction, samplingStepX, samplingStepY;

        internal KeyboardGlass(int width, int height, byte[] housingAlpha, byte[] keysAlpha)
        {
            if (width < 1 || height < 1 || (long)width * height > 1048576)
                throw new ArgumentOutOfRangeException("width", "Keyboard material exceeds the one-million-pixel budget.");
            Width = width; Height = height; int count = width * height;
            Pixels = new byte[count * 4]; FallbackPixels = new byte[count * 4];
            housing = new byte[count]; keys = new byte[count]; alpha = new byte[count];
            caseDistance = new float[count]; keyDistance = new float[count];
            caseX = new float[count]; caseY = new float[count]; keyX = new float[count]; keyY = new float[count];
            outputIndex = new int[count]; gain = new int[count]; add = new int[count]; sampleX = new int[count]; sampleY = new int[count];
            positionX = new float[count]; positionY = new float[count]; offsetX = new float[count]; offsetY = new float[count];
            reflection = new float[count]; extinction = new float[count];
            SetMasks(housingAlpha, keysAlpha);
        }

        internal void SetMasks(byte[] housingAlpha, byte[] keysAlpha)
        {
            if (housingAlpha == null || keysAlpha == null) throw new ArgumentNullException("housingAlpha");
            if (housingAlpha.Length != alpha.Length || keysAlpha.Length != alpha.Length)
                throw new ArgumentException("Both masks must contain exactly width * height bytes.");
            bool same = hasMasks;
            if (same) for (int i = 0; i < alpha.Length; i++)
                if (housing[i] != housingAlpha[i] || keys[i] != keysAlpha[i]) { same = false; break; }
            if (same) return;
            Buffer.BlockCopy(housingAlpha, 0, housing, 0, alpha.Length);
            Buffer.BlockCopy(keysAlpha, 0, keys, 0, alpha.Length);
            Array.Clear(Pixels, 0, Pixels.Length); Array.Clear(FallbackPixels, 0, FallbackPixels.Length);
            activeCount = 0; totalAlpha = 0; MeanBrightness = 0;
            int left = Width, right = -1, top = Height, bottom = -1;
            double weightedX = 0, weightedY = 0;
            for (int y = 0, i = 0; y < Height; y++) for (int x = 0; x < Width; x++, i++)
            {
                int value = Math.Max(housing[i], keys[i]); alpha[i] = (byte)value;
                if (value == 0) continue;
                outputIndex[activeCount] = i * 4;
                positionX[activeCount] = x + .5f; positionY[activeCount] = y + .5f; activeCount++;
                totalAlpha += value; weightedX += (x + .5) * value; weightedY += (y + .5) * value;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            centerX = totalAlpha == 0 ? Width * .5 : weightedX / totalAlpha;
            centerY = totalAlpha == 0 ? Height * .5 : weightedY / totalAlpha;
            caseScale = totalAlpha == 0 ? 1 : Math.Max(1, Math.Min(right - left + 1, bottom - top + 1));
            DistanceField(housing, caseDistance, caseX, caseY);
            DistanceField(keys, keyDistance, keyX, keyY);
            keyRadius = 0;
            for (int i = 0; i < keys.Length; i++) if (keys[i] > 0) keyRadius = Math.Max(keyRadius, keyDistance[i]);
            keyRadius = Math.Max(.75, Math.Min(caseScale * .14, keyRadius));
            hasMasks = true; BuildMap(builtThickness < 0 ? 1.25 : builtThickness);
        }

        private void BuildMap(double thickness)
        {
            builtThickness = thickness; MapVersion++; MaximumDisplacement = 0;
            double caseBevel = Math.Max(1.2, caseScale * .068 * (.9 + .1 * thickness));
            double caseDepth = caseScale * .085 * thickness;
            double capBevel = Math.Max(.65, keyRadius * .65 * (.9 + .1 * thickness));
            double capDepth = Math.Max(.9, keyRadius * 1.65) * thickness;
            for (int a = 0; a < activeCount; a++)
            {
                int i = outputIndex[a] / 4;
                double dx, dy, white, dark;
                Surface(caseDistance[i], caseX[i], caseY[i], caseBevel, caseDepth, out dx, out dy, out white, out dark);
                double housingWeight = housing[i] / 255.0;
                dx *= housingWeight; dy *= housingWeight; white *= housingWeight; dark *= housingWeight;
                // Slight body magnification gives the clear gaps optical depth.
                dx += (centerX - positionX[a]) * .016 * thickness * housingWeight;
                dy += (centerY - positionY[a]) * .016 * thickness * housingWeight;
                if (keys[i] > 0)
                {
                    double capDx, capDy, capWhite, capDark;
                    Surface(keyDistance[i], keyX[i], keyY[i], capBevel, capDepth, out capDx, out capDy, out capWhite, out capDark);
                    double weight = keys[i] / 255.0;
                    dx += capDx * weight; dy += capDy * weight;
                    // Combined transmission of the two optical surfaces. This
                    // adds facet reflections without an opaque keycap fill.
                    white = 1 - (1 - white) * (1 - capWhite * .78 * weight);
                    dark = 1 - (1 - dark) * (1 - capDark * .78 * weight);
                }
                offsetX[a] = (float)dx; offsetY[a] = (float)dy;
                reflection[a] = (float)white; extinction[a] = (float)dark;
                MaximumDisplacement = Math.Max(MaximumDisplacement, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        private static void Surface(double distance, double gx, double gy, double bevel, double depth,
            out double dx, out double dy, out double white, out double dark)
        {
            double d = Math.Max(0, distance - .4), q = Math.Min(1, d / bevel);
            double slope = depth / bevel * .85 * Math.Cos(q * Math.PI * .5);
            double nz = 1 / Math.Sqrt(1 + slope * slope * (gx * gx + gy * gy));
            double nx = -gx * slope * nz, ny = -gy * slope * nz;
            const double eta = 1 / 1.5;
            double bend = eta * nz - Math.Sqrt(1 - eta * eta * (1 - nz * nz));
            double tx = bend * nx, ty = bend * ny, tz = -eta + bend * nz;
            double travel = depth * (.8 + .55 * Math.Sin(q * Math.PI * .5)) / Math.Max(.25, -tz);
            dx = tx * travel; dy = ty * travel;
            double facing = .6 * gx + .8 * gy;
            double outer = Math.Exp(-d / Math.Max(.45, bevel * .43));
            double innerPosition = (q - .74) / .14;
            double inner = q < 1 ? Math.Exp(-innerPosition * innerPosition) : 0;
            white = outer * (.025 + .37 * Math.Max(0, facing)) + inner * (.01 + .075 * Math.Max(0, facing));
            dark = outer * (.025 + .25 * Math.Max(0, -facing)) + inner * (.012 + .055 * Math.Max(0, -facing));
        }

        private void BuildShade(double highlight, double clarity)
        {
            if (shadeMap == MapVersion && shadeHighlight == highlight && shadeClarity == clarity) return;
            shadeMap = MapVersion; shadeHighlight = highlight; shadeClarity = clarity;
            double veil = (1 - clarity) * .04;
            for (int a = 0; a < activeCount; a++)
            {
                int coverage = alpha[outputIndex[a] / 4];
                double white = Math.Min(.80, reflection[a] * highlight + veil);
                double dark = Math.Min(.60, extinction[a] * highlight);
                // Premultiplication and neutral lighting are folded into two
                // fixed-point constants. No float shading in the sampling loop.
                gain[a] = (int)((1 - white) * (1 - dark) * coverage / 255 * 65536 + .5);
                add[a] = (int)(white * coverage * 65536 + .5);
                Pixels[outputIndex[a] + 3] = (byte)coverage;
            }
        }

        internal void Render(byte[] source, int sourceWidth, int sourceHeight, int stride,
            double sourceX, double sourceY, double stepX, double stepY,
            double refraction = 1, double highlight = 1, double clarity = 1, double thickness = 1.25)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (sourceWidth < 1 || sourceHeight < 1 || stride < (long)sourceWidth * 4 ||
                (long)(sourceHeight - 1) * stride + (long)sourceWidth * 4 > source.Length)
                throw new ArgumentException("The BGRA source does not contain the declared rows.", "source");
            Finite(sourceX); Finite(sourceY); Finite(stepX); Finite(stepY); Finite(refraction); Finite(highlight); Finite(clarity); Finite(thickness);
            if (stepX <= 0 || stepY <= 0) throw new ArgumentOutOfRangeException("stepX");
            refraction = Clamp(refraction, 0, 2.5); highlight = Clamp(highlight, 0, 2); clarity = Clamp(clarity, 0, 1); thickness = Clamp(thickness, .65, 1.8);
            if (builtThickness != thickness) BuildMap(thickness);
            BuildShade(highlight, clarity);
            long luminance = 0;
            double originX = sourceX - .5, originY = sourceY - .5;
            int maxX = sourceWidth - 1, maxY = sourceHeight - 1;
            bool fixedSampling = sourceWidth <= 1048576 && sourceHeight <= 1048576 &&
                Math.Abs(originX) <= 1000000 && Math.Abs(originY) <= 1000000 && PrepareSampling(refraction, stepX, stepY);
            int originQX = fixedSampling ? (int)Math.Floor(originX * 256 + .5) : 0;
            int originQY = fixedSampling ? (int)Math.Floor(originY * 256 + .5) : 0;
            int maxQX = fixedSampling ? maxX * 256 : 0, maxQY = fixedSampling ? maxY * 256 : 0;
            for (int a = 0; a < activeCount; a++)
            {
                int x, y, fx, fy;
                if (fixedSampling)
                {
                    int sx = sampleX[a] + originQX, sy = sampleY[a] + originQY;
                    if (sx < 0) sx = 0; else if (sx > maxQX) sx = maxQX;
                    if (sy < 0) sy = 0; else if (sy > maxQY) sy = maxQY;
                    x = sx >> 8; y = sy >> 8; fx = sx & 255; fy = sy & 255;
                }
                else
                {
                    // Unusual but valid very large coordinates/scales use the
                    // bounded double path, with no fixed-point integer overflow.
                    double sx = originX + (positionX[a] + offsetX[a] * refraction) * stepX;
                    double sy = originY + (positionY[a] + offsetY[a] * refraction) * stepY;
                    if (sx < 0) sx = 0; else if (sx > maxX) sx = maxX;
                    if (sy < 0) sy = 0; else if (sy > maxY) sy = maxY;
                    x = (int)sx; y = (int)sy; fx = (int)((sx - x) * 256); fy = (int)((sy - y) * 256);
                }
                int p = y * stride + x * 4;
                int nextX = x < maxX ? 4 : 0, nextY = y < maxY ? stride : 0;
                int p10 = p + nextX, p01 = p + nextY, p11 = p01 + nextX;
                // Three integer multiplies per channel, with a single final
                // rounding; this is the same bilinear filter as four weights.
                int top = (source[p] << 8) + (source[p10] - source[p]) * fx;
                int bottom = (source[p01] << 8) + (source[p11] - source[p01]) * fx;
                int blue = (top + (((bottom - top) * fy) >> 8) + 128) >> 8;
                top = (source[p + 1] << 8) + (source[p10 + 1] - source[p + 1]) * fx;
                bottom = (source[p01 + 1] << 8) + (source[p11 + 1] - source[p01 + 1]) * fx;
                int green = (top + (((bottom - top) * fy) >> 8) + 128) >> 8;
                top = (source[p + 2] << 8) + (source[p10 + 2] - source[p + 2]) * fx;
                bottom = (source[p01 + 2] << 8) + (source[p11 + 2] - source[p01 + 2]) * fx;
                int red = (top + (((bottom - top) * fy) >> 8) + 128) >> 8;
                int g = gain[a], b = add[a] + 32768, output = outputIndex[a];
                blue = (blue * g + b) >> 16; green = (green * g + b) >> 16; red = (red * g + b) >> 16;
                Pixels[output] = (byte)blue; Pixels[output + 1] = (byte)green; Pixels[output + 2] = (byte)red;
                luminance += 54 * red + 183 * green + 19 * blue;
            }
            MeanBrightness = totalAlpha == 0 ? 0 : luminance / (256.0 * totalAlpha);
        }

        private bool PrepareSampling(double refraction, double stepX, double stepY)
        {
            if ((Width + MaximumDisplacement * 2.5 + 1) * stepX > 1000000 ||
                (Height + MaximumDisplacement * 2.5 + 1) * stepY > 1000000) return false;
            if (samplingMap == MapVersion && samplingRefraction == refraction && samplingStepX == stepX && samplingStepY == stepY) return true;
            samplingMap = MapVersion; samplingRefraction = refraction; samplingStepX = stepX; samplingStepY = stepY;
            // Scale/strength changes update this inexpensive sampling-coordinate
            // table. Moving the keyboard only adds a 1/256-pixel integer origin;
            // it never recalculates geometry, normals or per-pixel doubles.
            for (int a = 0; a < activeCount; a++)
            {
                sampleX[a] = (int)Math.Floor((positionX[a] + offsetX[a] * refraction) * stepX * 256);
                sampleY[a] = (int)Math.Floor((positionY[a] + offsetY[a] * refraction) * stepY * 256);
            }
            return true;
        }

        internal void RenderFallback(double highlight = 1, double clarity = 1, double thickness = 1.25)
        {
            Finite(highlight); Finite(clarity); Finite(thickness);
            highlight = Clamp(highlight, 0, 2); clarity = Clamp(clarity, 0, 1); thickness = Clamp(thickness, .65, 1.8);
            if (builtThickness != thickness) BuildMap(thickness);
            if (fallbackMap == MapVersion && fallbackHighlight == highlight && fallbackClarity == clarity) return;
            fallbackMap = MapVersion; fallbackHighlight = highlight; fallbackClarity = clarity; FallbackVersion++;
            double body = .008 + (1 - clarity) * .025;
            for (int a = 0; a < activeCount; a++)
            {
                int output = outputIndex[a], coverage = alpha[output / 4];
                double white = Math.Min(.8, reflection[a] * highlight), dark = Math.Min(.6, extinction[a] * highlight);
                double through = (1 - white) * (1 - dark);
                int channel = (int)((white * 255 + through * body * 144) * coverage / 255 + .5);
                FallbackPixels[output] = FallbackPixels[output + 1] = FallbackPixels[output + 2] = (byte)channel;
                FallbackPixels[output + 3] = (byte)((1 - through * (1 - body)) * coverage + .5);
            }
        }

        private void DistanceField(byte[] mask, float[] field, float[] gx, float[] gy)
        {
            int w = Width + 2, h = Height + 2, length = Math.Max(w, h);
            var image = new double[w * h]; var line = new double[length]; var result = new double[length];
            var sites = new int[length]; var boundary = new double[length + 1];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int value = x == 0 || y == 0 || x == w - 1 || y == h - 1 ? 0 : mask[(y - 1) * Width + x - 1];
                    line[x] = value == 255 ? 1e20 : value * value / 65025.0;
                }
                DistanceLine(line, result, w, sites, boundary);
                for (int x = 0; x < w; x++) image[y * w + x] = result[x];
            }
            for (int x = 1; x < w - 1; x++)
            {
                for (int y = 0; y < h; y++) line[y] = image[y * w + x];
                DistanceLine(line, result, h, sites, boundary);
                for (int y = 1; y < h - 1; y++) field[(y - 1) * Width + x - 1] = (float)Math.Sqrt(result[y]);
            }
            // A small filter preserves the short keycap bevel while smoothing
            // subpixel raster corners. Exact output alpha remains unchanged.
            var smooth = new float[field.Length];
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                smooth[i] = (At(field, x - 1, y) + 2 * field[i] + At(field, x + 1, y)) * .25f;
            }
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                field[i] = (At(smooth, x, y - 1) + 2 * smooth[i] + At(smooth, x, y + 1)) * .25f;
            }
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                gx[i] = (At(field, x + 1, y - 1) + 2 * At(field, x + 1, y) + At(field, x + 1, y + 1)
                    - At(field, x - 1, y - 1) - 2 * At(field, x - 1, y) - At(field, x - 1, y + 1)) * .125f;
                gy[i] = (At(field, x - 1, y + 1) + 2 * At(field, x, y + 1) + At(field, x + 1, y + 1)
                    - At(field, x - 1, y - 1) - 2 * At(field, x, y - 1) - At(field, x + 1, y - 1)) * .125f;
            }
        }
        private float At(float[] field, int x, int y)
        { return x < 0 || y < 0 || x >= Width || y >= Height ? 0 : field[y * Width + x]; }
        private static void DistanceLine(double[] input, double[] output, int length, int[] sites, double[] boundary)
        {
            int count = 0; sites[0] = 0; boundary[0] = Double.NegativeInfinity; boundary[1] = Double.PositiveInfinity;
            for (int q = 1; q < length; q++)
            {
                double crossing;
                while (true)
                {
                    int p = sites[count];
                    crossing = ((input[q] + (double)q * q) - (input[p] + (double)p * p)) / (2.0 * (q - p));
                    if (crossing > boundary[count]) break;
                    count--;
                }
                count++; sites[count] = q; boundary[count] = crossing; boundary[count + 1] = Double.PositiveInfinity;
            }
            int segment = 0;
            for (int q = 0; q < length; q++)
            { while (boundary[segment + 1] < q) segment++; double delta = q - sites[segment]; output[q] = delta * delta + input[sites[segment]]; }
        }
        private static double Clamp(double value, double low, double high) { return Math.Max(low, Math.Min(high, value)); }
        private static void Finite(double value)
        { if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentException("Keyboard optical parameters must be finite."); }
    }
}
