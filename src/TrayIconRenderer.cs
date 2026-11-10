using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace PeripheralBattery;

internal static class TrayIconRenderer
{
    // Original battery motif with transparent, monochrome notification-area styling.
    internal static Bitmap Render(int size,int? percent,bool charging,bool dark)
    {
        var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using var graphics=Graphics.FromImage(bitmap);
        graphics.SmoothingMode=SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
        graphics.ScaleTransform(size/32f,size/32f);
        bool valid=percent is >=0 and <=100;
        var color=!valid?(dark?Color.FromArgb(158,168,180):Color.FromArgb(120,132,145)):
            percent<=20?(dark?Color.FromArgb(241,181,91):Color.FromArgb(172,99,20)):
            (dark?Color.FromArgb(236,240,245):Color.FromArgb(37,49,63));
        using var ink=new SolidBrush(color);
        using var stroke=new Pen(color,2.3f) { StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round };
        using(var body=Rounded(3,8.5f,23,15,3)) graphics.DrawPath(stroke,body);
        graphics.DrawLine(stroke,29,13,29,19);
        if(!valid)
        {
            graphics.DrawLine(stroke,11,16,18,16);
        }
        else if(charging)
        {
            graphics.FillPolygon(ink,new PointF[] { new(17.9f,10.8f),new(10.8f,17),new(15.1f,17),new(13.5f,21.7f),new(21,14),new(16.6f,14) });
        }
        else
        {
            // One continuous level: multiple tiny bars become visual noise at 16px.
            if(percent>0)
            {
                float width=Math.Max(1.6f,16*percent!.Value/100f);
                using var level=Rounded(6.5f,12,width,8,Math.Min(1.5f,width/2));
                graphics.FillPath(ink,level);
            }
        }
        return bitmap;
    }
    internal static Icon Create(int? percent,bool charging,bool dark)
    {
        using var bitmap=Render(Math.Max(16,SystemInformation.SmallIconSize.Width),percent,charging,dark);
        var handle=bitmap.GetHicon();
        try { using var icon=Icon.FromHandle(handle); return (Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }
    static GraphicsPath Rounded(float x,float y,float width,float height,float radius)
    {
        var path=new GraphicsPath(); float d=radius*2;
        path.AddArc(x,y,d,d,180,90); path.AddArc(x+width-d,y,d,d,270,90);
        path.AddArc(x+width-d,y+height-d,d,d,0,90); path.AddArc(x,y+height-d,d,d,90,90);
        path.CloseFigure(); return path;
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
}
