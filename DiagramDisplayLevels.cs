using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private sealed record DisplayLevelOption(string Name, int Value);
    private bool _updatingDisplayLevel;
    private string _displayOptionsSignature = "";
    private readonly ConditionalWeakTable<Border, DisplayCardState> _displayCards = new();
    private sealed class DisplayCardState
    {
        public int LastLevel = -999;
        public double Height, Width;
        public CornerRadius Corner;
        public TextBlock? Detail;
    }
    private bool IsNeo4j => ProjectDatabaseBox.Text.Trim().Equals("Neo4j", StringComparison.OrdinalIgnoreCase);
    private int DiagramDisplayLevel => ViewModeBox.SelectedIndex == 1 ? _activeDiagram?.DisplayLevelLogical ?? 1 : _activeDiagram?.DisplayLevelPhysical ?? 1;
    private static int SupportedDisplayLevel(int level) => new[] { -1, 0, 1, 3, 5, 6, 7, 12, 13 }.Contains(level) ? level : 1;

    private void UpdateDisplayLevelOptions()
    {
        if (DiagramDisplayLevelBox is null || ViewModeBox is null || ProjectDatabaseBox is null) return;
        var logical = ViewModeBox.SelectedIndex == 1;
        var signature = $"{logical}:{IsNeo4j}:{_activeDiagram?.Id}:{DiagramDisplayLevel}";
        if (_displayOptionsSignature == signature) return;
        _displayOptionsSignature = signature;
        _updatingDisplayLevel = true;
        try
        {
            var options = new List<DisplayLevelOption> {
                new(logical ? "Entity" : "Table", 0), new(logical ? "Attribute" : "Column", 1), new("Key", 12) };
            if (IsNeo4j) options.Add(new("Graph", 13));
            options.Add(new(logical ? "Definition" : "Comment", 3));
            options.Add(new("PrimaryKey", 5));
            if (!logical) options.Add(new("Order", 6));
            options.Add(new("Icon", 7));
            DiagramDisplayLevelBox.ItemsSource = options;
            var selected = DiagramDisplayLevel;
            if (selected == -1 || selected == 13 && !IsNeo4j || logical && selected == 6) selected = 1;
            DiagramDisplayLevelBox.SelectedItem = options.FirstOrDefault(o => o.Value == selected) ?? options[1];
        }
        finally { _updatingDisplayLevel = false; }
    }

    private void DiagramDisplayLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingDisplayLevel || _activeDiagram is null || DiagramDisplayLevelBox.SelectedItem is not DisplayLevelOption option) return;
        if (ViewModeBox.SelectedIndex == 1) _activeDiagram.DisplayLevelLogical = option.Value;
        else _activeDiagram.DisplayLevelPhysical = option.Value;
        ApplyProjectViewMode();
        UpdateRelationshipLines();
    }

    private void ApplyDiagramDisplayLevels()
    {
        UpdateDisplayLevelOptions();
        if (_activeDiagram is null) return;
        var resized = false;
        foreach (var card in DiagramCanvasSurface.Children.OfType<Border>())
        {
            if (card.Tag is not string key || !_columns.TryGetValue(key, out var columns) || card.Child is not Grid grid || grid.RowDefinitions.Count < 2) continue;
            var fields = grid.Children.OfType<StackPanel>().FirstOrDefault(p => Grid.GetRow(p) == 1);
            if (fields is null) continue;
            var level = DiagramDisplayLevel;
            if (_activeDiagram.DisplayOverrides.TryGetValue(key, out var overrides))
            {
                var value = ViewModeBox.SelectedIndex == 1 ? overrides.Logical : overrides.Physical;
                if (value != -1) level = value;
            }
            if (level == -1 || level == 6 || level == 13 && !IsNeo4j) level = 1;
            var state = _displayCards.GetOrCreateValue(card);
            if (state.LastLevel == -999)
            {
                state.Height = card.Height; state.Width = card.Width; state.Corner = card.CornerRadius;
            }
            else if (state.LastLevel == 1)
            {
                state.Height = card.Height; state.Width = card.Width;
            }
            var header = grid.Children.OfType<Border>().FirstOrDefault(b => Grid.GetRow(b) == 0);
            if (state.LastLevel != level)
            {
                var width = level == 13 ? 120 : state.Width;
                if (card.Width != width) { card.Width = width; resized = true; }
                card.CornerRadius = level == 13 ? new CornerRadius(60) : state.Corner;
                grid.RowDefinitions[0].Height = level == 13 ? new GridLength(1, GridUnitType.Star) : new GridLength(38);
                if (header is not null) header.CornerRadius = level == 13 ? new CornerRadius(60) : new CornerRadius(6, 6, 0, 0);
                state.LastLevel = level;
            }
            var showFields = level is 1 or 5 or 12;
            fields.Visibility = showFields ? Visibility.Visible : Visibility.Collapsed;
            var visibleRows = 0;
            foreach (var child in fields.Children.OfType<UIElement>())
            {
                var visible = level == 1;
                if (child is Border { Tag: AttributeSelection selection })
                {
                    var column = columns.FirstOrDefault(c => c.Name == selection.AttributeName);
                    visible = level == 1 || column is not null && (level == 5 && column.IsPrimaryKey || level == 12 && (column.IsPrimaryKey || column.IsForeignKey));
                    if (visible) visibleRows++;
                }
                child.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            if (level is 3 or 7)
            {
                if (state.Detail is null)
                {
                    state.Detail = new TextBlock { Foreground = ThemeBrush("#DCE7F5"), Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
                    Grid.SetRow(state.Detail, 1); grid.Children.Add(state.Detail);
                }
                state.Detail.Text = level == 7 ? (_viewCardKeys.Contains(key) ? "▤" : "▦") : _objectDefinitions.GetValueOrDefault(key, "");
                state.Detail.FontSize = level == 7 ? 34 : 13;
                state.Detail.HorizontalAlignment = level == 7 ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
                state.Detail.Visibility = Visibility.Visible;
            }
            else if (state.Detail is not null) state.Detail.Visibility = Visibility.Collapsed;
            var height = level switch { 0 => 40, 3 => 160, 7 => 115, 13 => 120, 5 or 12 => 60 + visibleRows * 29, _ => state.Height };
            if (card.Height != height) { card.Height = height; resized = true; }
        }
        if (resized) UpdateRelationshipLines();
    }
}
