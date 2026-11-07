using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlassBongoCat
{
    internal sealed class CatSurface : FrameworkElement, IDisposable
    {
        internal const double DesignWidth=612,DesignHeight=354;
        private readonly CatWindow owner;
        private readonly CatOptions options;
        private readonly NativeInput input;
        private readonly CatArtwork artwork;
        private readonly CatKeyboard keyboard=new CatKeyboard();
        private readonly GlassMaterial[] materials=new GlassMaterial[4];
        private readonly WriteableBitmap[] glass=new WriteableBitmap[4],fallback=new WriteableBitmap[4];
        private readonly BitmapSource[] lightInk=new BitmapSource[4];
        private readonly BitmapSource[] ink=new BitmapSource[4];
        private readonly int[] fallbackVersions=new int[4];
        private readonly AsyncDesktopBackdrop capture=new AsyncDesktopBackdrop();
        private readonly BackdropOcclusion occlusion=new BackdropOcclusion();
        private static readonly int[] LeftOther={9,20,160,162,164};
        private static readonly int[] RightOther={8,13,161,163,165,37,38,39,40,186,187,188,189,190,191,192,219,220,221,222};
        private BackdropFrame backdrop;
        private int pose;
        private double lastRequest=-1,lastReceived=-1,opacity;
        private bool pressed,paused;
        internal bool TestLeft,TestRight;
        internal double PullX,PullY;
        internal bool HasRefraction {get{return backdrop!=null;}}
        internal long CapturedFrames {get{return capture.CapturedFrames;}}
        internal string CaptureStatus {get{return capture.FailureReason;}}
        internal string CaptureTimings {get{return "total="+capture.LastCaptureMilliseconds.ToString("F3")+"; precheck="+capture.LastPrecheckMilliseconds.ToString("F3")+"; buffer="+capture.LastBufferMilliseconds.ToString("F3")+"; BitBlt="+capture.LastBlitMilliseconds.ToString("F3")+"; GdiFlush="+capture.LastFlushMilliseconds.ToString("F3")+"; postcheck="+capture.LastPostcheckMilliseconds.ToString("F3")+"; copy="+capture.LastCopyMilliseconds.ToString("F3")+"; publish="+capture.LastPublishWaitMilliseconds.ToString("F3")+"; DIBs="+capture.NativeBufferCreations;}}
        internal int Pose {get{return pose;}}
        internal Rect BodyBounds {get{return artwork.BodyBounds;}}
        internal bool Pressed {get{return pressed;}set{pressed=value;}}

        internal CatSurface(CatWindow owner,CatOptions options,NativeInput input)
        {
            this.owner=owner;this.options=options;this.input=input;artwork=CatArtwork.Load();
            for(int p=0;p<4;p++)
            {
                materials[p]=new GlassMaterial(artwork.MaskWidth,artwork.MaskHeight,artwork.GetMask(p));
                glass[p]=new WriteableBitmap(artwork.MaskWidth,artwork.MaskHeight,96,96,PixelFormats.Pbgra32,null);
                fallback[p]=new WriteableBitmap(artwork.MaskWidth,artwork.MaskHeight,96,96,PixelFormats.Pbgra32,null);
                ink[p]=CatInkTreatment.SoftenOuterInk(artwork.GetInk(p),artwork.GetMask(p),artwork.MaskWidth,artwork.MaskHeight,.22);
                lightInk[p]=LightInk(ink[p]);RefreshFallback(p);
            }
        }
        internal void Pat() { }
        internal void ResetTouch(){pressed=false;}
        internal void ClearCapture(){capture.Stop();backdrop=null;opacity=0;keyboard.ClearBackdrop();}
        internal void Update(double now,double dt,bool isPaused,bool moving)
        {
            paused=isPaused;int previous=pose;
            keyboard.Configure(options.KeyboardFacesCat,options.ShowMouse);
            keyboard.SetPixelScale(owner.Scale*owner.DpiScale);
            if(owner.DiagnosticMode)pose=(TestLeft?1:0)|(TestRight?2:0);
            else if(paused||!options.InputEnabled)pose=0;
            else
            {
                bool left=HandActive(true),right=HandActive(false);
                // Original Bongo Cat sprites provide the raised / tapping paws.
                // Holding a key holds the corresponding paw, with a short tail
                // from the transient input heat so very short taps stay visible.
                // The source names paws from the cat's point of view. Match
                // the visible left/right keyboard halves from the viewer's side.
                pose=options.KeyboardFacesCat?((left?1:0)|(right?2:0)):((left?2:0)|(right?1:0));
                if(input.LeftButton||input.RightButton)pose|=options.ShowMouse?2:(input.LeftButton?2:1);
            }
            bool allowed=!options.RecordingMode&&!SystemParameters.HighContrast;
            if(!allowed&&backdrop!=null)ClearCapture();
            bool fresh=false;
            if(allowed&&(moving||now-lastRequest>.045))
            {
                lastRequest=now;
                capture.Request(owner.Handle,ScreenBounds(CaptureBounds()));
            }
            if(allowed)
            {
                BackdropFrame ready;
                if(capture.TryGetSnapshot(out ready)){backdrop=ready;lastReceived=now;fresh=true;}
                double target=backdrop!=null&&now-lastReceived<.4?1:0;
                opacity+=(target-opacity)*(1-Math.Exp(-14*dt));
                if(backdrop!=null&&now-lastReceived>1&&opacity<.005){backdrop=null;opacity=0;keyboard.ClearBackdrop();}
            }
            RefreshFallback(pose);
            keyboard.RefreshFallback(options.Highlight,options.Clarity,options.Thickness);
            if(backdrop!=null&&(fresh||moving||pose!=previous||!keyboard.HasRefraction))
            {
                RenderGlass();
                if(fresh||moving||!keyboard.HasRefraction)keyboard.RenderBackdrop(backdrop,ScreenBounds(CatKeyboard.Bounds),options.Refraction,options.Highlight,options.Clarity,options.Thickness);
            }
            InvalidateVisual();
        }
        private bool HandActive(bool left)
        {
            const string leftKeys="12345QWERTASDFGZXCVB";
            const string rightKeys="67890YUIOPHJKLNM";
            string keys=left?leftKeys:rightKeys;
            foreach(char key in keys)if(input.Heat(key)>.35)return true;
            if(input.Heat(32)>.35)return true;
            int[] other=left?LeftOther:RightOther;
            foreach(int key in other)if(input.Heat(key)>.35)return true;
            return false;
        }
        private Rect ScreenBounds()
        {return ScreenBounds(artwork.BodyBounds);}
        private Rect ScreenBounds(Rect bounds)
        {return new Rect(owner.SurfaceToScreen(bounds.TopLeft),owner.SurfaceToScreen(bounds.BottomRight));}
        private Rect CaptureBounds(){return Rect.Union(artwork.BodyBounds,CatKeyboard.Bounds);}
        private void RefreshFallback(int p)
        {
            GlassMaterial material=materials[p];material.RenderFallback(options.Highlight,options.Clarity,options.Thickness);
            if(fallbackVersions[p]==material.FallbackVersion)return;
            fallback[p].WritePixels(new Int32Rect(0,0,material.Width,material.Height),material.FallbackPixels,material.Width*4,0);
            fallbackVersions[p]=material.FallbackVersion;
        }
        private void RenderGlass()
        {
            Rect bounds=ScreenBounds();GlassMaterial material=materials[pose];
            double sx=bounds.Left-backdrop.ScreenBounds.Left,sy=bounds.Top-backdrop.ScreenBounds.Top;
            if(!bounds.IntersectsWith(backdrop.ScreenBounds))
            {sx=(backdrop.Width-bounds.Width)*.5;sy=(backdrop.Height-bounds.Height)*.5;}
            material.Render(backdrop.Pixels,backdrop.Width,backdrop.Height,backdrop.Stride,sx,sy,
                bounds.Width/material.Width,bounds.Height/material.Height,options.Refraction,options.Highlight,options.Clarity,options.Thickness);
            glass[pose].WritePixels(new Int32Rect(0,0,material.Width,material.Height),material.Pixels,material.Width*4,0);
        }
        private Geometry VisibleCaptureRegion()
        {
            Rect local=CaptureBounds(),screen=ScreenBounds(local);var regions=occlusion.GetRegions(owner.Handle,screen);
            if(regions.Count==0)return null;
            var mask=new GeometryGroup {FillRule=FillRule.Nonzero};
            foreach(Rect region in regions)
                mask.Children.Add(new RectangleGeometry(new Rect(local.Left+(region.Left-screen.Left)*local.Width/screen.Width,
                    local.Top+(region.Top-screen.Top)*local.Height/screen.Height,region.Width*local.Width/screen.Width,region.Height*local.Height/screen.Height)));
            var visible=new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(local),mask);visible.Freeze();return visible;
        }
        protected override void OnRender(DrawingContext drawing){Draw(drawing);}
        internal void Draw(DrawingContext drawing)
        {
            Geometry visible=backdrop!=null?VisibleCaptureRegion():null;
            keyboard.Draw(drawing,input,!paused&&options.InputEnabled,opacity,visible);
            drawing.DrawImage(fallback[pose],artwork.BodyBounds);
            if(backdrop!=null&&!options.RecordingMode&&!SystemParameters.HighContrast)
            {
                if(visible!=null)drawing.PushClip(visible);
                drawing.PushOpacity(opacity);drawing.DrawImage(glass[pose],artwork.BodyBounds);drawing.Pop();
                if(visible!=null)drawing.Pop();
            }
            bool dark=backdrop!=null&&materials[pose].MeanBrightness<.27;
            drawing.DrawImage(dark?lightInk[pose]:ink[pose],artwork.BodyBounds);
            if(paused)
            {
                drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(215,31,44,55)),null,new Rect(276,315,60,25),10,10);
                drawing.DrawRectangle(Brushes.White,null,new Rect(299,321,4,13));drawing.DrawRectangle(Brushes.White,null,new Rect(309,321,4,13));
            }
        }
        internal bool ContainsPoint(Point point)
        {
            if(artwork.BodyBounds.Contains(point))
            {
                int x=Math.Min(artwork.MaskWidth-1,(int)((point.X-artwork.BodyBounds.Left)*artwork.MaskWidth/artwork.BodyBounds.Width));
                int y=Math.Min(artwork.MaskHeight-1,(int)((point.Y-artwork.BodyBounds.Top)*artwork.MaskHeight/artwork.BodyBounds.Height));
                if(x>=0&&y>=0&&artwork.GetMask(pose)[y*artwork.MaskWidth+x]>8)return true;
            }
            return new Rect(30,210,550,135).Contains(point);
        }
        private static BitmapSource LightInk(BitmapSource original)
        {
            var rgba=new FormatConvertedBitmap(original,PixelFormats.Bgra32,null,0);
            int stride=rgba.PixelWidth*4;byte[] pixels=new byte[stride*rgba.PixelHeight];rgba.CopyPixels(pixels,stride,0);
            for(int i=0;i<pixels.Length;i+=4)
            {
                int max=Math.Max(pixels[i],Math.Max(pixels[i+1],pixels[i+2]));int min=Math.Min(pixels[i],Math.Min(pixels[i+1],pixels[i+2]));
                if(max<140&&max-min<35)pixels[i]=pixels[i+1]=pixels[i+2]=245;
            }
            var result=BitmapSource.Create(rgba.PixelWidth,rgba.PixelHeight,96,96,PixelFormats.Bgra32,null,pixels,stride);result.Freeze();return result;
        }
        public void Dispose(){capture.Dispose();backdrop=null;}
    }
}
