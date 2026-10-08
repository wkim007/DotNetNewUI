using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private readonly Dictionary<string, string> _objectDefinitions = new()
    {
        ["Order"] = "Customer purchase transaction."
    };

    private void ObjectTooltipsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox toggle) return;
        var enabled = toggle.IsChecked == true;
        toggle.Content = "Tun On Tooltip";
        if (!enabled && DiagramCanvasSurface is not null)
            foreach (var card in DiagramCanvasSurface.Children.OfType<Border>())
                if (card.ToolTip is ToolTip tooltip) tooltip.IsOpen = false;
    }
    private void LoadObjectDefinition(string? key)
    {
        ObjectDefinitionBox.Tag = null;
        ObjectDefinitionBox.Text = key is null ? "" : _objectDefinitions.GetValueOrDefault(key, "");
        ObjectDefinitionBox.IsEnabled = key is not null;
        ObjectDefinitionBox.Tag = key;
    }

    private void ObjectDefinition_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { Tag: string key } editor && _columns.ContainsKey(key))
            _objectDefinitions[key] = editor.Text;
    }

    private void ConfigureObjectToolTip(Border card, string key)
    {
        var panel = new StackPanel { MaxWidth = 320 };
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        var kind = new TextBlock { Foreground = ThemeBrush("#A9C9E8"), Margin = new Thickness(0, 3, 0, 10) };
        var definition = new TextBlock { Foreground = ThemeBrush("#DCE7F5"), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(title); panel.Children.Add(kind); panel.Children.Add(definition);
        var tooltip = new ToolTip
        {
            Content = panel, Background = ThemeBrush("#17232F"), BorderBrush = ThemeBrush("#405D7B"),
            BorderThickness = new Thickness(1), Padding = new Thickness(12)
        };
        tooltip.Opened += (_, _) =>
        {
            title.Text = EntityDisplayName(key);
            kind.Text = _materializedViewKeys.Contains(key) ? "Materialized View" : _viewCardKeys.Contains(key) ? "View" : "Entity";
            var text = _objectDefinitions.GetValueOrDefault(key, "");
            definition.Text = string.IsNullOrWhiteSpace(text) ? "No definition or comment provided." : text;
        };
        card.ToolTip = tooltip;
        card.SetBinding(ToolTipService.IsEnabledProperty, new System.Windows.Data.Binding(nameof(CheckBox.IsChecked))
        { Source = ObjectTooltipsToggle, TargetNullValue = false });
        ToolTipService.SetInitialShowDelay(card, 500);
        ToolTipService.SetShowDuration(card, 20000);
    }
}
