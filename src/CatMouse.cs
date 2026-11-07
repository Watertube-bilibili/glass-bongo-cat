using System;
using System.Windows;
using System.Windows.Media;

namespace GlassBongoCat
{
    // Original vector accessory. The body is supplied by the shared keyboard
    // glass sampler; this class caches only its mask and transparent ink overlays.
    internal sealed class CatMouse
    {
        private readonly DrawingGroup[] idle = new DrawingGroup[2];
        private readonly DrawingGroup[] active = new DrawingGroup[2];
        private readonly Geometry leftButton, rightButton;
        internal Rect Bounds { get; private set; }
        internal Geometry HousingGeometry { get; private set; }
        internal Geometry ButtonsGeometry { get; private set; }

        internal CatMouse()
        {
            // Palm rest faces the cat; buttons/wheel point toward the viewer.
            // Rotate the complete device and button mapping together.
            TransformGroup pose = new TransformGroup();
            pose.Children.Add(new RotateTransform(190));
            pose.Children.Add(new TranslateTransform(99, 248));
            pose.Freeze();

            StreamGeometry body = new StreamGeometry();
            using (StreamGeometryContext s = body.Open())
            {
                s.BeginFigure(new Point(0, -31), true, true);
                s.BezierTo(new Point(-14, -31), new Point(-24, -20), new Point(-24, -5), true, false);
                s.BezierTo(new Point(-24, 16), new Point(-18, 31), new Point(0, 31), true, false);
                s.BezierTo(new Point(18, 31), new Point(24, 16), new Point(24, -5), true, false);
                s.BezierTo(new Point(24, -20), new Point(14, -31), new Point(0, -31), true, false);
            }
            body.Transform = pose; body.Freeze(); HousingGeometry = body;
            leftButton = Button(pose, false);
            rightButton = Button(pose, true);
            GeometryGroup buttons = new GeometryGroup();
            buttons.Children.Add(leftButton); buttons.Children.Add(rightButton);
            buttons.Freeze(); ButtonsGeometry = buttons;
            Rect bounds = HousingGeometry.Bounds; bounds.Inflate(2, 2); Bounds = bounds;

            for (int style = 0; style < 2; style++)
            {
                bool dark = style != 0;
                Brush line = B(dark ? (byte)235 : (byte)220, dark ? (byte)245 : (byte)39,
                    dark ? (byte)250 : (byte)55, dark ? (byte)255 : (byte)75);
                Brush halo = dark ? B(155, 9, 20, 35) : B(225, 252, 254, 255);
                DrawingGroup group = new DrawingGroup();
                using (DrawingContext d = group.Open())
                {
                    // Opposite-tone halo keeps the rim visible on both desktop
                    // themes while leaving the broad center truly transparent.
                    d.DrawGeometry(null, P(halo, 2.4), HousingGeometry);
                    d.DrawGeometry(null, P(line, .9), HousingGeometry);
                    d.PushTransform(pose);
                    Pen seam = P(line, .9);
                    d.DrawLine(P(halo, 2.3), new Point(0, -27), new Point(0, -18));
                    d.DrawLine(seam, new Point(0, -27), new Point(0, -18));
                    d.DrawLine(P(halo, 2.3), new Point(0, -5), new Point(0, 2));
                    d.DrawLine(seam, new Point(0, -5), new Point(0, 2));
                    StreamGeometry divide = new StreamGeometry();
                    using (StreamGeometryContext s = divide.Open())
                    {
                        s.BeginFigure(new Point(-20, 2), false, false);
                        s.BezierTo(new Point(-9, 5), new Point(9, 5), new Point(20, 2), true, false);
                    }
                    divide.Freeze();
                    d.DrawGeometry(null, P(halo, 2), divide);
                    d.DrawGeometry(null, P(line, .7), divide);
                    d.DrawRoundedRectangle(halo, null, new Rect(-3.4, -17.2, 6.8, 12.9), 3.4, 3.4);
                    d.DrawRoundedRectangle(line, null, new Rect(-2.1, -16, 4.2, 10.5), 2.1, 2.1);
                    d.DrawLine(P(dark ? B(230, 15, 36, 59) : B(245, 239, 249, 255), .8),
                        new Point(-.8, -13), new Point(-.8, -8.5));
                    d.Pop();
                }
                group.Freeze(); idle[style] = group;
            }

            for (int button = 0; button < 2; button++)
            {
                DrawingGroup group = new DrawingGroup();
                using (DrawingContext d = group.Open())
                    d.DrawGeometry(B(116, 74, 160, 255), P(B(225, 36, 125, 232), 1.1),
                        button == 0 ? leftButton : rightButton);
                group.Freeze(); active[button] = group;
            }
        }

        internal void DrawOverlay(DrawingContext drawing, NativeInput input, bool enabled, bool dark)
        {
            DrawState(drawing, enabled && input != null ? input.Heat(1) : 0,
                enabled && input != null ? input.Heat(2) : 0, dark);
        }
        internal void DrawFeedback(DrawingContext drawing,NativeInput input,bool enabled)
        {
            if(!enabled||input==null)return;
            PaintPress(drawing,active[0],input.Heat(1));PaintPress(drawing,active[1],input.Heat(2));
        }

        internal void DrawPreview(DrawingContext drawing, Func<int, double> heat, bool enabled, bool dark)
        {
            DrawState(drawing, enabled && heat != null ? heat(1) : 0,
                enabled && heat != null ? heat(2) : 0, dark);
        }

        private void DrawState(DrawingContext d, double left, double right, bool dark)
        {
            PaintPress(d, active[0], left);
            PaintPress(d, active[1], right);
            d.DrawDrawing(idle[dark ? 1 : 0]);
        }

        private static void PaintPress(DrawingContext d, DrawingGroup overlay, double heat)
        {
            if (Double.IsNaN(heat) || heat <= .005) return;
            d.PushOpacity(Math.Min(1, heat)); d.DrawDrawing(overlay); d.Pop();
        }

        private static Geometry Button(Transform pose, bool right)
        {
            double sign = right ? 1 : -1;
            StreamGeometry g = new StreamGeometry();
            using (StreamGeometryContext s = g.Open())
            {
                s.BeginFigure(new Point(2 * sign, -27), true, true);
                s.BezierTo(new Point(14 * sign, -27), new Point(20 * sign, -18), new Point(21 * sign, -6), true, false);
                s.LineTo(new Point(21 * sign, 0), true, false);
                s.BezierTo(new Point(14 * sign, 2), new Point(8 * sign, 2), new Point(2 * sign, 1), true, false);
            }
            g.Transform = pose; g.Freeze(); return g;
        }

        private static Brush B(byte a, byte r, byte g, byte b)
        { SolidColorBrush bsh = new SolidColorBrush(Color.FromArgb(a, r, g, b)); bsh.Freeze(); return bsh; }
        private static Pen P(Brush brush, double width)
        { Pen pen = new Pen(brush, width); pen.Freeze(); return pen; }
    }
}
