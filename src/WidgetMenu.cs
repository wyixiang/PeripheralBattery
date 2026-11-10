using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;
namespace PeripheralBattery;

// ContextMenu supplies Windows/WPF popup capture and dismissal, including when
// the taskbar card intentionally uses WS_EX_NOACTIVATE.
internal static class WidgetMenu
{
    internal static C.ContextMenu Create(WidgetContext context)
    {
        var p=new Palette(context.IsDark);
        var menu=new C.ContextMenu { Width=300,MaxHeight=Math.Min(720,Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea.Height-16),StaysOpen=false,FontFamily=new M.FontFamily("Microsoft YaHei UI"),FontSize=13,Foreground=p.Text,Background=Palette.Brush(context.IsDark?"#FA262A30":"#FAF8FAFC"),BorderBrush=p.Stroke,BorderThickness=new W.Thickness(1),Padding=new W.Thickness(6) };
        var border=new W.FrameworkElementFactory(typeof(C.Border));
        border.SetValue(C.Border.CornerRadiusProperty,new W.CornerRadius(9));
        border.SetValue(C.Border.PaddingProperty,new W.Thickness(6));
        border.SetValue(C.Border.BorderThicknessProperty,new W.Thickness(1));
        border.SetValue(C.Border.BorderBrushProperty,p.Stroke);
        border.SetValue(C.Border.BackgroundProperty,menu.Background);
        var scroll=new W.FrameworkElementFactory(typeof(C.ScrollViewer));
        scroll.SetValue(C.ScrollViewer.VerticalScrollBarVisibilityProperty,C.ScrollBarVisibility.Auto);
        scroll.SetValue(C.ScrollViewer.HorizontalScrollBarVisibilityProperty,C.ScrollBarVisibility.Disabled);
        var items=new W.FrameworkElementFactory(typeof(C.ItemsPresenter));
        scroll.AppendChild(items); border.AppendChild(scroll);
        menu.Template=new C.ControlTemplate(typeof(C.ContextMenu)) { VisualTree=border };
        var itemBorder=new W.FrameworkElementFactory(typeof(C.Border),"ItemBorder");
        itemBorder.SetValue(C.Border.CornerRadiusProperty,new W.CornerRadius(5));
        itemBorder.SetValue(C.Border.PaddingProperty,new W.Thickness(10,0,10,0));
        itemBorder.SetValue(C.Border.BackgroundProperty,M.Brushes.Transparent);
        var content=new W.FrameworkElementFactory(typeof(C.ContentPresenter));
        content.SetValue(C.ContentPresenter.ContentSourceProperty,"Header");
        content.SetValue(W.FrameworkElement.VerticalAlignmentProperty,W.VerticalAlignment.Center);
        itemBorder.AppendChild(content);
        var itemTemplate=new C.ControlTemplate(typeof(C.MenuItem)) { VisualTree=itemBorder };
        var hover=new W.Trigger { Property=C.MenuItem.IsHighlightedProperty,Value=true };
        hover.Setters.Add(new W.Setter(C.Border.BackgroundProperty,p.Hover,"ItemBorder")); itemTemplate.Triggers.Add(hover);
        void Caption(string label)
        {
            var text=new C.TextBlock { Text=label,FontSize=11,Foreground=p.Muted,Margin=new W.Thickness(10,7,0,4) };
            menu.Items.Add(new C.MenuItem { Header=text,IsEnabled=false,Height=29,Template=itemTemplate });
        }
        void Item(string label,Action action,string icon="",bool selected=false,int height=36)
        {
            var grid=new C.Grid();
            grid.ColumnDefinitions.Add(new C.ColumnDefinition { Width=new W.GridLength(28) });
            grid.ColumnDefinitions.Add(new C.ColumnDefinition());
            grid.ColumnDefinitions.Add(new C.ColumnDefinition { Width=new W.GridLength(20) });
            grid.Children.Add(new C.TextBlock { Text=icon,FontFamily=new M.FontFamily("Segoe Fluent Icons"),Foreground=p.Muted,FontSize=15,VerticalAlignment=W.VerticalAlignment.Center });
            var text=new C.TextBlock { Text=label,Foreground=p.Text,FontSize=height>36?11:13,VerticalAlignment=W.VerticalAlignment.Center };
            C.Grid.SetColumn(text,1); grid.Children.Add(text);
            if(selected) { var check=new C.TextBlock { Text="✓",Foreground=p.Accent,VerticalAlignment=W.VerticalAlignment.Center }; C.Grid.SetColumn(check,2); grid.Children.Add(check); }
            var item=new C.MenuItem { Header=grid,Height=height,Template=itemTemplate };
            W.Automation.AutomationProperties.SetName(item,label.Replace("\n"," "));
            item.Click+=(_,_)=> { menu.IsOpen=false; action(); };
            menu.Items.Add(item);
        }
        void Line()
        {
            var line=new W.FrameworkElementFactory(typeof(C.Border));
            line.SetValue(W.FrameworkElement.HeightProperty,1.0); line.SetValue(W.FrameworkElement.MarginProperty,new W.Thickness(10,5,10,5)); line.SetValue(C.Border.BackgroundProperty,p.Stroke);
            menu.Items.Add(new C.Separator { Template=new C.ControlTemplate(typeof(C.Separator)) { VisualTree=line } });
        }
        Caption("外设电量");
        Item("刷新电量",()=>_=context.Refresh(),"\uE72C");
        Item("查看详情",context.ShowDetails,"\uE946");
        Item("打开 Gear Link",WidgetContext.OpenGearLink,"\uE774");
        Line();
        Item("固定位置",()=>context.Change(s=>s.Locked=!s.Locked),"\uE718",context.Settings.Locked);
        Item("显示任务栏卡片",()=>context.Change(s=>s.Visible=!s.Visible),"\uE7F4",context.Settings.Visible);
        Item("恢复默认位置",()=>context.Change(s=>s.Offset=0),"\uE777");
        Caption("颜色");
        foreach(var (value,label) in new[]{("system","跟随系统"),("light","浅色"),("dark","深色")}) Item(label,()=>context.Change(s=>s.Theme=value),"\uE790",context.Settings.Theme==value);
        Caption("显示区域");
        foreach(var display in DisplayCatalog.Read()) Item(display.Label,()=>context.Change(s=> { s.MonitorIdentity=display.Identity; s.MonitorDevice=display.DeviceName; s.Offset=0; }),"\uE7F4",context.Dock.MonitorDevice==display.DeviceName,48);
        Line();
        Item("登录后自动启动",context.ToggleAutostart,"\uE7E8",context.Autostart);
        Item("退出",()=>context.Quit(),"\uE8BB");
        return menu;
    }
}
