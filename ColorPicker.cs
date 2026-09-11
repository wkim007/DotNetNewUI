using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private string? ChooseThemeColor(Window owner, string title, string initial)
    {
        var current = (Color)ColorConverter.ConvertFromString(initial);
        var chosen = current;
        var (hueValue, saturation, brightness) = ToHsv(current);
        var updating = false;
        string? result = null;
        var picker = new Window
        {
            Title = title, Owner = owner, Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ThemeBrush("#17232F"), Foreground = ThemeInk, UseLayoutRounding = true,
            FontFamily = new FontFamily("Segoe UI"), MaxHeight = SystemParameters.WorkArea.Height - 30
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        picker.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Choose a color", Foreground = ThemeInk, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        var spectrum = new Grid { Height = 190, ClipToBounds = true, Focusable = true, Cursor = Cursors.Cross };
        System.Windows.Automation.AutomationProperties.SetName(spectrum, "Color spectrum. Use left/right for saturation and up/down for brightness.");
        var hueSurface = new Border(); spectrum.Children.Add(hueSurface);
        spectrum.Children.Add(new Border { Background = new LinearGradientBrush(Colors.White, Colors.Transparent, 0) });
        spectrum.Children.Add(new Border { Background = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90) });
        var overlay = new Canvas { IsHitTestVisible = false };
        var ring = new Ellipse { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent };
        var outline = new Ellipse { Width = 16, Height = 16, Stroke = Brushes.Black, StrokeThickness = 1, Fill = Brushes.Transparent };
        overlay.Children.Add(outline); overlay.Children.Add(ring); spectrum.Children.Add(overlay); panel.Children.Add(spectrum);
        panel.Children.Add(new TextBlock { Text = "Hue", Foreground = ThemeMuted, Margin = new Thickness(0, 12, 0, 5) });
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        foreach (var stop in new[] { (Colors.Red, 0d), (Colors.Yellow, 1d/6), (Colors.Lime, 2d/6), (Colors.Cyan, .5), (Colors.Blue, 4d/6), (Colors.Magenta, 5d/6), (Colors.Red, 1d) }) rainbow.GradientStops.Add(new GradientStop(stop.Item1, stop.Item2));
        var hue = new Slider { Minimum = 0, Maximum = 360, Value = hueValue, SmallChange = 1, LargeChange = 15, Height = 26, Background = rainbow };
        // Keep the full rainbow visible behind the native keyboard-accessible slider thumb.
        var hueHost = new Grid { Background = rainbow, Margin = new Thickness(0, 0, 0, 14) }; hueHost.Children.Add(hue); panel.Children.Add(hueHost);
        System.Windows.Automation.AutomationProperties.SetName(hue, "Hue");

        var previewGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition()); previewGrid.ColumnDefinitions.Add(new ColumnDefinition());
        Border Preview(string label, Color color, int column)
        {
            var box = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 0) };
            box.Children.Add(new TextBlock { Text = label, Foreground = ThemeMuted, Margin = new Thickness(0, 0, 0, 5) });
            var swatch = new Border { Height = 38, Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(4), BorderBrush = ThemeMuted, BorderThickness = new Thickness(1) };
            box.Children.Add(swatch); Grid.SetColumn(box, column); previewGrid.Children.Add(box); return swatch;
        }
        Preview("Current", current, 0); var newPreview = Preview("New", chosen, 1); panel.Children.Add(previewGrid);
        panel.Children.Add(new TextBlock { Text = "Preset colors", Foreground = ThemeMuted, Margin = new Thickness(0, 0, 0, 6) });
        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(presets);
        var inputGrid = new Grid();
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
        TextBox Field(string label, int column)
        {
            var box = new StackPanel { Margin = new Thickness(0, 0, column == 3 ? 0 : 8, 0) };
            box.Children.Add(new TextBlock { Text = label, Foreground = ThemeMuted, Margin = new Thickness(0, 0, 0, 6) });
            var input = new TextBox { Background = ThemeBrush("#0E1824"), Foreground = ThemeInk, CaretBrush = ThemeInk, BorderBrush = ThemeMuted, Padding = new Thickness(6, 8, 6, 8) };
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            box.Children.Add(input); Grid.SetColumn(box, column); inputGrid.Children.Add(box); return input;
        }
        var hex = Field("Hex", 0); var red = Field("Red", 1); var green = Field("Green", 2); var blue = Field("Blue", 3); panel.Children.Add(inputGrid);
        var error = new TextBlock { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) }; panel.Children.Add(error);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("ProjectSettingsButton"), Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "Apply Color", IsDefault = true, Style = (Style)FindResource("ProjectSettingsButton") };
        footer.Children.Add(cancel); footer.Children.Add(apply); panel.Children.Add(footer);
        void PositionRing()
        {
            var x = saturation * spectrum.ActualWidth; var y = (1 - brightness) * spectrum.ActualHeight;
            Canvas.SetLeft(ring, x - 7); Canvas.SetTop(ring, y - 7); Canvas.SetLeft(outline, x - 8); Canvas.SetTop(outline, y - 8);
        }
        void Render(bool updateFields = true)
        {
            updating = true;
            chosen = FromHsv(hueValue, saturation, brightness);
            hueSurface.Background = new SolidColorBrush(FromHsv(hueValue, 1, 1));
            hue.Value = hueValue; newPreview.Background = new SolidColorBrush(chosen); PositionRing();
            if (updateFields)
            {
                hex.Text = $"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}";
                red.Text = chosen.R.ToString(); green.Text = chosen.G.ToString(); blue.Text = chosen.B.ToString();
            }
            error.Text = ""; apply.IsEnabled = true; updating = false;
        }
        void Pick(Color color)
        {
            var hsv = ToHsv(color);
            if (hsv.Saturation > 0) hueValue = hsv.Hue;
            saturation = hsv.Saturation; brightness = hsv.Value; Render();
        }
        void PickPoint(MouseEventArgs args)
        {
            var point = args.GetPosition(spectrum);
            saturation = Math.Clamp(point.X / Math.Max(1, spectrum.ActualWidth), 0, 1);
            brightness = 1 - Math.Clamp(point.Y / Math.Max(1, spectrum.ActualHeight), 0, 1); Render();
        }
        spectrum.MouseLeftButtonDown += (_, args) => { spectrum.Focus(); spectrum.CaptureMouse(); PickPoint(args); args.Handled = true; };
        spectrum.MouseMove += (_, args) => { if (spectrum.IsMouseCaptured && args.LeftButton == MouseButtonState.Pressed) PickPoint(args); };
        spectrum.MouseLeftButtonUp += (_, _) => spectrum.ReleaseMouseCapture();
        spectrum.KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case Key.Left: saturation = Math.Max(0, saturation - .01); break;
                case Key.Right: saturation = Math.Min(1, saturation + .01); break;
                case Key.Up: brightness = Math.Min(1, brightness + .01); break;
                case Key.Down: brightness = Math.Max(0, brightness - .01); break;
                default: return;
            }
            Render(); args.Handled = true;
        };
        spectrum.SizeChanged += (_, _) => PositionRing();
        hue.ValueChanged += (_, _) => { if (!updating) { hueValue = hue.Value; Render(); } };
        foreach (var value in new[] { "#FFFFFF", "#808080", "#000000", "#E74856", "#FF8C00", "#FFD26B", "#16C60C", "#42D9D4", "#0078D4", "#8FC1FF", "#886CE4", "#E3008C" })
        {
            var button = new Button { Width = 27, Height = 27, Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 5), ToolTip = value, Content = new Border { Width = 23, Height = 23, Background = ThemeBrush(value), BorderBrush = ThemeMuted, BorderThickness = new Thickness(1) } };
            System.Windows.Automation.AutomationProperties.SetName(button, "Preset " + value);
            button.Click += (_, _) => Pick((Color)ColorConverter.ConvertFromString(value)); presets.Children.Add(button);
        }
        hex.TextChanged += (_, _) =>
        {
            if (updating) return;
            if (System.Text.RegularExpressions.Regex.IsMatch(hex.Text, "^#[0-9a-fA-F]{6}$")) Pick((Color)ColorConverter.ConvertFromString(hex.Text));
            else { error.Text = "Enter a hex color such as #0078D4."; apply.IsEnabled = false; }
        };
        void RgbChanged(object sender, TextChangedEventArgs args)
        {
            if (updating) return;
            if (byte.TryParse(red.Text, out var r) && byte.TryParse(green.Text, out var g) && byte.TryParse(blue.Text, out var b)) Pick(Color.FromRgb(r, g, b));
            else { error.Text = "Red, green, and blue must be between 0 and 255."; apply.IsEnabled = false; }
        }
        red.TextChanged += RgbChanged; green.TextChanged += RgbChanged; blue.TextChanged += RgbChanged;
        apply.Click += (_, _) => { result = $"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}"; picker.Close(); };
        cancel.Click += (_, _) => picker.Close();
        Render(); picker.ShowDialog(); return result;
    }

    private static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return ((hue + 360) % 360, max == 0 ? 0 : delta / max, max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation; var h = (hue % 360) / 60; var x = c * (1 - Math.Abs(h % 2 - 1)); var m = value - c;
        var (r, g, b) = h switch { < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x), < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
