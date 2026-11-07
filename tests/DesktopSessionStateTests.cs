using System;
using System.Runtime.InteropServices;
using System.Windows;
using GlassBongoCat;

internal static class DesktopSessionStateTests
{
    private static int assertions;
    private static void Check(bool okay, string message) { assertions++; if (!okay) throw new Exception(message); }
    private static int Main()
    {
        try
        {
            IntPtr buffer = Marshal.AllocHGlobal(20);
            try
            {
                for (int i=0;i<20;i++) Marshal.WriteByte(buffer,i,0);
                Marshal.WriteInt32(buffer,0,1); Marshal.WriteInt32(buffer,12,0); Marshal.WriteInt32(buffer,16,1);
                Check(DesktopSessionState.Decode(buffer,20).Active,"active unlocked session");
                Marshal.WriteInt32(buffer,16,0); Check(!DesktopSessionState.Decode(buffer,20).Active,"startup detects already locked session");
                Marshal.WriteInt32(buffer,16,1); Marshal.WriteInt32(buffer,12,4); Check(!DesktopSessionState.Decode(buffer,20).Active,"disconnected session");
                Marshal.WriteInt32(buffer,12,0); Marshal.WriteInt32(buffer,16,-1); Check(!DesktopSessionState.Decode(buffer,20).Active,"unknown session state fails closed");
                Check(!DesktopSessionState.Decode(buffer,16).Active,"truncated WTS response");
            }
            finally { Marshal.FreeHGlobal(buffer); }
            double time=0;int reads=0;bool current=true;
            using(var state=new DesktopSessionState(IntPtr.Zero,delegate {reads++;return new DesktopSessionState.Probe {Active=current,Reason=current?"":"locked"};},delegate{return time;}))
            {
                Check(reads==1&&state.IsActive,"startup reader runs immediately");
                for(int i=0;i<600;i++)Check(state.IsActive,"cached foreground session");
                Check(reads==1,"frame checks do not poll WTS every frame");
                Check(state.ProcessMessage(0x2B1,new IntPtr(7))&&!state.IsActive,"lock notification pauses immediately");
                current=false;time=1100;Check(!state.IsActive&&reads==2,"fallback poll observes locked session");
                current=true;Check(state.ProcessMessage(0x2B1,new IntPtr(8))&&state.IsActive,"unlock refreshes authoritative state");
                current=false;time=2200;Check(!state.IsActive,"missed disconnect notification recovered by polling");
                current=true;time=3300;Check(state.IsActive,"missed unlock notification recovers automatically");
                Check(!state.ProcessMessage(0x200,IntPtr.Zero),"unrelated window messages remain untouched");
            }
            Rect desktop=new Rect(-1920,-200,3840,1400);
            for(int i=0;i<1000;i++)
            {
                var lens=new Rect(-300+i*.113,200+i*.037,381*400.0/612,268*400.0/612);
                Rect area=AsyncDesktopBackdrop.SamplingBounds(lens,desktop);
                Check(area.Width==Math.Ceiling(lens.Width)+193&&area.Height==Math.Ceiling(lens.Height)+193,"fractional motion cannot resize cached DIB");
                Check(area.Contains(lens),"fixed capture extent still contains the entire pet");
            }
            Rect narrow=AsyncDesktopBackdrop.SamplingBounds(new Rect(2015,-100,100,100),desktop);
            Check(narrow.Width==1,"single-pixel desktop intersection remains available");
            string reason;bool active=DesktopSessionState.QueryActive(out reason);
            Console.WriteLine("Runtime session active="+active+"; "+reason);
            Console.WriteLine("PASS "+assertions+" session-state / stable-capture-geometry assertions; no actual session state was changed.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
