using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassBongoCat;

// Controlled local drawing fixture: no screen capture or input injection.
internal static class CreatePreview
{
    [STAThread]
    private static int Main()
    {
        var art=CatArtwork.Load();var material=new GlassMaterial[4];var inks=new BitmapSource[4];
        var keyboard=new CatKeyboard();var input=new NativeInput();
        var setKey=typeof(NativeInput).GetMethod("SetKey",BindingFlags.Instance|BindingFlags.NonPublic);
        for(int p=0;p<4;p++)
        {material[p]=new GlassMaterial(art.MaskWidth,art.MaskHeight,art.GetMask(p));inks[p]=CatInkTreatment.SoftenOuterInk(art.GetInk(p),art.GetMask(p),art.MaskWidth,art.MaskHeight,.22);}
        var gif=new GifBitmapEncoder();Directory.CreateDirectory("verification");
        for(int f=0;f<32;f++)
        {
            int pose=f<8?0:f<14?2:f<20?0:f<26?1:3;
            input.Enabled=false;input.Enabled=true;
            if((pose&2)!=0)setKey.Invoke(input,new object[]{65,true});
            if((pose&1)!=0)setKey.Invoke(input,new object[]{76,true});
            var background=new DrawingVisual();
            using(var d=background.RenderOpen())
            {
                d.DrawRectangle(new SolidColorBrush(Color.FromRgb(239,243,247)),null,new Rect(0,0,612,354));
                d.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(83,177,176)),null,new Rect(123,28,58,286),5,5);
                d.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(236,160,82)),null,new Rect(274,28,24,286),5,5);
                d.DrawEllipse(new SolidColorBrush(Color.FromRgb(91,129,191)),null,new Point(449,98),55,55);
                for(int y=40;y<310;y+=24)d.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(110,39,69,84)),1),new Point(48,y),new Point(565,y));
                d.DrawText(new FormattedText("CLEAR GLASS  /  0123456789",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),15,new SolidColorBrush(Color.FromRgb(38,61,71)),1),new Point(143,110));
            }
            var source=new RenderTargetBitmap(612,354,96,96,PixelFormats.Pbgra32);source.Render(background);
            var bytes=new byte[612*354*4];source.CopyPixels(bytes,612*4,0);
            material[pose].Render(bytes,612,354,612*4,art.BodyBounds.X,art.BodyBounds.Y,1,1,1.65,1.1,.98,1.35);
            var crystal=BitmapSource.Create(art.MaskWidth,art.MaskHeight,96,96,PixelFormats.Pbgra32,null,material[pose].Pixels,art.MaskWidth*4);
            var drawing=new DrawingVisual();
            using(var d=drawing.RenderOpen())
            {
                d.DrawImage(source,new Rect(0,0,612,354));keyboard.Draw(d,input,true);
                d.DrawImage(crystal,art.BodyBounds);d.DrawImage(inks[pose],art.BodyBounds);
            }
            var frame=new RenderTargetBitmap(612,354,96,96,PixelFormats.Pbgra32);frame.Render(drawing);
            if(f==0){Save(frame,"verification/glass-bongo-cat-preview.png");Save(frame,"verification/glass-bongo-cat-0.1.1.png");}
            if(f==26)Save(frame,"verification/keyboard-pressed.png");
            var metadata=new BitmapMetadata("gif");metadata.SetQuery("/grctlext/Delay",(ushort)8);
            gif.Frames.Add(BitmapFrame.Create(frame,null,metadata,null));
        }
        using(var output=File.Create("verification/glass-bongo-cat-typing.gif"))gif.Save(output);
        var contrast=new DrawingVisual();
        using(var d=contrast.RenderOpen())
        {
            for(int panel=0;panel<2;panel++)
            {
                d.DrawRectangle(panel==0?Brushes.WhiteSmoke:new SolidColorBrush(Color.FromRgb(19,23,29)),null,new Rect(panel*520,0,520,174));
                d.DrawText(new FormattedText(panel==0?"LIGHT DESKTOP":"DARK DESKTOP",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),10,panel==0?Brushes.DimGray:Brushes.LightGray,1),new Point(panel*520+16,8));
                d.PushTransform(new TranslateTransform(panel*520-30,-180));keyboard.Draw(d,input,false);d.Pop();
            }
        }
        var comparison=new RenderTargetBitmap(1040,174,96,96,PixelFormats.Pbgra32);comparison.Render(contrast);Save(comparison,"verification/keyboard-contrast.png");
        input.Dispose();
        Console.WriteLine("Created original Bongo Cat + actual GlassMaterial preview and four-pose typing animation on controlled geometry.");return 0;
    }
    private static void Save(BitmapSource bitmap,string filename)
    {var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(filename))png.Save(output);}
}
