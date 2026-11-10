using System.Runtime.InteropServices;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using H = System.Windows.Interop;
using S = System.Windows.Shell;

namespace PeripheralBattery;

internal sealed class Palette(bool dark)
{
    internal M.SolidColorBrush Text => Brush(dark ? "#F4F6F8" : "#18242E");
    internal M.SolidColorBrush Muted => Brush(dark ? "#BAC4CC" : "#53616C");
    internal M.SolidColorBrush Hover => Brush(dark ? "#16FFFFFF" : "#0D000000");
    internal M.SolidColorBrush Stroke => Brush(dark ? "#29FFFFFF" : "#18000000");
    internal M.SolidColorBrush Card => Brush(dark ? "#20FFFFFF" : "#B3FFFFFF");
    internal M.SolidColorBrush Accent => Brush(dark ? "#72DEC2" : "#00735C");
    internal static M.SolidColorBrush Brush(string color) => new((M.Color)M.ColorConverter.ConvertFromString(color));
}

internal class GlassWindow : W.Window
{
    private bool _dark;
    internal bool IsDisposed { get; private set; }
    internal bool IsClosing { get; private set; }
    internal bool CanUse => !IsDisposed && !IsClosing && UiDispatch.Available(Dispatcher);
    internal bool AcrylicApplied { get; private set; }
    internal readonly Palette Colors;
    internal readonly C.Border Surface;
    internal GlassWindow(bool dark)
    {
        _dark = dark;
        Colors = new(dark);
        WindowStyle = W.WindowStyle.None;
        ResizeMode = W.ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = M.Brushes.Transparent;
        FontFamily = new M.FontFamily("Microsoft YaHei UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FontSize = 14;
        Foreground = Colors.Text;
        S.WindowChrome.SetWindowChrome(this, new S.WindowChrome
        {
            CaptionHeight = 0, ResizeBorderThickness = new W.Thickness(0),
            GlassFrameThickness = new W.Thickness(-1), CornerRadius = new W.CornerRadius(10),
            UseAeroCaptionButtons = false
        });
        Surface = new C.Border
        {
            CornerRadius = new W.CornerRadius(10), BorderThickness = new W.Thickness(1),
            BorderBrush = Colors.Stroke, SnapsToDevicePixels = true,
            Background = Palette.Brush(dark ? "#D925292E" : "#D9FAFAFC")
        };
        Content = Surface;
        M.TextOptions.SetTextFormattingMode(this, M.TextFormattingMode.Display);
        M.TextOptions.SetTextRenderingMode(this, M.TextRenderingMode.Grayscale);
        SourceInitialized += (_, _) => ApplyBackdrop();
        PreviewKeyDown += (_, e) => { if (this is not DockWindow && e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } };
        Closed += (_, _) => IsDisposed = true;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico"));
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e); IsClosing = !e.Cancel;
    }
    internal void UpdateBackdrop(bool dark)
    {
        if (_dark == dark) return;
        _dark = dark; if (W.PresentationSource.FromVisual(this) is not null) ApplyBackdrop();
    }
    private void ApplyBackdrop()
    {
        var handle = new H.WindowInteropHelper(this).Handle;
        var source = H.HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is { } target) target.BackgroundColor = M.Colors.Transparent;
        try
        {
            int dark = _dark ? 1 : 0, corners = 2, acrylic = 3;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(handle, ref margins);
            AcrylicApplied = DwmSetWindowAttribute(handle, 38, ref acrylic, sizeof(int)) == 0;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        if (!AcrylicApplied) Surface.Background = Palette.Brush(_dark ? "#25292E" : "#FAFAFC");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { internal int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

    internal static C.Button Button(string label, Palette palette, Action action, string? symbol = null, bool selected = false)
    {
        var button = new C.Button
        {
            Height = 36, Margin = new W.Thickness(0, 1, 0, 1),
            Background = M.Brushes.Transparent, BorderThickness = new W.Thickness(0), Foreground = palette.Text,
            HorizontalContentAlignment = W.HorizontalAlignment.Stretch, FocusVisualStyle = null,
            Padding = new W.Thickness(10, 0, 10, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var border = new W.FrameworkElementFactory(typeof(C.Border));
        border.SetValue(C.Border.CornerRadiusProperty, new W.CornerRadius(5));
        border.SetBinding(C.Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new W.FrameworkElementFactory(typeof(C.ContentPresenter));
        presenter.SetBinding(W.FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        presenter.SetValue(W.FrameworkElement.VerticalAlignmentProperty, W.VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new C.ControlTemplate(typeof(C.Button)) { VisualTree = border };
        var hover = new W.Trigger { Property = W.UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new W.Setter(C.Control.BackgroundProperty, palette.Hover));
        template.Triggers.Add(hover);
        var focus = new W.Trigger { Property = W.UIElement.IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new W.Setter(C.Control.BackgroundProperty, palette.Hover));
        template.Triggers.Add(focus);
        button.Template = template;
        var grid = new C.Grid();
        grid.ColumnDefinitions.Add(new C.ColumnDefinition { Width = new W.GridLength(28) });
        grid.ColumnDefinitions.Add(new C.ColumnDefinition { Width = new W.GridLength(1, W.GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new C.ColumnDefinition { Width = new W.GridLength(selected ? 20 : 0) });
        grid.Children.Add(new C.TextBlock { Text = symbol ?? "", FontFamily = new M.FontFamily("Segoe Fluent Icons"), FontSize = 15, VerticalAlignment = W.VerticalAlignment.Center, Foreground = palette.Muted });
        var text = new C.TextBlock { Text = label, FontSize = 14, VerticalAlignment = W.VerticalAlignment.Center };
        C.Grid.SetColumn(text, 1); grid.Children.Add(text);
        if (selected)
        {
            var check = new C.TextBlock { Text = "✓", Foreground = palette.Accent, FontSize = 15, VerticalAlignment = W.VerticalAlignment.Center };
            C.Grid.SetColumn(check, 2); grid.Children.Add(check);
        }
        button.Content = grid;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }
}
