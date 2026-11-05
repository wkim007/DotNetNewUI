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

    private static ToolTip CreateDiagramToolTip(StackPanel body) => new()
    {
        Content = body, Background = Brushes.White, Foreground = Brushes.Black, BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 8), HasDropShadow = true
    };

    private static TextBlock CreateDiagramToolTipTitle() => new()
    {
        Foreground = Brushes.DarkBlue, FontSize = 14, FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
    };

    private void ConfigureObjectToolTip(Border card, string key)
    {
        var panel = new StackPanel { MaxWidth = 320 };
        var title = CreateDiagramToolTipTitle();
        var kind = new TextBlock { Foreground = Brushes.Black };
        var definition = new TextBlock { Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(title); panel.Children.Add(kind); panel.Children.Add(definition);
        var tooltip = CreateDiagramToolTip(panel);
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
