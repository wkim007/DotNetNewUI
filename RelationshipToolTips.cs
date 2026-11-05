using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Text.Json.Nodes;
using Path = System.Windows.Shapes.Path;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private sealed class RelationshipText
    {
        public string Name { get; set; } = "";
        public string Comment { get; set; } = "";

        public static RelationshipText Read(JsonObject relationship) => new()
        {
            Name = ModelJsonDocument.Text(relationship, "name"),
            Comment = new[] { "description", "definition", "comment" }
                .Select(field => ModelJsonDocument.Text(relationship, field))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? ""
        };
    }

    private void ConfigureRelationshipToolTip(Path hit, string key, RelationshipText data)
    {
        hit.Resources[typeof(RelationshipText)] = data;
        var body = new StackPanel { MaxWidth = 320 };
        var title = new TextBlock { Foreground = Brushes.DarkBlue, FontSize = 14, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        body.Children.Add(title);
        TextBlock Detail()
        {
            var text = new TextBlock { Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap };
            body.Children.Add(text);
            return text;
        }
        var parent = Detail();
        var child = Detail();
        var comment = Detail();
        var tip = new ToolTip { Content = body, Background = Brushes.White, BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 8), HasDropShadow = true };
        tip.Opened += (_, _) =>
        {
            if (GetRelationshipInfo(key) is not { } info) return;
            var from = EntityDisplayName(info.Source);
            var to = EntityDisplayName(info.Target);
            var route = $"{from} → {to}";
            title.Text = string.IsNullOrWhiteSpace(data.Name) || data.Name == route ? route : $"{data.Name} ({from} to {to})";
            parent.Text = $"Parent: {from}";
            child.Text = $"Child: {to}";
            comment.Text = $"Comment: {data.Comment}";
            comment.Visibility = string.IsNullOrWhiteSpace(data.Comment) ? Visibility.Collapsed : Visibility.Visible;
        };
        hit.ToolTip = tip;
        hit.SetBinding(ToolTipService.IsEnabledProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = ObjectTooltipsToggle });
        ToolTipService.SetInitialShowDelay(hit, 500);
        ToolTipService.SetShowDuration(hit, 20000);
    }
}
