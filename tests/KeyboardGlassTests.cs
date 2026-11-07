using System;
using System.Diagnostics;
using System.IO;
using GlassBongoCat;

internal static class KeyboardGlassTests
{
    private static int assertions;
    private static int Main(string[] args)
    {
        try
        {
            Reject(delegate { new KeyboardGlass(0, 20, new byte[0], new byte[0]); }, "dimensions");
            Reject(delegate { new KeyboardGlass(10, 10, new byte[100], new byte[99]); }, "mask length");
            byte[] housing, keys; MakeMasks(320, 110, out housing, out keys);
            var glass = new KeyboardGlass(320, 110, housing, keys);
            byte[] source = Source(420, 220, 1696);
            glass.Render(source, 420, 220, 1696, 11, 17, 1, 1, 0, 0, 1);
            for (int y=0,i=0;y<110;y++)for(int x=0;x<320;x++,i++)
            {
                int alpha=Math.Max(housing[i],keys[i]),p=i*4,s=(y+17)*1696+(x+11)*4;
                Check(glass.Pixels[p+3]==alpha,"union alpha remains exact");
                for(int channel=0;channel<3;channel++)
                    Check(Math.Abs(glass.Pixels[p+channel]-(source[s+channel]*alpha+127)/255)<=1,"zero optics reproduces the padded-stride background");
            }
            int version=glass.MapVersion;
            glass.Render(source,420,220,1696,.3,.7,1.1,1.1,1.2,.8,.99);
            Check(glass.MapVersion==version,"strength and screen position do not rebuild maps");
            glass.SetMasks((byte[])housing.Clone(),(byte[])keys.Clone());
            Check(glass.MapVersion==version,"identical masks skip geometry work");
            glass.Render(source,420,220,1696,.375,.625,1.125,1.25,0,0,1);
            for(int y=8;y<90;y+=7)for(int x=8;x<300;x+=11)
            {
                int i=y*320+x;
                if(housing[i]!=255)continue;
                double sx=.375+(x+.5)*1.125-.5,sy=.625+(y+.5)*1.25-.5;
                int xx=(int)sx,yy=(int)sy;double fx=sx-xx,fy=sy-yy;
                for(int c=0;c<3;c++)
                {
                    double expected=source[yy*1696+xx*4+c]*(1-fx)*(1-fy)+source[yy*1696+(xx+1)*4+c]*fx*(1-fy)+source[(yy+1)*1696+xx*4+c]*(1-fx)*fy+source[(yy+1)*1696+(xx+1)*4+c]*fx*fy;
                    Check(Math.Abs(glass.Pixels[i*4+c]-expected)<=1.01,"fixed-point moving sampler agrees with bilinear reference");
                }
            }
            Check(glass.MaximumDisplacement>5,"housing and keycap rays have visible travel");
            glass.Render(source,420,220,1696,0,0,1,1,1,0,1,.65);double thin=glass.MaximumDisplacement;
            glass.Render(source,420,220,1696,0,0,1,1,1,0,1,1.8);
            Check(glass.MaximumDisplacement>thin*1.5,"thickness changes refraction, not opacity alone");
            Check(glass.MapVersion==version+2,"only thickness transitions rebuild the combined map");
            var neutral=new byte[]{118,118,118,255};
            glass.Render(neutral,1,1,4,-10000,10000,3,2,2.5,2,.2,1.8);
            glass.Render(neutral,1,1,4,-1e200,1e200,1e200,1e200,2.5,2,.2,1.8);
            for(int p=0;p<glass.Pixels.Length;p+=4)
            {
                Check(glass.Pixels[p]==glass.Pixels[p+1]&&glass.Pixels[p+1]==glass.Pixels[p+2],"material remains neutral");
                Check(glass.Pixels[p]<=glass.Pixels[p+3],"output is premultiplied");
            }
            byte[] old=(byte[])glass.Pixels.Clone();glass.RenderFallback();int fallback=glass.FallbackVersion;glass.RenderFallback();
            Check(glass.FallbackVersion==fallback,"fallback cache is reusable");
            int strong=0;
            for(int p=0;p<old.Length;p+=4)
            {
                Check(old[p]==glass.Pixels[p]&&old[p+3]==glass.Pixels[p+3],"fallback cannot overwrite live refraction");
                Check(glass.FallbackPixels[p]<=glass.FallbackPixels[p+3],"fallback is premultiplied");
                strong=Math.Max(strong,glass.FallbackPixels[p+3]);
            }
            Check(strong>35&&strong<220,"fallback has transparent glass facets");
            Check(glass.FallbackPixels[(55*320+160)*4+3]<15,"fallback center stays clear");
            CheckIndividualKeycaps();
            var single=new KeyboardGlass(1,1,new byte[]{128},new byte[]{0});single.Render(neutral,1,1,4,0,0,1,1);
            Check(single.Pixels[3]==128,"one pixel source and mask are safe");
            glass.SetMasks(new byte[housing.Length],new byte[keys.Length]);glass.Render(neutral,1,1,4,0,0,1,1);glass.RenderFallback();
            for(int p=0;p<glass.Pixels.Length;p++)Check(glass.Pixels[p]==0&&glass.FallbackPixels[p]==0,"new masks clear old output completely");
            Reject(delegate{glass.Render(neutral,2,1,4,0,0,1,1);},"short row");
            Reject(delegate{glass.Render(neutral,1,2,4,0,0,1,1);},"short source");
            Reject(delegate{glass.Render(neutral,1,1,4,0,0,0,1);},"invalid scale");
            Reject(delegate{glass.Render(neutral,1,1,4,0,0,1,1,Double.NaN);},"nonfinite optics");
            Console.WriteLine("PASS: "+assertions+" keyboard mask/refraction/cache assertions; 61 keys share one map.");
            if(Array.IndexOf(args,"--perf")>=0)Benchmark();
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static void CheckIndividualKeycaps()
    {
        const int w=100,h=40;var housing=new byte[w*h];var keys=new byte[w*h];
        for(int i=0;i<housing.Length;i++)housing[i]=255;
        Rounded(keys,w,h,8,7,30,26,3);Rounded(keys,w,h,60,7,30,26,3);
        var glass=new KeyboardGlass(w,h,housing,keys);var plain=new KeyboardGlass(w,h,housing,new byte[w*h]);
        var ramp=new byte[120*60*4];
        for(int y=0;y<60;y++)for(int x=0;x<120;x++){int p=(y*120+x)*4;ramp[p+2]=(byte)(x*2);ramp[p+1]=(byte)(y*3);ramp[p]=80;ramp[p+3]=255;}
        glass.Render(ramp,120,60,480,0,0,1,1,1,0,1);plain.Render(ramp,120,60,480,0,0,1,1,1,0,1);
        foreach(int left in new[]{8,60})
        {
            int a=(20*w+left+2)*4+2,b=(20*w+left+27)*4+2;
            Check(glass.Pixels[a]>plain.Pixels[a]+3,"each separate key left bevel bends inward");
            Check(glass.Pixels[b]<plain.Pixels[b]-3,"each separate key right bevel bends inward");
        }
    }
    internal static void MakeMasks(int width,int height,out byte[] housing,out byte[] keys)
    {
        housing=new byte[width*height];keys=new byte[width*height];
        Rounded(housing,width,height,2,2,width-4,height-4,8);
        int[] counts={14,14,13,12,8};
        double pitch=(height-18)/5.0;
        for(int row=0;row<5;row++)
        {
            int count=counts[row];double keyWidth=(width-20)/(double)count;
            for(int key=0;key<count;key++)Rounded(keys,width,height,10+key*keyWidth,9+row*pitch,keyWidth-3,pitch-3,2.3);
        }
    }
    private static void Rounded(byte[] mask,int width,int height,double left,double top,double w,double h,double radius)
    {
        radius=Math.Min(radius,Math.Min(w,h)*.5);
        for(int y=Math.Max(0,(int)top-1);y<Math.Min(height,(int)Math.Ceiling(top+h)+1);y++)
            for(int x=Math.Max(0,(int)left-1);x<Math.Min(width,(int)Math.Ceiling(left+w)+1);x++)
            {
                int count=0;
                for(int yy=0;yy<2;yy++)for(int xx=0;xx<2;xx++)
                {
                    double px=x+(xx+.5)/2,py=y+(yy+.5)/2;
                    double dx=Math.Max(0,Math.Abs(px-(left+w*.5))-(w*.5-radius));
                    double dy=Math.Max(0,Math.Abs(py-(top+h*.5))-(h*.5-radius));
                    if(dx*dx+dy*dy<=radius*radius&&px>=left&&px<=left+w&&py>=top&&py<=top+h)count++;
                }
                mask[y*width+x]=Math.Max(mask[y*width+x],(byte)((count*255+2)/4));
            }
    }
    internal static byte[] Source(int width,int height,int stride)
    {
        var pixels=new byte[stride*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {int p=y*stride+x*4;pixels[p]=(byte)((x*3+y*7)%256);pixels[p+1]=(byte)((y*3)%256);pixels[p+2]=(byte)((x*2)%256);pixels[p+3]=255;}
        return pixels;
    }
    private static void Benchmark()
    {
        byte[] housing,keys;MakeMasks(320,110,out housing,out keys);
        var glass=new KeyboardGlass(320,110,housing,keys);byte[] source=Source(640,360,2560);
        for(int i=0;i<400;i++)glass.Render(source,640,360,2560,40+i%7*.13,50,1,1);
        var timings=new double[1600];int gc=GC.CollectionCount(0);long memory=GC.GetTotalMemory(false);
        for(int i=0;i<timings.Length;i++)
        {
            long start=Stopwatch.GetTimestamp();glass.Render(source,640,360,2560,40+i%7*.13,50,1,1);
            timings[i]=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
        }
        long memoryAfter=GC.GetTotalMemory(false);int collections=GC.CollectionCount(0)-gc;
        double total=0;foreach(double value in timings)total+=value;Array.Sort(timings);
        string report="35,200 pixels / 61-key composite / 1,600 warmed frames: mean="+(total/timings.Length).ToString("F3")+" ms; p50="+timings[timings.Length/2].ToString("F3")+" ms; p95="+timings[(int)(timings.Length*.95)].ToString("F3")+" ms; Gen0="+collections+"; managed-memory delta="+(memoryAfter-memory)+" bytes; maximum default ray="+glass.MaximumDisplacement.ToString("F2")+" px.";
        Console.WriteLine(report);Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/keyboard-glass-perf.txt",report+Environment.NewLine);
    }
    private static void Check(bool okay,string message){assertions++;if(!okay)throw new Exception("FAIL: "+message);}
    private static void Reject(Action action,string message){try{action();}catch(ArgumentException){assertions++;return;}throw new Exception("FAIL expected exception: "+message);}
}
