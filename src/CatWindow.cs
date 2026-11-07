using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GlassBongoCat
{
    // Native presentation isolates desktop movement from WPF's compositor. WPF
    // still draws the small vector surface offscreen and owns the settings UI.
    internal sealed class CatWindow : IDisposable
    {
        internal readonly CatOptions Options;
        internal readonly NativeInput Input=new NativeInput();
        internal readonly CatSurface Surface;
        internal Action SettingsRequested,PositionChanged;
        internal Action<double> BeforeRender;
        internal ContextMenu ContextMenu;
        internal bool DiagnosticMode,Paused;
        internal long RenderedFrames;
        internal double TotalRenderMilliseconds,MaximumRenderMilliseconds;
        internal double Opacity=1;
        private readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;
        private readonly Stopwatch watch=Stopwatch.StartNew();
        private readonly DrawingVisual drawing=new DrawingVisual();
        private NativeLayerWindow layer;
        private RenderTargetBitmap bitmap;
        private FrameClock clock;
        private DesktopSessionState session;
        private bool sessionSuspended;
        private bool disposed,dragging,moved;
        private bool highContrast=SystemParameters.HighContrast;
        private double left,top,previous,lastRaise,lastMotion;
        private Point down,windowDown;
        internal double DpiScale {get{return layer==null?1:layer.DpiScale;}}
        internal double Scale {get{return Options.Size/CatSurface.DesignWidth;}}
        internal double Width {get{return CatSurface.DesignWidth*Scale;}}
        internal double Height {get{return CatSurface.DesignHeight*Scale;}}
        internal double Left {get{return left;}set{if(left!=value)lastMotion=watch.Elapsed.TotalSeconds;left=value;}}
        internal double Top {get{return top;}set{if(top!=value)lastMotion=watch.Elapsed.TotalSeconds;top=value;}}
        internal bool IsVisible {get{return layer!=null&&layer.IsVisible;}}
        internal IntPtr Handle {get{return layer==null?IntPtr.Zero:layer.Handle;}}

        internal CatWindow(CatOptions options)
        {
            Options=options;
            Surface=new CatSurface(this,options,Input){Width=CatSurface.DesignWidth,Height=CatSurface.DesignHeight};
            Input.Enabled=options.InputEnabled;
        }
        internal void Show()
        {
            dispatcher.VerifyAccess();if(disposed)throw new ObjectDisposedException("CatWindow");
            if(layer==null)
            {
                layer=new NativeLayerWindow("Glass Bongo Cat");layer.MessageHook=Message;
                session=new DesktopSessionState(layer.Handle);
                if(!DiagnosticMode)Input.Attach(layer.Handle,dispatcher);
                Input.Enabled=Options.InputEnabled&&!Paused;
            }
            if(IsVisible)return;
            layer.SetClickThrough(Options.ClickThrough);
            Present();layer.Show();if(CheckSession())ApplyCapture();
            previous=watch.Elapsed.TotalSeconds;
            clock=new FrameClock(dispatcher,RenderFrame);
        }
        internal void Hide()
        {
            dispatcher.VerifyAccess();StopClock();dragging=false;Surface.ResetTouch();
            if(layer!=null){layer.SetPointerCapture(false);Surface.ClearCapture();layer.SetCaptureExclusion(false);layer.Hide();}
        }
        private void StopClock(){if(clock!=null){clock.Dispose();clock=null;}}
        private void RenderFrame()
        {
            if(!IsVisible||disposed)return;
            if(!CheckSession())return;
            long begin=Stopwatch.GetTimestamp();double now=watch.Elapsed.TotalSeconds;
            double dt=Math.Min(.05,Math.Max(.001,now-previous));previous=now;
            if(highContrast!=SystemParameters.HighContrast){highContrast=SystemParameters.HighContrast;ApplyCapture();}
            if(BeforeRender!=null)BeforeRender(now);
            // A non-activating window cannot rely on capture outside its own
            // thread's foreground window. Read the current pointer only during
            // an actual drag; never inject or move the user's system pointer.
            if(dragging)
            {
                if(GetAsyncKeyState(1)>=0)EndDrag();
                else { NativePoint p;if(GetCursorPos(out p))UpdateDrag(new Point(p.X,p.Y)); }
            }
            if(now-lastRaise>2){layer.Raise();lastRaise=now;}
            if(!Paused&&!Options.ReducedMotion&&Options.FollowCursor&&Input.Enabled&&!dragging&&Input.LastActivitySeconds<5)
            {
                Point p=PointFromScreen(Input.Cursor);
                double dx=p.X-Width/2,dy=p.Y-Height/2,distance=Math.Sqrt(dx*dx+dy*dy);
                if(distance>220){double step=Math.Min(180*dt,(distance-220)*.7*dt);Left+=dx/distance*step;Top+=dy/distance*step;KeepOnScreen();}
            }
            Surface.Update(now,dt,Paused,dragging||now-lastMotion<.25);
            Present();
            double elapsed=(Stopwatch.GetTimestamp()-begin)*1000.0/Stopwatch.Frequency;
            RenderedFrames++;TotalRenderMilliseconds+=elapsed;MaximumRenderMilliseconds=Math.Max(MaximumRenderMilliseconds,elapsed);
        }
        private bool CheckSession()
        {
            if(session!=null&&!session.IsActive)
            {
                if(!sessionSuspended)
                {
                    sessionSuspended=true;dragging=false;Surface.ResetTouch();
                    layer.SetPointerCapture(false);Surface.ClearCapture();Input.Enabled=false;
                }
                return false;
            }
            if(sessionSuspended)
            {
                sessionSuspended=false;Surface.ClearCapture();
                Input.Enabled=Options.InputEnabled&&!Paused;previous=watch.Elapsed.TotalSeconds;ApplyCapture();
            }
            return true;
        }
        private void Present()
        {
            RenderTargetBitmap current=RenderBitmap();
            if(!layer.Present(current,(int)Math.Round(Left*DpiScale),(int)Math.Round(Top*DpiScale)))
                throw new InvalidOperationException("无法呈现 Glass Bongo Cat。");
        }
        private RenderTargetBitmap RenderBitmap()
        {
            double pixelScale=Scale*DpiScale;
            int w=Math.Max(1,(int)Math.Ceiling(CatSurface.DesignWidth*pixelScale)),h=Math.Max(1,(int)Math.Ceiling(CatSurface.DesignHeight*pixelScale));
            if(bitmap==null||bitmap.PixelWidth!=w||bitmap.PixelHeight!=h)bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);
            else bitmap.Clear();
            using(DrawingContext d=drawing.RenderOpen())
            {
                d.PushTransform(new ScaleTransform(pixelScale,pixelScale));
                if(Opacity<1)d.PushOpacity(Math.Max(0,Opacity));
                Surface.Draw(d);
                if(Opacity<1)d.Pop();d.Pop();
            }
            bitmap.Render(drawing);return bitmap;
        }
        internal BitmapSource Snapshot(){return RenderBitmap();}
        internal void ApplyOptions()
        {
            Options.Size=Math.Max(160,Math.Min(480,Options.Size));
            Input.Enabled=Options.InputEnabled&&!Paused&&!sessionSuspended;
            if(layer!=null){layer.SetClickThrough(Options.ClickThrough);ApplyCapture();KeepOnScreen();if(IsVisible)Present();}
        }
        private void ApplyCapture()
        {
            if(layer==null)return;
            bool enable=IsVisible&&!sessionSuspended&&!Options.RecordingMode&&!SystemParameters.HighContrast;
            if(!enable)Surface.ClearCapture();layer.SetCaptureExclusion(enable);
        }

        internal void SetPaused(bool value){Paused=value;Input.Enabled=Options.InputEnabled&&!value&&!sessionSuspended;}
        internal void Raise(){if(layer!=null)layer.Raise();}
        internal Point PointToScreen(Point point){return new Point((Left+point.X)*DpiScale,(Top+point.Y)*DpiScale);}
        internal Point PointFromScreen(Point point){return new Point(point.X/DpiScale-Left,point.Y/DpiScale-Top);}
        internal Point SurfaceToScreen(Point point){return PointToScreen(new Point(point.X*Scale,point.Y*Scale));}
        internal Point ScreenToSurface(Point point){Point p=PointFromScreen(point);return new Point(p.X/Scale,p.Y/Scale);}
        internal void ResetPosition()
        {
            Rect area=SystemParameters.WorkArea;Left=area.Right-Width-30;Top=area.Top+24;
            if(PositionChanged!=null)PositionChanged();
        }
        internal void KeepOnScreen()
        {
            Point center=PointToScreen(new Point(Width/2,Height/2));
            var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X,(int)center.Y));
            var work=screen.WorkingArea;
            Left=Math.Max(work.Left/DpiScale,Math.Min(work.Right/DpiScale-Width,Left));
            Top=Math.Max(work.Top/DpiScale,Math.Min(work.Bottom/DpiScale-Height,Top));
        }
        private IntPtr Message(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(session!=null&&session.ProcessMessage(message,wParam)){CheckSession();handled=true;return IntPtr.Zero;}
            Input.ProcessMessage(hwnd,message,wParam,lParam,ref handled);if(handled)return IntPtr.Zero;
            long packed=lParam.ToInt64();Point local=new Point((short)(packed&65535),(short)((packed>>16)&65535));
            if(message==0x84)
            {
                Point p=ScreenToSurface(local);handled=true;
                return new IntPtr(Options.ClickThrough||!Surface.ContainsPoint(p)?-1:1);
            }
            if(message==0x201)
            {
                down=new Point(layer.LeftPixels+local.X,layer.TopPixels+local.Y);windowDown=new Point(Left,Top);
                dragging=true;moved=false;Surface.Pressed=true;layer.SetPointerCapture(true);handled=true;
            }
            else if(message==0x200&&dragging)
            {
                if((wParam.ToInt64()&1)==0)EndDrag();
                else UpdateDrag(new Point(layer.LeftPixels+local.X,layer.TopPixels+local.Y));handled=true;
            }
            else if(message==0x202&&dragging)
            {
                EndDrag();handled=true;
            }
            else if(message==0x203)
            {
                dragging=false;Surface.Pressed=false;layer.SetPointerCapture(false);
                if(SettingsRequested!=null)dispatcher.BeginInvoke(SettingsRequested);handled=true;
            }
            else if(message==0x215){dragging=false;Surface.Pressed=false;}
            else if(message==0x1F){dragging=false;Surface.Pressed=false;layer.SetPointerCapture(false);}
            else if(message==0x205&&ContextMenu!=null)
            {
                double x=(layer.LeftPixels+local.X)/DpiScale,y=(layer.TopPixels+local.Y)/DpiScale;
                dispatcher.BeginInvoke(new Action(delegate{ContextMenu.Placement=PlacementMode.AbsolutePoint;ContextMenu.HorizontalOffset=x;ContextMenu.VerticalOffset=y;ContextMenu.IsOpen=true;}));handled=true;
            }
            else if(message==0x10){if(Input.ExitRequested!=null)dispatcher.BeginInvoke(Input.ExitRequested);handled=true;}
            return IntPtr.Zero;
        }
        internal void Close(){Dispose();}
        private void UpdateDrag(Point screen)
        {
            Vector delta=screen-down;if(delta.Length>3)moved=true;
            Left=windowDown.X+delta.X/DpiScale;Top=windowDown.Y+delta.Y/DpiScale;
            Surface.PullX=Math.Max(-1,Math.Min(1,delta.X/120));Surface.PullY=Math.Max(-1,Math.Min(1,delta.Y/120));
        }
        private void EndDrag()
        {
            dragging=false;Surface.Pressed=false;layer.SetPointerCapture(false);
            if(!moved)Surface.Pat();KeepOnScreen();if(PositionChanged!=null)PositionChanged();
        }
        public void Dispose()
        {
            if(disposed)return;dispatcher.VerifyAccess();disposed=true;StopClock();
            if(ContextMenu!=null)ContextMenu.IsOpen=false;
            Surface.Dispose();Input.Dispose();
            if(session!=null){session.Dispose();session=null;}
            if(layer!=null){layer.SetPointerCapture(false);layer.Dispose();layer=null;}
        }
        [StructLayout(LayoutKind.Sequential)]private struct NativePoint{internal int X,Y;}
        [DllImport("user32.dll")]private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    }
}
