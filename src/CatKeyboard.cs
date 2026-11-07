using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBongoCat
{
    // One optical atlas covers the case, keys and mouse; lettering stays sharp above it.
    internal sealed class CatKeyboard
    {
        private sealed class Key { internal int Code,Alternate; internal Point Center; internal Rect Bounds; internal DrawingGroup ActiveDark,ActiveLight; internal BitmapSource DarkBitmap,LightBitmap; }
        internal static readonly Rect Bounds=new Rect(48,190,510,164);
        private const int TextureWidth=340,TextureHeight=110;
        private readonly List<Key> keys=new List<Key>();
        private readonly CatMouse mouse=new CatMouse();
        private readonly WriteableBitmap glass=new WriteableBitmap(TextureWidth,TextureHeight,96,96,PixelFormats.Pbgra32,null);
        private readonly WriteableBitmap fallback=new WriteableBitmap(TextureWidth,TextureHeight,96,96,PixelFormats.Pbgra32,null);
        private DrawingGroup light,dark,housingMask,keysMask;
        private Transform plane;
        private BitmapSource lightBitmap,darkBitmap;
        private double pixelScale=1;
        private KeyboardGlass optics;
        private bool facesCat,showMouse,configured,hasGlass;
        private int fallbackVersion=-1;
        private static readonly Typeface Face=new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
        internal int KeyCount {get{return keys.Count;}}
        internal bool HasRefraction {get{return hasGlass;}}
        internal bool FacesCat {get{return facesCat;}}
        internal CatKeyboard(){Configure(true,true);}
        internal void Configure(bool catFacing,bool mouseVisible)
        {
            if(configured&&facesCat==catFacing&&showMouse==mouseVisible)return;
            facesCat=catFacing;showMouse=mouseVisible;configured=true;keys.Clear();
            var transform=new TransformGroup();
            if(facesCat)transform.Children.Add(new RotateTransform(180,212,52));
            transform.Children.Add(new MatrixTransform(showMouse?new Matrix(.82,.82*108/611,-.25,.78,174,202):new Matrix(1,108.0/611,-.25,.7,92,192)));
            transform.Freeze();plane=transform;
            light=new DrawingGroup();dark=new DrawingGroup();housingMask=new DrawingGroup();keysMask=new DrawingGroup();
            using(var l=light.Open())using(var d=dark.Open())using(var h=housingMask.Open())using(var k=keysMask.Open())
            {
                l.PushTransform(plane);d.PushTransform(plane);h.PushTransform(plane);k.PushTransform(plane);
                var outline=new RectangleGeometry(new Rect(0,0,424,104),8,8);outline.Freeze();
                h.DrawGeometry(Brushes.White,null,outline);DrawCase(l,outline);DrawCase(d,outline);
                const double pitch=18.3;
                Row(l,d,k,8,new[]{"esc","1","2","3","4","5","6","7","8","9","0","−","=","⌫"},new[]{27,49,50,51,52,53,54,55,56,57,48,189,187,8},new double[]{1,1,1,1,1,1,1,1,1,1,1,1,1,2});
                Row(l,d,k,8+pitch,new[]{"tab","Q","W","E","R","T","Y","U","I","O","P","[","]","\\"},new[]{9,81,87,69,82,84,89,85,73,79,80,219,221,220},new double[]{1.5,1,1,1,1,1,1,1,1,1,1,1,1,1.5});
                Row(l,d,k,8+pitch*2,new[]{"caps","A","S","D","F","G","H","J","K","L",";","'","enter"},new[]{20,65,83,68,70,71,72,74,75,76,186,222,13},new double[]{1.75,1,1,1,1,1,1,1,1,1,1,1,2.25});
                Row(l,d,k,8+pitch*3,new[]{"shift","Z","X","C","V","B","N","M",",",".","/","shift"},new[]{160,90,88,67,86,66,78,77,188,190,191,161},new double[]{2.25,1,1,1,1,1,1,1,1,1,1,2.75});
                Row(l,d,k,8+pitch*4,new[]{"ctrl","win","alt","","alt","fn","menu","ctrl"},new[]{162,91,164,32,165,0,93,163},new double[]{1.25,1.25,1.25,6.25,1.25,1.25,1.25,1.25});
                l.Pop();d.Pop();h.Pop();k.Pop();
                if(showMouse){h.DrawGeometry(Brushes.White,null,mouse.HousingGeometry);k.DrawGeometry(Brushes.White,null,mouse.ButtonsGeometry);mouse.DrawOverlay(l,null,false,false);mouse.DrawOverlay(d,null,false,true);}
            }
            light.Freeze();dark.Freeze();housingMask.Freeze();keysMask.Freeze();
            RebuildInkBitmaps();
            byte[] housing=Mask(housingMask),caps=Mask(keysMask);
            if(optics==null)optics=new KeyboardGlass(TextureWidth,TextureHeight,housing,caps);else optics.SetMasks(housing,caps);
            fallbackVersion=-1;hasGlass=false;RefreshFallback(1.1,.98,1.35);
        }
        internal void SetPixelScale(double scale)
        {
            scale=Math.Max(.2,Math.Min(4,scale));if(Math.Abs(pixelScale-scale)<.001)return;
            pixelScale=scale;RebuildInkBitmaps();
        }
        private void RebuildInkBitmaps()
        {
            lightBitmap=Raster(light,Bounds,pixelScale);darkBitmap=Raster(dark,Bounds,pixelScale);
            foreach(Key key in keys){key.LightBitmap=Raster(key.ActiveLight,key.Bounds,pixelScale);key.DarkBitmap=Raster(key.ActiveDark,key.Bounds,pixelScale);}
        }
        private static BitmapSource Raster(Drawing drawing,Rect bounds,double scale)
        {
            var visual=new DrawingVisual();using(var d=visual.RenderOpen())
            {d.PushTransform(new MatrixTransform(new Matrix(scale,0,0,scale,-bounds.Left*scale,-bounds.Top*scale)));d.DrawDrawing(drawing);d.Pop();}
            var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(bounds.Width*scale)),Math.Max(1,(int)Math.Ceiling(bounds.Height*scale)),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
        }
        private static byte[] Mask(DrawingGroup shapes)
        {
            var visual=new DrawingVisual();
            using(var d=visual.RenderOpen())
            {
                d.PushTransform(new MatrixTransform(new Matrix(TextureWidth/Bounds.Width,0,0,TextureHeight/Bounds.Height,-Bounds.X*TextureWidth/Bounds.Width,-Bounds.Y*TextureHeight/Bounds.Height)));
                d.DrawDrawing(shapes);d.Pop();
            }
            var bitmap=new RenderTargetBitmap(TextureWidth,TextureHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            byte[] pixels=new byte[TextureWidth*TextureHeight*4],alpha=new byte[TextureWidth*TextureHeight];bitmap.CopyPixels(pixels,TextureWidth*4,0);
            for(int i=0;i<alpha.Length;i++)alpha[i]=pixels[i*4+3];return alpha;
        }
        internal void ClearBackdrop(){hasGlass=false;}
        internal void RefreshFallback(double highlight,double clarity,double thickness)
        {
            optics.RenderFallback(highlight,clarity,thickness);
            if(fallbackVersion==optics.FallbackVersion)return;
            fallback.WritePixels(new Int32Rect(0,0,TextureWidth,TextureHeight),optics.FallbackPixels,TextureWidth*4,0);fallbackVersion=optics.FallbackVersion;
        }
        internal void RenderBackdrop(BackdropFrame source,Rect screenBounds,double refraction,double highlight,double clarity,double thickness)
        {
            double x=screenBounds.Left-source.ScreenBounds.Left,y=screenBounds.Top-source.ScreenBounds.Top;
            if(!screenBounds.IntersectsWith(source.ScreenBounds)){x=(source.Width-screenBounds.Width)*.5;y=(source.Height-screenBounds.Height)*.5;}
            optics.Render(source.Pixels,source.Width,source.Height,source.Stride,x,y,screenBounds.Width/TextureWidth,screenBounds.Height/TextureHeight,refraction,highlight,clarity,thickness);
            glass.WritePixels(new Int32Rect(0,0,TextureWidth,TextureHeight),optics.Pixels,TextureWidth*4,0);hasGlass=true;
        }
        internal void Draw(DrawingContext drawing,NativeInput input,bool enabled){Draw(drawing,input,enabled,1,null);}
        internal void Draw(DrawingContext drawing,NativeInput input,bool enabled,double opacity,Geometry visibleCapture)
        {
            drawing.DrawImage(fallback,Bounds);
            if(hasGlass&&opacity>.001)
            {
                if(visibleCapture!=null)drawing.PushClip(visibleCapture);
                drawing.PushOpacity(opacity);drawing.DrawImage(glass,Bounds);drawing.Pop();
                if(visibleCapture!=null)drawing.Pop();
            }
            bool darkBackground=hasGlass&&optics.MeanBrightness<.35;
            drawing.DrawImage(darkBackground?darkBitmap:lightBitmap,Bounds);
            if(enabled&&input!=null)foreach(Key key in keys)
            {
                double heat=key.Code==0?0:input.Heat(key.Code);
                if(key.Alternate!=0)heat=Math.Max(heat,input.Heat(key.Alternate));
                DrawHeat(drawing,key,heat,darkBackground);
            }
            if(showMouse)mouse.DrawFeedback(drawing,input,enabled);
        }
        internal bool TryGetKeyCenter(int virtualKey,out Point center)
        {
            if(virtualKey==16)virtualKey=160;if(virtualKey==17)virtualKey=162;if(virtualKey==18)virtualKey=164;
            foreach(Key key in keys)if(key.Code==virtualKey||(virtualKey!=0&&key.Alternate==virtualKey)){center=key.Center;return true;}
            center=new Point();return false;
        }
        private static void DrawHeat(DrawingContext d,Key key,double heat,bool darkBackground)
        {
            if(Double.IsNaN(heat)||heat<=.005)return;
            d.PushOpacity(Math.Min(1,heat));d.DrawImage(darkBackground?key.DarkBitmap:key.LightBitmap,key.Bounds);d.Pop();
        }
        private void Row(DrawingContext l,DrawingContext d,DrawingContext mask,double y,string[] labels,int[] codes,double[] widths)
        {
            double x=8;
            for(int i=0;i<labels.Length;i++)
            {
                Rect cap=new Rect(x,y,widths[i]*27.2-2.9,14.7);var shape=new RectangleGeometry(cap,2.5,2.5);shape.Freeze();
                Geometry glyph=Label(labels[i],cap,labels[i].Length>1?11.3:15.5);
                mask.DrawGeometry(Brushes.White,null,shape);DrawCap(l,cap,shape,glyph,false,false);DrawCap(d,cap,shape,glyph,true,false);
                var al=new DrawingGroup();var ad=new DrawingGroup();
                using(var a=al.Open()){a.PushTransform(plane);DrawCap(a,cap,shape,glyph,false,true);a.Pop();}
                using(var a=ad.Open()){a.PushTransform(plane);DrawCap(a,cap,shape,glyph,true,true);a.Pop();}
                al.Freeze();ad.Freeze();Rect keyBounds=plane.TransformBounds(cap);keyBounds.Inflate(3,3);
                keys.Add(new Key {Code=codes[i],Alternate=codes[i]==91?92:0,Center=plane.Transform(new Point(cap.X+cap.Width/2,cap.Y+cap.Height/2)),Bounds=keyBounds,ActiveLight=al,ActiveDark=ad});
                x+=widths[i]*27.2;
            }
        }
        private static void DrawCase(DrawingContext d,Geometry outline)
        {
            d.DrawGeometry(Brush(8,230,242,255),Pen(Brush(100,18,36,56),2),outline);
            d.DrawGeometry(null,Pen(Brush(205,255,255,255),.9),outline);
            d.DrawLine(Pen(Brush(125,255,255,255),1),new Point(12,2),new Point(410,2));
        }
        private static void DrawCap(DrawingContext d,Rect cap,Geometry shape,Geometry glyph,bool dark,bool active)
        {
            d.DrawGeometry(Brush(active?(byte)105:(byte)15,active?(byte)95:(byte)235,active?(byte)180:(byte)245,255),Pen(Brush(active?(byte)230:(byte)100,20,60,100),active?1.4:.8),shape);
            d.DrawGeometry(null,Pen(Brush(190,255,255,255),.55),shape);
            d.DrawLine(Pen(Brush(active?(byte)235:(byte)155,255,255,255),active?1.1:.7),new Point(cap.Left+2,cap.Top+1),new Point(cap.Right-2,cap.Top+1));
            Brush ink=dark?Brush(255,247,251,255):Brush(255,22,38,53);
            Pen halo=Pen(dark?Brush(230,13,27,43):Brush(235,255,255,255),1.65);
            if(glyph!=null){d.DrawGeometry(null,halo,glyph);d.DrawGeometry(ink,null,glyph);}
            else d.DrawRoundedRectangle(ink,halo,new Rect(cap.X+cap.Width*.35,cap.Y+cap.Height*.65,cap.Width*.3,.8),.4,.4);
            if(active)d.DrawLine(Pen(Brush(240,83,172,255),1.5),new Point(cap.Left+3,cap.Bottom-1.2),new Point(cap.Right-3,cap.Bottom-1.2));
        }
        private static Geometry Label(string label,Rect cap,double size)
        {
            if(label.Length==0)return null;
            var text=new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Face,size,Brushes.Black,1);
            Geometry glyph=text.BuildGeometry(new Point());Rect bounds=glyph.Bounds;
            glyph.Transform=new TranslateTransform(cap.X+(cap.Width-bounds.Width)/2-bounds.X,cap.Y+(cap.Height-bounds.Height)/2-bounds.Y-.15);glyph.Freeze();return glyph;
        }
        private static Brush Brush(byte a,byte r,byte g,byte b){var result=new SolidColorBrush(Color.FromArgb(a,r,g,b));result.Freeze();return result;}
        private static Pen Pen(Brush brush,double width){var p=new Pen(brush,width);p.Freeze();return p;}
    }
}
