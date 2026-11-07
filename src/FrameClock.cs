using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace GlassBongoCat
{
    // One queued frame at most; UI stalls never produce a catch-up animation backlog.
    internal sealed class FrameClock : IDisposable
    {
        private readonly ManualResetEvent stop=new ManualResetEvent(false);
        private readonly object lifetime=new object();
        private readonly Dispatcher dispatcher;
        private readonly Action frame;
        private readonly Thread worker;
        private volatile bool disposed;
        private bool stopDisposed;
        private int pending;
        internal FrameClock(Dispatcher dispatcher,Action frame)
        {
            this.dispatcher=dispatcher;this.frame=frame;
            worker=new Thread(Run){IsBackground=true,Name="GlassPet frame clock"};worker.Start();
        }
        private void Run()
        {
            bool precision=timeBeginPeriod(1)==0;
            try
            {
                var watch=Stopwatch.StartNew();double next=0;
                while(!disposed)
                {
                    double now=watch.Elapsed.TotalSeconds;
                    if(now<next){if(stop.WaitOne(Math.Max(1,(int)Math.Ceiling((next-now)*1000))))break;continue;}
                    next+=1.0/60;if(next<now)next=now+1.0/60;
                    if(Interlocked.CompareExchange(ref pending,1,0)!=0)continue;
                    if(dispatcher.HasShutdownStarted)break;
                    dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(delegate
                    {
                        try{if(!disposed)frame();}finally{Interlocked.Exchange(ref pending,0);}
                    }));
                }
            }
            catch(InvalidOperationException error){if(!dispatcher.HasShutdownStarted)Trace.WriteLine(error);}
            finally
            {
                if(precision)timeEndPeriod(1);
                lock(lifetime){stop.Dispose();stopDisposed=true;}
            }
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            lock(lifetime){if(!stopDisposed)stop.Set();}
        }
        [DllImport("winmm.dll")]private static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")]private static extern uint timeEndPeriod(uint ms);
    }
}
