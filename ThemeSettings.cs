using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private ObservableCollection<StudioTheme> _themes = [new()];
    private StudioTheme ActiveTheme => _themes[_activeThemeIndex];
    private int _activeThemeIndex;
    private string? _appliedDiagramFill;
    private string? _appliedDefaultFont;
    private static readonly Brush ThemeInk = new SolidColorBrush(Color.FromRgb(227, 235, 246));
    private static readonly Brush ThemeMuted = new SolidColorBrush(Color.FromRgb(166, 188, 211));
    private static Brush ThemeBrush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private void ThemeSettings_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = new ObservableCollection<StudioTheme>(_themes.Select(t => t with { }));
        var originalIndex = _activeThemeIndex;
        var accepted = false;
        var dialog = new Window
        {
            Title = "Theme Settings", Owner = this, Width = 650, Height = Math.Min(840, SystemParameters.WorkArea.Height - 40),
            MinWidth = 560, MinHeight = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ThemeBrush("#17232F"), Foreground = ThemeInk, FontFamily = new FontFamily("Segoe UI"),
            UseLayoutRounding = true
        };
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "Theme Settings", FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        Grid.SetRow(columns, 1); root.Children.Add(columns);
        Border Section(UIElement child) => new() { Background = ThemeBrush("#13212E"), BorderBrush = ThemeBrush("#293D50"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Child = child };
        Button ActionButton(string text) => new() { Content = text, Style = (Style)FindResource("ProjectSettingsButton"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 10, 12, 10) };
        var left = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "THEMES", Foreground = ThemeMuted, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 22) });
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        var add = ActionButton("Add Theme"); var delete = ActionButton("Delete");
        actions.Children.Add(add); actions.Children.Add(delete); top.Children.Add(actions);
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top);
        var list = new ListBox { ItemsSource = _themes, DisplayMemberPath = "Name", Background = ThemeBrush("#0B151F"), Foreground = ThemeInk, BorderBrush = ThemeBrush("#293D50") };
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, ThemeInk));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, ThemeBrush("#13212E")));
        var template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'><Border x:Name='bd' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' BorderBrush='Transparent' BorderThickness='1' CornerRadius='10' Padding='{TemplateBinding Padding}' Margin='2'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#FFD26B'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#29445B'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, template)); list.ItemContainerStyle = itemStyle;
        left.Children.Add(list); columns.Children.Add(Section(left));
        var fields = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var scroll = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var right = Section(scroll); Grid.SetColumn(right, 2); columns.Children.Add(right);
        void Label(string text) => fields.Children.Add(new TextBlock { Text = text, Foreground = ThemeMuted, Margin = new Thickness(0, 10, 0, 8) });
        TextBox Input(string text) => new() { Text = text, Background = ThemeBrush("#0E1824"), Foreground = ThemeInk, CaretBrush = ThemeInk, BorderBrush = ThemeBrush("#293D50"), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 14) };
        void RefreshEditor()
        {
            fields.Children.Clear();
            var theme = ActiveTheme;
            delete.IsEnabled = !theme.IsDefault;
            fields.Children.Add(new TextBlock { Text = "THEME", Foreground = ThemeMuted, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 14) });
            Label("Name"); var name = Input(theme.Name); fields.Children.Add(name);
            name.LostKeyboardFocus += (_, _) =>
            {
                var value = name.Text.Trim();
                if (value.Length == 0 || _themes.Any(t => t != theme && t.Name.Equals(value, StringComparison.OrdinalIgnoreCase))) { name.Text = theme.Name; return; }
                theme.Name = value; list.Items.Refresh();
            };
            void FontOption(string label, string value, Action<string> setter)
            {
                Label(label);
                var choices = new[] { "Segoe UI", "Outfit", "Source Serif 4", "Inter", "Georgia", "Arial", "Courier New" };
                var combo = new ComboBox { ItemsSource = choices, SelectedItem = value, Foreground = Brushes.Black, Background = Brushes.White, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 14), ToolTip = "Fonts unavailable on this computer use Segoe UI as a fallback." };
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string font) { setter(font); ApplyTheme(); } };
                fields.Children.Add(combo);
            }
            void ColorOption(string label, string value, Action<string> setter)
            {
                Label(label);
                var swatch = new Border { Background = ThemeBrush(value), Height = 24, BorderBrush = ThemeMuted, BorderThickness = new Thickness(1) };
                var button = ActionButton(""); button.Content = swatch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                // A fixed swatch width keeps the color visible in the shared centered button template.
                swatch.Width = 175; button.Margin = new Thickness(0, 0, 0, 14); button.ToolTip = value;
                button.Click += (_, _) =>
                {
                    var color = ChooseThemeColor(dialog, label, (string)button.ToolTip);
                    if (color is null) return;
                    setter(color); swatch.Background = ThemeBrush(color); button.ToolTip = color; ApplyTheme();
                };
                fields.Children.Add(button);
            }
            FontOption("Default Font", theme.DefaultFont, v => theme.DefaultFont = v);
            ColorOption("Diagram Fill", theme.DiagramFill, v => theme.DiagramFill = v);
            FontOption("Entity Font", theme.EntityFont, v => theme.EntityFont = v);
            ColorOption("Entity Fill", theme.EntityFill, v => theme.EntityFill = v);
            FontOption("Attribute Font", theme.AttributeFont, v => theme.AttributeFont = v);
            FontOption("Relationship Text Font", theme.RelationshipFont, v => theme.RelationshipFont = v);
            ColorOption("Relationship Line Color", theme.LineColor, v => theme.LineColor = v);
            Label("Relationship Line Width"); var width = Input(theme.LineWidth.ToString(CultureInfo.InvariantCulture)); fields.Children.Add(width);
            width.ToolTip = "Enter a width from 0.5 to 12";
            width.TextChanged += (_, _) =>
            {
                if (double.TryParse(width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0.5 && value <= 12)
                { theme.LineWidth = value; width.BorderBrush = ThemeBrush("#293D50"); ApplyTheme(); }
                else width.BorderBrush = Brushes.Salmon;
            };
            width.LostKeyboardFocus += (_, _) => width.Text = theme.LineWidth.ToString(CultureInfo.InvariantCulture);
            ColorOption("FK Column Color", theme.FkColor, v => theme.FkColor = v);
            ColorOption("PK Column Color", theme.PkColor, v => theme.PkColor = v);
        }
        list.SelectionChanged += (_, _) => { if (list.SelectedIndex < 0) return; _activeThemeIndex = list.SelectedIndex; RefreshEditor(); ApplyTheme(); };
        add.Click += (_, _) =>
        {
            var n = 1; while (_themes.Any(t => t.Name == $"Theme {n}")) n++;
            _themes.Add(ActiveTheme with { Name = $"Theme {n}", IsDefault = false }); list.SelectedIndex = _themes.Count - 1;
        };
        delete.Click += (_, _) =>
        {
            if (ActiveTheme.IsDefault) return;
            var index = _activeThemeIndex; list.SelectedIndex = 0; _themes.RemoveAt(index);
        };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var reset = ActionButton("Reset"); var close = ActionButton("Close"); var cancel = ActionButton("Cancel");
        reset.Click += (_, _) =>
        {
            var index = _activeThemeIndex; var current = ActiveTheme;
            _themes[index] = new StudioTheme { Name = current.Name, IsDefault = current.IsDefault };
            list.SelectedIndex = index; RefreshEditor(); ApplyTheme();
        };
        close.Click += (_, _) => { accepted = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        footer.Children.Add(reset); footer.Children.Add(close); footer.Children.Add(cancel); Grid.SetRow(footer, 2); root.Children.Add(footer);
        dialog.Content = root; list.SelectedIndex = _activeThemeIndex;
        dialog.ShowDialog();
        if (!accepted) { _themes = snapshot; _activeThemeIndex = originalIndex; ApplyTheme(); }
    }

    private void ApplyTheme()
    {
        if (DiagramCanvasSurface is null) return;
        SyncNotationEndpoints();
        var theme = ActiveTheme;
        void Font(Control control, string family) { var value = family + ", Segoe UI"; if (control.FontFamily.Source != value) control.FontFamily = new FontFamily(value); }
        void TextFont(TextBlock text, string family) { var value = family + ", Segoe UI"; if (text.FontFamily.Source != value) text.FontFamily = new FontFamily(value); }
        void Color(DependencyObject element, DependencyProperty property, string value)
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            if (element.GetValue(property) is not SolidColorBrush brush || brush.Color != color) element.SetValue(property, new SolidColorBrush(color));
        }
        Font(this, theme.DefaultFont);
        if (_appliedDefaultFont != theme.DefaultFont)
        {
            var textStyle = new Style(typeof(TextBlock), Application.Current.TryFindResource(typeof(TextBlock)) as Style);
            textStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily(theme.DefaultFont + ", Segoe UI")));
            Resources[typeof(TextBlock)] = textStyle;
            _appliedDefaultFont = theme.DefaultFont;
        }
        if (_appliedDiagramFill != theme.DiagramFill)
        {
            var grid = ((DrawingBrush)FindResource("DiagramGridBrush")).Clone();
            if (grid.Drawing is GeometryDrawing drawing) drawing.Brush = ThemeBrush(theme.DiagramFill);
            DiagramCanvasSurface.Background = grid; DiagramScrollViewer.Background = grid; _appliedDiagramFill = theme.DiagramFill;
        }
        foreach (var card in DiagramCanvasSurface.Children.OfType<Border>())
        {
            if (card.Tag is not string key || (!_entityCardKeys.Contains(key) && !_viewCardKeys.Contains(key))) continue;
            Color(card, Border.BackgroundProperty, theme.EntityFill);
            if (card.Child is not Grid content) continue;
            foreach (var header in content.Children.OfType<Border>().Where(b => Grid.GetRow(b) == 0))
                foreach (var text in Descendants<TextBlock>(header)) TextFont(text, theme.EntityFont);
            foreach (var fields in content.Children.OfType<StackPanel>())
            {
                foreach (var text in Descendants<TextBlock>(fields)) TextFont(text, theme.AttributeFont);
                foreach (var editor in Descendants<TextBox>(fields)) Font(editor, theme.AttributeFont);
                foreach (var badge in Descendants<Border>(fields))
                    if (badge.Child is TextBlock label && badge.Width == 36)
                    {
                        if (label.Text == "PK") Color(label, TextBlock.ForegroundProperty, theme.PkColor);
                        else if (label.Text == "FK") Color(label, TextBlock.ForegroundProperty, theme.FkColor);
                    }
            }
        }
        foreach (var label in _relationshipLabels.Values) TextFont(label, theme.RelationshipFont);
        foreach (var key in new[] { "CustomerOrder", "OrderProduct", "OrderOrderItem", "ProductOrderItem" }.Concat(_dynamicRelationships.Keys))
        {
            if (FindRelationshipLine(key) is not { } line) continue;
            Color(line, System.Windows.Shapes.Shape.StrokeProperty, line == _selectedRelationship ? "#F1DF77" : theme.LineColor);
            var width = line == _selectedRelationship ? Math.Max(4, theme.LineWidth + 1.5) : theme.LineWidth;
            if (line.StrokeThickness != width) line.StrokeThickness = width;
        }
    }
}

public sealed record StudioTheme
{
    public string Name { get; set; } = "Default Theme";
    public bool IsDefault { get; set; } = true;
    public string DefaultFont { get; set; } = "Segoe UI";
    public string EntityFont { get; set; } = "Segoe UI";
    public string AttributeFont { get; set; } = "Segoe UI";
    public string RelationshipFont { get; set; } = "Segoe UI";
    public string DiagramFill { get; set; } = "#0D1520";
    public string EntityFill { get; set; } = "#202B3A";
    public string LineColor { get; set; } = "#42D9D4";
    public double LineWidth { get; set; } = 2.5;
    public string FkColor { get; set; } = "#8FC1FF";
    public string PkColor { get; set; } = "#FFD26B";
}
