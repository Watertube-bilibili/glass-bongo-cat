using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Reproducible Windows icon derived only from the original Bongo Cat mask/ink.
public static class MakeIcon
{
    static bool Edge(Bitmap mask,int x,int y,int radius)
    {
        for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++) {
            if(dx*dx+dy*dy>radius*radius)continue;
            int px=x+dx,py=y+dy;
            if(px<0||py<0||px>=mask.Width||py>=mask.Height||mask.GetPixel(px,py).A<128)return true;
        }
        return false;
    }
    static void DrawPixels(Graphics g,Bitmap b,Rectangle dest)
    {g.DrawImage(b,dest,new Rectangle(0,0,b.Width,b.Height),GraphicsUnit.Pixel);}
    public static void Main(string[] args)
    {
        string art=args[0],iconPath=args[1],variantPath=args[2];
        using(Bitmap mask=new Bitmap(Path.Combine(art,"body-mask-0.png")))
        using(Bitmap ink=new Bitmap(Path.Combine(art,"body-ink-0.png")))
        using(Bitmap filled=new Bitmap(mask.Width,mask.Height,PixelFormat.Format32bppArgb))
        using(Bitmap soft=new Bitmap(mask.Width,mask.Height,PixelFormat.Format32bppArgb))
        {
            int changed=0,coloredUnchanged=0;
            for(int y=0;y<mask.Height;y++)for(int x=0;x<mask.Width;x++) {
                Color m=mask.GetPixel(x,y),p=ink.GetPixel(x,y);
                bool edge=Edge(mask,x,y,6);
                double t=(double)y/mask.Height;
                int r=(int)(251-27*t),g=(int)(254-14*t),b=255;
                if(edge){r=247;g=252;b=255;}
                filled.SetPixel(x,y,Color.FromArgb(m.A,r,g,b));
                int max=Math.Max(p.R,Math.Max(p.G,p.B)),min=Math.Min(p.R,Math.Min(p.G,p.B));
                if(p.A>0&&max-min<=15&&edge){p=Color.FromArgb((int)Math.Round(p.A*.22),p.R,p.G,p.B);changed++;}
                else if(p.A>0&&max-min>15)coloredUnchanged++;
                soft.SetPixel(x,y,p);
            }
            using(Graphics g=Graphics.FromImage(filled))DrawPixels(g,ink,new Rectangle(0,0,ink.Width,ink.Height));
            soft.Save(variantPath,ImageFormat.Png);
            int[] sizes={16,32,48,256};byte[][] payloads=new byte[sizes.Length][];
            for(int n=0;n<sizes.Length;n++)using(Bitmap icon=new Bitmap(sizes[n],sizes[n],PixelFormat.Format32bppArgb)) {
                using(Graphics g=Graphics.FromImage(icon)) {
                    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    int pad=Math.Max(1,(int)Math.Round(sizes[n]*.045));
                    int width=sizes[n]-2*pad,height=(int)Math.Round(width*(double)filled.Height/filled.Width);
                    DrawPixels(g,filled,new Rectangle(pad,(sizes[n]-height)/2,width,height));
                }
                using(MemoryStream stream=new MemoryStream()){icon.Save(stream,ImageFormat.Png);payloads[n]=stream.ToArray();}
                icon.Save(Path.Combine(Path.GetDirectoryName(iconPath),"GlassBongoCat-"+sizes[n]+".png"),ImageFormat.Png);
            }
            using(BinaryWriter writer=new BinaryWriter(File.Create(iconPath))) {
                writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);
                uint offset=(uint)(6+16*sizes.Length);
                for(int n=0;n<sizes.Length;n++) {
                    writer.Write((byte)(sizes[n]==256?0:sizes[n]));writer.Write((byte)(sizes[n]==256?0:sizes[n]));
                    writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);
                    writer.Write((uint)payloads[n].Length);writer.Write(offset);offset+=(uint)payloads[n].Length;
                }
                foreach(byte[] data in payloads)writer.Write(data);
            }
            Console.WriteLine("Softened neutral edge pixels="+changed+"; colored pixels unchanged="+coloredUnchanged);
            Console.WriteLine("Icon contains transparent 16/32/48/256 PNG frames.");
        }
    }
}
