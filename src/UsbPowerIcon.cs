using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;

namespace PeripheralBattery;

internal static class UsbPowerIcon
{
    // Draw a USB-A plug directly so it stays sharp at taskbar and detail sizes.
    internal static C.Image Create(M.Brush color,double size)
    {
        var drawing=new M.DrawingGroup();
        using(var canvas=drawing.Open())
        {
            canvas.DrawRectangle(M.Brushes.Transparent,null,new W.Rect(0,0,24,24));
            var pen=new M.Pen(color,1.6) { StartLineCap=M.PenLineCap.Round,EndLineCap=M.PenLineCap.Round,LineJoin=M.PenLineJoin.Round };
            canvas.DrawGeometry(null,pen,M.Geometry.Parse("M 8,10 V 3 H 16 V 10 M 10.5,5.5 V 7 M 13.5,5.5 V 7 M 12,18 V 22"));
            canvas.DrawRoundedRectangle(null,pen,new W.Rect(6,10,12,8),2,2);
        }
        drawing.Freeze();
        var image=new C.Image { Source=new M.DrawingImage(drawing),Width=size,Height=size,VerticalAlignment=W.VerticalAlignment.Center,ToolTip="USB 有线供电" };
        W.Automation.AutomationProperties.SetName(image,"USB 有线供电");
        return image;
    }
}
