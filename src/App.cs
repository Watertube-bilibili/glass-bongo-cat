using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Serialization;
using Forms=System.Windows.Forms;

namespace GlassBongoCat
{
    internal sealed class App : Application
    {
        private static readonly string DataFolder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GlassBongoCat");
        private CatOptions options;
        private CatWindow cat;
        private SettingsWindow settings;
        private Forms.NotifyIcon tray;
        private Mutex singleton;
        private DispatcherTimer saveTimer;
        private bool closing,diagnostic;

        [STAThread]
        private static void Main(string[] args)
        {
            var app=new App {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            if(Array.IndexOf(args,"--diagnostic")>=0){app.RunDiagnostic(args);return;}
            bool first;app.singleton=new Mutex(true,"Local\\Watertube.GlassBongoCat",out first);
            if(!first){MessageBox.Show("Glass Bongo Cat 已经在运行。\n双击猫咪或托盘图标可以打开设置。","Glass Bongo Cat");app.singleton.Dispose();return;}
            app.Startup+=delegate{app.Initialize();};
            app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e)
            {
                try{Directory.CreateDirectory(DataFolder);File.WriteAllText(Path.Combine(DataFolder,"last-error.txt"),e.Exception.ToString());}catch{}
                MessageBox.Show("猫咪遇到一个错误，信息已保存在本机。\n"+e.Exception.Message,"Glass Bongo Cat");
                e.Handled=true;app.Quit();
            };
            app.Run();
        }
        private void Initialize()
        {
            options=LoadOptions();cat=new CatWindow(options);
            cat.SettingsRequested=OpenSettings;cat.PositionChanged=SaveSoon;
            cat.Input.ToggleRequested=Toggle;cat.Input.SettingsRequested=OpenSettings;
            cat.Input.PauseRequested=Pause;cat.Input.ExitRequested=Quit;
            cat.ContextMenu=new ContextMenu();
            Menu(cat.ContextMenu,"设置",OpenSettings);Menu(cat.ContextMenu,"暂停 / 继续敲键盘",Pause);
            Menu(cat.ContextMenu,"找回猫咪",delegate{cat.Show();cat.ResetPosition();});
            Menu(cat.ContextMenu,"隐藏",Toggle);Menu(cat.ContextMenu,"退出",Quit);
            if(Double.IsNaN(options.Left)||Double.IsNaN(options.Top))cat.ResetPosition();
            else{cat.Left=options.Left;cat.Top=options.Top;}
            cat.Show();cat.KeepOnScreen();
            tray=new Forms.NotifyIcon {Text="Glass Bongo Cat · 右键设置",Visible=true,Icon=System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location)};
            var menu=new Forms.ContextMenuStrip();
            menu.Items.Add("设置",null,delegate{OpenSettings();});menu.Items.Add("显示 / 隐藏",null,delegate{Toggle();});
            menu.Items.Add("暂停 / 继续敲键盘",null,delegate{Pause();});
            menu.Items.Add("找回猫咪",null,delegate{cat.Show();cat.ResetPosition();});
            menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,delegate{Quit();});
            tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{OpenSettings();};
            saveTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(400)};
            saveTimer.Tick+=delegate{saveTimer.Stop();SaveOptions();};
        }
        private static void Menu(ContextMenu menu,string label,Action action)
        {var item=new MenuItem {Header=label};item.Click+=delegate{action();};menu.Items.Add(item);}
        private void OpenSettings()
        {
            if(settings!=null){settings.Show();settings.Activate();return;}
            settings=new SettingsWindow(options,delegate{options.Validate();cat.ApplyOptions();SaveSoon();},delegate{cat.Show();cat.ResetPosition();},cat.Input.Status);
            settings.Closed+=delegate{settings=null;SaveOptions();};settings.Show();
        }
        private void Toggle(){if(cat.IsVisible)cat.Hide();else{cat.Show();cat.KeepOnScreen();}}
        private void Pause(){cat.SetPaused(!cat.Paused);}
        private void SaveSoon(){if(saveTimer!=null){saveTimer.Stop();saveTimer.Start();}}
        private static CatOptions LoadOptions()
        {
            CatOptions result;
            try{using(var s=File.OpenRead(Path.Combine(DataFolder,"settings.xml")))result=(CatOptions)new XmlSerializer(typeof(CatOptions)).Deserialize(s);}
            catch{result=new CatOptions();}result.Validate();return result;
        }
        private void SaveOptions()
        {
            if(diagnostic||options==null)return;
            try
            {
                if(cat!=null){options.Left=cat.Left;options.Top=cat.Top;}
                Directory.CreateDirectory(DataFolder);string target=Path.Combine(DataFolder,"settings.xml"),temp=target+".tmp";
                using(var s=File.Create(temp))new XmlSerializer(typeof(CatOptions)).Serialize(s,options);
                if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        private void Quit()
        {
            if(closing)return;closing=true;SaveOptions();if(saveTimer!=null)saveTimer.Stop();
            if(settings!=null)settings.Close();if(cat!=null)cat.Dispose();
            if(tray!=null){tray.Visible=false;tray.Dispose();}
            if(singleton!=null){singleton.ReleaseMutex();singleton.Dispose();}Shutdown();
        }
        private void RunDiagnostic(string[] args)
        {
            diagnostic=true;options=new CatOptions {InputEnabled=false,Size=400};
            string folder=args[args.Length-1];Directory.CreateDirectory(folder);
            string sessionReason;
            if(!DesktopSessionState.QueryActive(out sessionReason))
            {
                File.WriteAllText(Path.Combine(folder,"diagnostic.txt"),"Live desktop diagnostic skipped: "+sessionReason+Environment.NewLine+"No normal-desktop FPS result was measured. Unlock normally and rerun this diagnostic."+Environment.NewLine);
                return;
            }
            var background=new Window {Title="Glass Bongo Cat · optical fixture",Width=900,Height=620,Left=80,Top=70,Topmost=true,ShowActivated=false,Content=new OpticalFixture()};
            background.Show();cat=new CatWindow(options){DiagnosticMode=true,Left=210,Top=155};cat.Show();
            var elapsed=System.Diagnostics.Stopwatch.StartNew();
            int step=0;long frames=0,captures=0;double ms=0,begin=0;bool moving=false;
            cat.BeforeRender=delegate(double seconds)
            {
                if(moving){cat.Left=240+Math.Sin(seconds*1.7)*110;cat.Top=170+Math.Sin(seconds*.9)*25;}
                cat.Surface.TestLeft=step>=1 && ((int)(seconds*4)%2==0);
                cat.Surface.TestRight=step>=1 && ((int)(seconds*3)%2==0);
            };
            var timer=new DispatcherTimer(DispatcherPriority.Send){Interval=TimeSpan.FromSeconds(2)};
            timer.Tick+=delegate
            {
                try
                {
                    if(step==0)
                    {
                        SaveBitmap(cat.Snapshot(),Path.Combine(folder,"cat-live.png"));
                        File.WriteAllText(Path.Combine(folder,"diagnostic.txt"),"HasRefraction="+cat.Surface.HasRefraction+Environment.NewLine);
                        frames=cat.RenderedFrames;captures=cat.Surface.CapturedFrames;ms=cat.TotalRenderMilliseconds;begin=elapsed.Elapsed.TotalSeconds;moving=true;
                    }
                    else if(step==6)
                    {
                        moving=false;double seconds=elapsed.Elapsed.TotalSeconds-begin;long count=cat.RenderedFrames-frames;
                        File.AppendAllText(Path.Combine(folder,"diagnostic.txt"),"Moving presented frames="+count+"; seconds="+seconds.ToString("F3")+"; fps="+(count/seconds).ToString("F1")+"; mean full frame ms="+((cat.TotalRenderMilliseconds-ms)/Math.Max(1,count)).ToString("F3")+"; captures="+(cat.Surface.CapturedFrames-captures)+Environment.NewLine);
                        SaveBitmap(cat.Snapshot(),Path.Combine(folder,"cat-typing.png"));
                        settings=new SettingsWindow(options,delegate{cat.ApplyOptions();},delegate{},"诊断模式"){ShowActivated=false};settings.Show();
                    }
                    else if(step==7)
                    {
                        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(settings.ActualWidth),(int)Math.Ceiling(settings.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(settings);SaveBitmap(bitmap,Path.Combine(folder,"settings.png"));settings.Close();
                    }
                    else if(step==8){cat.Dispose();background.Close();timer.Stop();Shutdown();}
                    step++;
                }catch(Exception error){File.AppendAllText(Path.Combine(folder,"diagnostic.txt"),error.ToString());timer.Stop();cat.Dispose();background.Close();Shutdown(1);}
            };
            timer.Start();Run();
        }
        internal static void SaveBitmap(BitmapSource bitmap,string path)
        {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(path))encoder.Save(stream);}
        private sealed class OpticalFixture:FrameworkElement
        {
            protected override void OnRender(DrawingContext d)
            {
                d.DrawRectangle(Brushes.WhiteSmoke,null,new Rect(0,0,900,620));
                var colors=new[]{Color.FromRgb(23,150,159),Color.FromRgb(237,170,66),Color.FromRgb(60,101,181),Color.FromRgb(235,96,81)};
                for(int x=0;x<900;x+=90)d.DrawRectangle(new SolidColorBrush(colors[(x/90)%4]),null,new Rect(x,0,30,620));
                for(int y=0;y<620;y+=32)d.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(50,64,79)),1.4),new Point(0,y),new Point(900,y));
                d.DrawText(new FormattedText("GLASS BONGO CAT   0123456789",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),25,Brushes.Black,1),new Point(50,170));
            }
        }
    }
}
