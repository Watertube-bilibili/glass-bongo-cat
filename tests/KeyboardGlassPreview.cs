using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using GlassBongoCat;

internal static class KeyboardGlassPreview
{
    private static int Main()
    {
        Directory.CreateDirectory("artifacts");
        byte[] housing,keys;KeyboardGlassTests.MakeMasks(320,110,out housing,out keys);
        var material=new KeyboardGlass(320,110,housing,keys);
        foreach(bool dark in new[]{false,true})
        using(var background=new Bitmap(400,190,PixelFormat.Format32bppArgb))
        {
            using(var g=Graphics.FromImage(background))
            {
                g.Clear(dark?Color.FromArgb(24,34,44):Color.FromArgb(246,246,240));
                using(var a=new SolidBrush(dark?Color.FromArgb(35,106,103):Color.FromArgb(72,178,163)))g.FillRectangle(a,20,0,62,190);
                using(var a=new SolidBrush(dark?Color.FromArgb(172,80,40):Color.FromArgb(237,143,81)))g.FillRectangle(a,166,0,22,190);
                using(var a=new SolidBrush(dark?Color.FromArgb(48,71,149):Color.FromArgb(117,152,220)))g.FillEllipse(a,265,14,100,100);
                using(var pen=new Pen(dark?Color.FromArgb(79,103,119):Color.FromArgb(180,191,195)))
                {
                    for(int x=0;x<400;x+=21)g.DrawLine(pen,x,0,x,190);
                    for(int y=0;y<190;y+=19)g.DrawLine(pen,0,y,400,y);
                }
            }
            var source=new byte[400*190*4];var locked=background.LockBits(new Rectangle(0,0,400,190),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            Marshal.Copy(locked.Scan0,source,0,source.Length);background.UnlockBits(locked);
            material.Render(source,400,190,1600,40,40,1,1);
            using(Bitmap normal=Composite(material,source,false,dark))
            using(Bitmap fallback=Composite(material,source,true,dark))
            using(var sheet=new Bitmap(1300,624,PixelFormat.Format32bppArgb))
            using(var g=Graphics.FromImage(sheet))
            using(var label=new Font("Segoe UI",12))
            {
                g.Clear(Color.White);g.DrawString("LIVE REFRACTION / 61 KEYS",label,Brushes.Black,14,8);
                g.DrawImageUnscaled(normal,14,38);g.DrawString("TRANSPARENT FALLBACK",label,Brushes.Black,350,8);g.DrawImageUnscaled(fallback,350,38);
                g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
                g.DrawImage(normal,new Rectangle(14,168,1280,440),new Rectangle(0,0,320,110),GraphicsUnit.Pixel);
                string name=dark?"dark":"light";
                normal.Save("artifacts/keyboard-glass-"+name+"-native.png",ImageFormat.Png);
                sheet.Save("artifacts/keyboard-glass-"+name+"-proof.png",ImageFormat.Png);
            }
        }
        Console.WriteLine("PASS: controlled-background keyboard material/fallback PNGs written without a window or screen capture.");return 0;
    }
    private static Bitmap Composite(KeyboardGlass material,byte[] source,bool fallback,bool dark)
    {
        if(fallback)material.RenderFallback();byte[] pixels=fallback?material.FallbackPixels:material.Pixels;
        var rgba=new byte[320*110*4];
        for(int y=0;y<110;y++)for(int x=0;x<320;x++)
        {
            int p=(y*320+x)*4,s=((y+40)*400+x+40)*4,a=pixels[p+3];
            for(int c=0;c<3;c++)rgba[p+c]=(byte)(pixels[p+c]+(source[s+c]*(255-a)+127)/255);rgba[p+3]=255;
        }
        var result=new Bitmap(320,110,PixelFormat.Format32bppArgb);
        var locked=result.LockBits(new Rectangle(0,0,320,110),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);Marshal.Copy(rgba,0,locked.Scan0,rgba.Length);result.UnlockBits(locked);
        using(var g=Graphics.FromImage(result))using(var font=new Font("Segoe UI",7.2f,FontStyle.Bold))
        {
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int[] counts={14,14,13,12,8};string labels="1234567890ABCDQWERTYUIOPASDFGHZXCVBNM1234567890ABCDEF0123456789";int at=0;
            for(int row=0;row<5;row++)for(int key=0;key<counts[row];key++)
            {
                string text=labels[(at++)%labels.Length].ToString();SizeF size=g.MeasureString(text,font);
                double pitch=300.0/counts[row];float x=(float)(10+key*pitch+(pitch-3-size.Width)*.5),y=(float)(9+row*18.4+(15.4-size.Height)*.5);
                g.DrawString(text,font,dark?Brushes.White:Brushes.Black,x,y);
            }
        }
        return result;
    }
}
