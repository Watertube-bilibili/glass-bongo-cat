using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

// Deterministic pixel extraction from the MIT-licensed original Bongo Cat Mver art.
// This does not redraw the cat: alpha, facial strokes and paw poses come from PNGs.
public static class ExtractArtwork
{
    static Color Ink(Color c)
    {
        int white = Math.Min(c.R, Math.Min(c.G, c.B));
        int strength = 255 - white;
        if (strength == 0 || c.A == 0) return Color.Transparent;
        int alpha = (c.A * strength + 127) / 255;
        return Color.FromArgb(alpha, (c.R-white)*255/strength,
            (c.G-white)*255/strength, (c.B-white)*255/strength);
    }

    static Bitmap Load(string path) { return new Bitmap(path); }
    static string Source(string root, string original, string flat)
    {
        string full=Path.Combine(root,original);
        return File.Exists(full)?full:Path.Combine(root,flat);
    }
    static void DrawPixels(Graphics g, Bitmap b, int x, int y)
    {
        g.DrawImage(b,new Rectangle(x,y,b.Width,b.Height),new Rectangle(0,0,b.Width,b.Height),GraphicsUnit.Pixel);
    }
    static int DeskTop(Bitmap image, int x)
    {
        int expected = (int)Math.Round(151.0 + 108.0*x/611.0);
        for (int y=expected-5; y<=expected+5; y++) {
            Color p=image.GetPixel(x,y);
            if(p.A>200 && p.R<80 && p.G<80 && p.B<80) return y;
        }
        throw new Exception("Desktop line was not found at "+x);
    }

    public static void Main(string[] args)
    {
        string input=args[0], output=args[1];
        Directory.CreateDirectory(output);
        using(Bitmap bg=Load(Path.Combine(input,"bg.png")))
        using(Bitmap lu=Load(Source(input,"lefthand/leftup.png","left-up.png")))
        using(Bitmap ld=Load(Source(input,"lefthand/0.png","left-down.png")))
        using(Bitmap ru=Load(Source(input,"righthand/rightup.png","right-up.png")))
        using(Bitmap rd=Load(Source(input,"righthand/0.png","right-down.png")))
        using(Bitmap body=new Bitmap(bg.Width,bg.Height,PixelFormat.Format32bppArgb))
        using(Bitmap desk=new Bitmap(bg.Width,bg.Height,PixelFormat.Format32bppArgb))
        {
            int w=bg.Width,h=bg.Height,left=DeskTop(bg,0),right=DeskTop(bg,w-1);
            // The original diagonal desk is a separate layer, not part of the body.
            for(int x=0;x<w;x++) for(int y=0;y<h;y++) {
                double boundary=left+(right-left)*(double)x/(w-1);
                double safeBoundary=boundary-3;
                bool inside=bg.GetPixel(x,Math.Max(0,(int)Math.Ceiling(safeBoundary)-1)).A>0;
                Color p=bg.GetPixel(x,y);
                if(y<safeBoundary || (inside && y<boundary)) body.SetPixel(x,y,p);
                if(y>=safeBoundary) desk.SetPixel(x,y,Ink(p));
            }
            desk.Save(Path.Combine(output,"desk-ink.png"),ImageFormat.Png);
            Bitmap[] frames=new Bitmap[4];
            int x0=w,y0=h,x1=-1,y1=-1;
            for(int pose=0;pose<4;pose++) {
                frames[pose]=new Bitmap(w,h,PixelFormat.Format32bppArgb);
                using(Graphics g=Graphics.FromImage(frames[pose])) {
                    DrawPixels(g,body,0,0);
                    DrawPixels(g,(pose&2)!=0?rd:ru,0,0);
                    DrawPixels(g,(pose&1)!=0?ld:lu,0,0);
                }
                for(int y=0;y<h;y++) for(int x=0;x<w;x++)
                    if(frames[pose].GetPixel(x,y).A>0) {
                        x0=Math.Min(x0,x);y0=Math.Min(y0,y);
                        x1=Math.Max(x1,x);y1=Math.Max(y1,y);
                    }
            }
            int cw=x1-x0+1,ch=y1-y0+1;
            for(int pose=0;pose<4;pose++) {
                using(Bitmap mask=new Bitmap(cw,ch,PixelFormat.Format32bppArgb))
                using(Bitmap ink=new Bitmap(cw,ch,PixelFormat.Format32bppArgb))
                {
                    for(int y=0;y<ch;y++) for(int x=0;x<cw;x++) {
                        Color p=frames[pose].GetPixel(x+x0,y+y0);
                        mask.SetPixel(x,y,Color.FromArgb(p.A,255,255,255));
                        ink.SetPixel(x,y,Ink(p));
                    }
                    mask.Save(Path.Combine(output,"body-mask-"+pose+".png"),ImageFormat.Png);
                    ink.Save(Path.Combine(output,"body-ink-"+pose+".png"),ImageFormat.Png);
                }
            }
            using(Bitmap sheet=new Bitmap(w*2,h*2,PixelFormat.Format32bppArgb))
            using(Graphics g=Graphics.FromImage(sheet)) {
                g.Clear(Color.FromArgb(231,235,239));
                for(int pose=0;pose<4;pose++) {
                    int sx=(pose%2)*w,sy=(pose/2)*h;
                    DrawPixels(g,desk,sx,sy);
                    DrawPixels(g,frames[pose],sx,sy);
                    using(Font f=new Font("Arial",13)) g.DrawString("Original pose "+pose,f,Brushes.DimGray,sx+12,sy+12);
                }
                sheet.Save(Path.Combine(output,"original-poses.png"),ImageFormat.Png);
            }
            foreach(Bitmap frame in frames) frame.Dispose();
            string report="Canvas="+w+","+h+"\nBodyBounds="+x0+","+y0+","+cw+","+ch+"\nDeskTop="+left+","+right+"\n";
            File.WriteAllText(Path.Combine(output,"geometry.txt"),report);
            Console.WriteLine(report);
        }
    }
}
