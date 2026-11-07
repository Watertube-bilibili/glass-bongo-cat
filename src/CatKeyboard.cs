using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GlassBongoCat
{
    // Original vector keyboard, independent of the licensed cat artwork. All
    // housing, keycap and text geometry is constructed once and frozen.
    internal sealed class CatKeyboard
    {
        private sealed class Key
        {
            internal int Code, Alternate;
            internal Point Center;
            internal DrawingGroup Active;
        }

        private readonly List<Key> keys = new List<Key>();
        private readonly DrawingGroup idle;
        private readonly MatrixTransform plane;
        private static readonly Typeface Face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Brush Cap = Brush(255, 253, 254, 255);
        private static readonly Brush ModifierCap = Brush(255, 233, 238, 244);
        private static readonly Brush Ink = Brush(255, 34, 44, 58);
        private static readonly Brush KeySide = Brush(255, 159, 171, 187);
        private static readonly Brush ActiveCap = Brush(255, 177, 214, 255);
        private static readonly Brush ActiveInk = Brush(255, 18, 69, 132);
        private static readonly Pen CapBorder = Pen(Brush(255, 192, 202, 215), .65);
        private static readonly Pen ActiveBorder = Pen(Brush(255, 67, 139, 227), 1);
        internal int KeyCount { get { return keys.Count; } }

        internal CatKeyboard()
        {
            // Same diagonal as the original desktop, with a shallow foreshortening.
            // Full footprint, including the raised front lip: x64..519, y195..348.
            plane = new MatrixTransform(new Matrix(1, 108.0 / 611.0, -.25, .62, 92, 198));
            plane.Freeze();
            idle = new DrawingGroup();
            using (DrawingContext d = idle.Open())
            {
                d.PushTransform(plane);
                DrawHousing(d);
                const double pitch = 18.3;
                Row(d, 8, new string[] { "esc", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "−", "=", "⌫" },
                    new int[] {27,49,50,51,52,53,54,55,56,57,48,189,187,8},
                    new double[] {1,1,1,1,1,1,1,1,1,1,1,1,1,2});
                Row(d, 8 + pitch, new string[] { "tab", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\" },
                    new int[] {9,81,87,69,82,84,89,85,73,79,80,219,221,220},
                    new double[] {1.5,1,1,1,1,1,1,1,1,1,1,1,1,1.5});
                Row(d, 8 + pitch * 2, new string[] { "caps", "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'", "enter" },
                    new int[] {20,65,83,68,70,71,72,74,75,76,186,222,13},
                    new double[] {1.75,1,1,1,1,1,1,1,1,1,1,1,2.25});
                Row(d, 8 + pitch * 3, new string[] { "shift", "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "shift" },
                    new int[] {160,90,88,67,86,66,78,77,188,190,191,161},
                    new double[] {2.25,1,1,1,1,1,1,1,1,1,1,2.75});
                Row(d, 8 + pitch * 4, new string[] { "ctrl", "win", "alt", "", "alt", "fn", "menu", "ctrl" },
                    new int[] {162,91,164,32,165,0,93,163},
                    new double[] {1.25,1.25,1.25,6.25,1.25,1.25,1.25,1.25});
                d.Pop();
            }
            idle.Freeze();
        }

        internal void Draw(DrawingContext drawing, NativeInput input, bool enabled)
        {
            drawing.DrawDrawing(idle);
            if (!enabled || input == null) return;
            for (int i = 0; i < keys.Count; i++)
            {
                Key key = keys[i];
                double heat = key.Code == 0 ? 0 : input.Heat(key.Code);
                if (key.Alternate != 0) heat = Math.Max(heat, input.Heat(key.Alternate));
                DrawHeat(drawing, key, heat);
            }
        }

        // Deterministic offscreen previews/tests; this never reads or injects input.
        internal void DrawPreview(DrawingContext drawing, Func<int, double> heat, bool enabled)
        {
            drawing.DrawDrawing(idle);
            if (!enabled || heat == null) return;
            for (int i = 0; i < keys.Count; i++)
            {
                Key key = keys[i];
                double value = key.Code == 0 ? 0 : heat(key.Code);
                if (key.Alternate != 0) value = Math.Max(value, heat(key.Alternate));
                DrawHeat(drawing, key, value);
            }
        }

        internal bool TryGetKeyCenter(int virtualKey, out Point center)
        {
            if (virtualKey == 16) virtualKey = 160;
            if (virtualKey == 17) virtualKey = 162;
            if (virtualKey == 18) virtualKey = 164;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].Code == virtualKey || (virtualKey != 0 && keys[i].Alternate == virtualKey))
                { center = keys[i].Center; return true; }
            center = new Point(); return false;
        }

        private static void DrawHeat(DrawingContext drawing, Key key, double heat)
        {
            if (Double.IsNaN(heat) || heat <= .005) return;
            drawing.PushOpacity(Math.Min(1, heat));
            drawing.DrawDrawing(key.Active);
            drawing.Pop();
        }

        private void Row(DrawingContext d, double y, string[] labels, int[] codes, double[] widths)
        {
            const double unit = 27.2;
            double x = 8;
            for (int i = 0; i < labels.Length; i++)
            {
                double width = widths[i] * unit - 2.9;
                Rect cap = new Rect(x, y, width, 14.7);
                bool modifier = labels[i].Length > 1 && labels[i] != "⌫";
                Geometry text = Label(labels[i], cap, modifier ? 10.8 : 14.5);
                DrawCap(d, cap, modifier ? ModifierCap : Cap, Ink, CapBorder, text, false);
                DrawingGroup active = new DrawingGroup();
                using (DrawingContext a = active.Open())
                {
                    a.PushTransform(plane);
                    DrawCap(a, cap, ActiveCap, ActiveInk, ActiveBorder, text, true);
                    a.Pop();
                }
                active.Freeze();
                keys.Add(new Key { Code = codes[i], Alternate = codes[i] == 91 ? 92 : 0,
                    Center = plane.Transform(new Point(cap.X + cap.Width / 2, cap.Y + cap.Height / 2)), Active = active });
                x += widths[i] * unit;
            }
        }

        private static void DrawHousing(DrawingContext d)
        {
            d.DrawRoundedRectangle(Brush(40, 11, 24, 43), null, new Rect(-3, 5, 430, 109), 10, 10);
            d.DrawRoundedRectangle(Brush(30, 11, 24, 43), null, new Rect(-1, 3, 426, 108), 9, 9);
            d.DrawRoundedRectangle(Brush(255, 143, 157, 176), Pen(Brush(255, 119, 136, 157), .8), new Rect(0, 3, 424, 104), 8, 8);
            LinearGradientBrush metal = new LinearGradientBrush(Color.FromRgb(240, 244, 249), Color.FromRgb(208, 218, 231), 90);
            metal.Freeze();
            d.DrawRoundedRectangle(metal, Pen(Brush(255, 245, 248, 252), 1), new Rect(0, 0, 424, 104), 8, 8);
            d.DrawRoundedRectangle(Brush(255, 181, 194, 210), null, new Rect(5, 5, 414, 94), 5, 5);
            d.DrawRoundedRectangle(Brush(255, 66, 135, 219), null, new Rect(393, 100, 17, 1.8), .9, .9);
        }

        private static void DrawCap(DrawingContext d, Rect cap, Brush fill, Brush text,
            Pen border, Geometry label, bool active)
        {
            if (!active) d.DrawRoundedRectangle(KeySide, null, new Rect(cap.X, cap.Y + 1.7, cap.Width, cap.Height), 2.5, 2.5);
            else
            {
                // Paint an opaque well over the cached raised key first, so the
                // lowered legend never leaves a second copy of the idle glyph.
                d.DrawRoundedRectangle(KeySide, null,
                    new Rect(cap.X - .5, cap.Y - .5, cap.Width + 1, cap.Height + 2.7), 2.8, 2.8);
                d.PushTransform(new TranslateTransform(0, 1.2));
            }
            d.DrawRoundedRectangle(fill, border, cap, 2.5, 2.5);
            if (!active) d.DrawLine(Pen(Brush(230, 255, 255, 255), .8), new Point(cap.Left + 3, cap.Top + 1), new Point(cap.Right - 3, cap.Top + 1));
            if (label != null) d.DrawGeometry(text, null, label);
            else d.DrawRoundedRectangle(active ? ActiveInk : Brush(255, 173, 183, 198), null,
                new Rect(cap.X + cap.Width * .35, cap.Y + cap.Height * .66, cap.Width * .3, .9), .4, .4);
            if (active) d.Pop();
        }

        private static Geometry Label(string label, Rect cap, double size)
        {
            if (label.Length == 0) return null;
            FormattedText formatted = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Face, size, Ink, 1.0);
            Geometry glyph = formatted.BuildGeometry(new Point());
            Rect bounds = glyph.Bounds;
            glyph.Transform = new TranslateTransform(cap.X + (cap.Width - bounds.Width) / 2 - bounds.X,
                cap.Y + (cap.Height - bounds.Height) / 2 - bounds.Y - .15);
            glyph.Freeze(); return glyph;
        }

        private static Brush Brush(byte a, byte r, byte g, byte b)
        { SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(a, r, g, b)); brush.Freeze(); return brush; }
        private static Pen Pen(Brush brush, double thickness)
        { Pen pen = new Pen(brush, thickness); pen.Freeze(); return pen; }
    }
}
