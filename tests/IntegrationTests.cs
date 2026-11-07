using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GlassBongoCat;

internal static class IntegrationTests
{
    private static int assertions;
    private static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    [STAThread]
    private static int Main()
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};CatWindow cat=null;
        try
        {
            VerifyKeyboard();
            CatArtwork art=CatArtwork.Load();
            Check(art.MaskWidth>250&&art.MaskHeight>150,"Original cat crop is unexpectedly small");
            for(int pose=0;pose<4;pose++)
            {
                byte[] mask=art.GetMask(pose);Check(mask.Length==art.MaskWidth*art.MaskHeight,"Pose dimensions");
                int inside=0,outside=0;foreach(byte a in mask){if(a>240)inside++;if(a==0)outside++;}
                Check(inside>15000&&outside>2000,"Pose is missing a filled cat or transparent surroundings");
                Check(art.GetInk(pose).PixelWidth==art.MaskWidth,"Original face/line alignment");
                if(pose>0){int difference=0;for(int i=0;i<mask.Length;i++)if(mask[i]!=art.GetMask(0)[i])difference++;Check(difference>100,"Paws do not move between original poses");}
            }
            var options=new CatOptions {InputEnabled=false};cat=new CatWindow(options){DiagnosticMode=true,Opacity=0,Left=200,Top=150};cat.Show();
            string sessionReason;bool activeDesktop=DesktopSessionState.QueryActive(out sessionReason);
            var warmup=Stopwatch.StartNew();while(activeDesktop&&!cat.Surface.HasRefraction&&warmup.Elapsed.TotalSeconds<3)Pump(.1);
            Check(cat.IsVisible&&GetForegroundWindow()!=cat.Handle,"Native cat activates the desktop");
            uint affinity;
            if(activeDesktop)
            {
                Check(cat.Surface.HasRefraction,"No captured backdrop after warmup: "+cat.Surface.CaptureStatus+"; frames="+cat.RenderedFrames);
                Check(GetWindowDisplayAffinity(cat.Handle,out affinity)&&affinity==0x11,"Self-capture exclusion");
            }
            else
            {
                Pump(.2);Check(!cat.Surface.HasRefraction,"Locked session unexpectedly captured the desktop");
                Console.WriteLine("SKIP live desktop capture / edge / protected-window checks: "+sessionReason);
            }
            cat.Surface.TestLeft=true;cat.Surface.Update(1,.016,false,false);Check(cat.Surface.Pose==1,"Left paw pose");
            cat.Surface.TestRight=true;cat.Surface.Update(1.02,.016,false,false);Check(cat.Surface.Pose==3,"Both paws pose");
            cat.Surface.TestLeft=false;cat.Surface.Update(1.04,.016,false,false);Check(cat.Surface.Pose==2,"Right paw pose");
            cat.Surface.TestRight=false;
            cat.DiagnosticMode=false;options.InputEnabled=true;cat.Input.Enabled=true;
            MethodInfo setKey=typeof(NativeInput).GetMethod("SetKey",BindingFlags.Instance|BindingFlags.NonPublic);
            setKey.Invoke(cat.Input,new object[]{65,true});cat.Surface.Update(1.06,.016,false,false);Check(cat.Surface.Pose==2,"A key does not drive the paw over the left keyboard half");
            setKey.Invoke(cat.Input,new object[]{76,true});cat.Surface.Update(1.08,.016,false,false);Check(cat.Surface.Pose==3,"L key does not drive the right original paw");
            cat.Input.Enabled=false;cat.Input.Enabled=true;
            setKey.Invoke(cat.Input,new object[]{161,true});setKey.Invoke(cat.Input,new object[]{16,true});
            cat.Surface.Update(1.09,.016,false,false);Check(cat.Surface.Pose==1,"Right Shift must drive only the paw over the right keyboard half");
            cat.Input.Enabled=false;options.InputEnabled=false;cat.DiagnosticMode=true;
            cat.Surface.Update(1.1,.016,false,false);Check(cat.Surface.Pose==0,"Disabling input leaves a stuck paw");
            if(activeDesktop)
            {
            var desktop=System.Windows.Forms.SystemInformation.VirtualScreen;
            double cx=art.BodyBounds.X+art.BodyBounds.Width*.5,cy=art.BodyBounds.Y+art.BodyBounds.Height*.5;
            Point[] targets={new Point(desktop.Left+2,desktop.Top+desktop.Height/2),new Point(desktop.Right-2,desktop.Top+desktop.Height/2),new Point(desktop.Left+desktop.Width/2,desktop.Top+2),new Point(desktop.Left+desktop.Width/2,desktop.Bottom-2),new Point(desktop.Left+2,desktop.Top+2),new Point(desktop.Right-2,desktop.Top+2),new Point(desktop.Left+2,desktop.Bottom-2),new Point(desktop.Right-2,desktop.Bottom-2)};
            int sampled=0,lost=0,phase=0;var watch=Stopwatch.StartNew();
            cat.BeforeRender=delegate(double t)
            {
                phase=Math.Min(7,(int)(watch.Elapsed.TotalSeconds/.35));
                cat.Left=targets[phase].X/cat.DpiScale-cx*cat.Scale;cat.Top=targets[phase].Y/cat.DpiScale-cy*cat.Scale;
                if(!cat.Surface.HasRefraction)lost++;sampled++;
            };
            Pump(3.0);cat.BeforeRender=null;
            Check(phase==7&&sampled>100&&lost==0,"Refraction lost at screen edge: "+lost+"/"+sampled);
            Console.WriteLine("Edge checks="+sampled+"; lost refraction="+lost);
            cat.Left=220;cat.Top=170;Pump(.4);
            using(var protectedWindow=new NativeLayerWindow("Glass Bongo Cat protected content fixture"))
            {
                Point split=cat.SurfaceToScreen(new Point(cx,art.BodyBounds.Top));
                var clear=BitmapSource.Create(400,400,96,96,PixelFormats.Pbgra32,null,new byte[400*400*4],1600);
                protectedWindow.Present(clear,(int)split.X,(int)split.Y);protectedWindow.Show();protectedWindow.SetCaptureExclusion(true);cat.Raise();Pump(.4);
                cat.Opacity=1;BitmapSource frame=cat.Snapshot();cat.Opacity=0;
                byte[] pixels=new byte[frame.PixelWidth*frame.PixelHeight*4];frame.CopyPixels(pixels,frame.PixelWidth*4,0);
                Point normal=Interior(art,false),covered=Interior(art,true);
                int a=AlphaAt(pixels,frame.PixelWidth,cat,normal),b=AlphaAt(pixels,frame.PixelWidth,cat,covered);
                Check(a>220&&b<90,"Protected content is overwritten: normal="+a+", protected="+b);
                Console.WriteLine("Protected overlap alpha="+b+"; ordinary glass alpha="+a);
            }
            }
            options.RecordingMode=true;cat.ApplyOptions();Check(!cat.Surface.HasRefraction,"Recording retains live sample");
            Check(GetWindowDisplayAffinity(cat.Handle,out affinity)&&affinity==0,"Recording exclusion was not restored");
            cat.Hide();Check(!cat.IsVisible,"Hide failed");cat.Show();Check(cat.IsVisible,"Show failed");
            Console.WriteLine("PASS "+assertions+" integration assertions: original cat/paws, local input state, capture mode and window lifecycle; live desktop checks="+(activeDesktop?"executed":"SKIPPED (inactive/locked session)")+". No global input injected.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{if(cat!=null)cat.Dispose();app.Shutdown();}
    }
    private static void VerifyKeyboard()
    {
        var keyboard=new CatKeyboard();
        using(var input=new NativeInput())
        {
            Check(keyboard.KeyCount>=50,"Keyboard is missing its full compact key layout");
            Point a=new Point(),l=new Point(),space=new Point(),rightShift=new Point();
            Check(keyboard.TryGetKeyCenter(65,out a)&&keyboard.TryGetKeyCenter(76,out l),"Letter key mapping");
            Check(keyboard.TryGetKeyCenter(32,out space)&&keyboard.TryGetKeyCenter(161,out rightShift),"Space/right Shift mapping");
            byte[] idle=KeyboardPixels(keyboard,input,true);
            foreach(Point p in new[]{a,l,space,rightShift})
            {
                Check(p.X>=0&&p.X<612&&p.Y>=0&&p.Y<354,"Key is clipped outside the pet window");
                Check(idle[((int)p.Y*612+(int)p.X)*4+3]>240,"Keycaps are transparent and disappear into the desktop");
            }
            var setKey=typeof(NativeInput).GetMethod("SetKey",BindingFlags.Instance|BindingFlags.NonPublic);
            setKey.Invoke(input,new object[]{65,true});
            byte[] pressed=KeyboardPixels(keyboard,input,true);
            Check(PatchDifference(idle,pressed,a)>100,"A key has no visible pressed feedback");
            Check(PatchDifference(idle,pressed,l)==0,"A incorrectly lights the unrelated L key");
            Check(PatchDifference(idle,KeyboardPixels(keyboard,input,false),a)==0,"Pause retains a lit key");
            input.Enabled=false;
            Check(PatchDifference(idle,KeyboardPixels(keyboard,input,true),a)==0,"Disabling input leaves a stuck key");
            Console.WriteLine("Keyboard visible keycaps / exact key feedback / pause and reset checks passed.");
        }
    }
    private static byte[] KeyboardPixels(CatKeyboard keyboard,NativeInput input,bool enabled)
    {
        var drawing=new DrawingVisual();using(var d=drawing.RenderOpen())keyboard.Draw(d,input,enabled);
        var frame=new RenderTargetBitmap(612,354,96,96,PixelFormats.Pbgra32);frame.Render(drawing);
        byte[] pixels=new byte[612*354*4];frame.CopyPixels(pixels,612*4,0);return pixels;
    }
    private static long PatchDifference(byte[] before,byte[] after,Point point)
    {
        long difference=0;
        for(int y=(int)point.Y-5;y<=(int)point.Y+5;y++)for(int x=(int)point.X-5;x<=(int)point.X+5;x++)
            for(int channel=0;channel<4;channel++){int i=(y*612+x)*4+channel;difference+=Math.Abs(before[i]-after[i]);}
        return difference;
    }
    private static Point Interior(CatArtwork art,bool right)
    {
        int w=art.MaskWidth,h=art.MaskHeight;byte[] mask=art.GetMask(0),ink=new byte[w*h*4];
        new FormatConvertedBitmap(art.GetInk(0),PixelFormats.Bgra32,null,0).CopyPixels(ink,w*4,0);
        for(int y=h/3;y<h*2/3;y++)for(int x=right?w*3/5:w/4;x<(right?w*4/5:w*2/5);x++)
        {
            bool safe=ink[(y*w+x)*4+3]<5;
            for(int dy=-16;dy<=16&&safe;dy+=4)for(int dx=-16;dx<=16;dx+=4)if(mask[(y+dy)*w+x+dx]<250){safe=false;break;}
            if(safe)return new Point(art.BodyBounds.X+x,art.BodyBounds.Y+y);
        }
        throw new Exception("No clean cat interior sample found");
    }
    private static int AlphaAt(byte[] pixels,int stridePixels,CatWindow cat,Point point)
    {double scale=cat.Scale*cat.DpiScale;return pixels[((int)(point.Y*scale)*stridePixels+(int)(point.X*scale))*4+3];}
    private static void Pump(double seconds)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(seconds)};
        timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool GetWindowDisplayAffinity(IntPtr hwnd,out uint affinity);
}
