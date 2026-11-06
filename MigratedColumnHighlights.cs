using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private bool _migratedHighlights;
    private bool _migratedHighlightBold = true;
    private string _migratedHighlightColor = "#C00000";
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> _highlightHoverTargets = new();

    private FrameworkElement CreateMigratedColumnOptions(Window dialog)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Migrated Column Options", Foreground = ThemeInk, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
        var enabled = new CheckBox { Content = "Display Diagram Highlights", Foreground = ThemeInk, IsChecked = _migratedHighlights, Margin = new Thickness(0, 0, 0, 16) };
        var bold = new CheckBox { Content = "Bold", Foreground = ThemeInk, IsChecked = _migratedHighlightBold, Margin = new Thickness(0, 16, 0, 0) };
        var swatch = new Border { Width = 175, Height = 24, Background = ThemeBrush(_migratedHighlightColor), BorderBrush = ThemeMuted, BorderThickness = new Thickness(1) };
        var color = new Button { Content = swatch, Width = 253, Height = 46, HorizontalAlignment = HorizontalAlignment.Left,
            Background = ThemeBrush("#344D6A"), Padding = new Thickness(12, 8, 12, 8), ToolTip = _migratedHighlightColor };
        System.Windows.Automation.AutomationProperties.SetName(color, "Migrated Column Color");
        void Apply()
        {
            if (_alternateViewModel?["meta"] is { } meta)
            {
                meta["migratedColumnHighlights"] = _migratedHighlights;
                meta["migratedColumnBold"] = _migratedHighlightBold;
                meta["migratedColumnColor"] = _migratedHighlightColor;
            }
            ApplyMigratedColumnHighlights();
        }
        enabled.Click += (_, _) => { _migratedHighlights = enabled.IsChecked == true; Apply(); };
        bold.Click += (_, _) => { _migratedHighlightBold = bold.IsChecked == true; Apply(); };
        color.Click += (_, _) => {
            var chosen = ChooseThemeColor(dialog, "Migrated Column Color", _migratedHighlightColor);
            if (chosen is null) return;
            _migratedHighlightColor = chosen; swatch.Background = ThemeBrush(chosen); color.ToolTip = chosen; Apply();
        };
        panel.Children.Add(enabled);
        panel.Children.Add(new TextBlock { Text = "Color", Foreground = ThemeInk, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(color); panel.Children.Add(bold);
        return panel;
    }

    private void ApplyMigratedColumnHighlights()
    {
        string? hovered = null;
        foreach (var path in DiagramCanvasSurface.Children.OfType<System.Windows.Shapes.Path>())
        {
            var key = path.Tag as string ?? _notationEndpoints.FirstOrDefault(p => p.Value.Marker == path).Key;
            if (key is null || GetRelationshipInfo(key) is null) continue;
            if (!_highlightHoverTargets.TryGetValue(path, out _))
            {
                _highlightHoverTargets.Add(path, new object());
                path.MouseEnter += (_, _) => ApplyMigratedColumnHighlights();
                path.MouseLeave += (_, _) => ApplyMigratedColumnHighlights();
            }
            if (path.IsMouseOver) hovered = key;
        }
        var activeKey = hovered ?? _selectedRelationshipKey;
        if (activeKey is null && _selectedSubCategoryMarker is { } marker)
            activeKey = _notationEndpoints.FirstOrDefault(p => p.Value.Marker == marker).Key;
        var highlighted = new HashSet<Guid>();
        if (_migratedHighlights && activeKey is not null)
        foreach (var key in new[] { activeKey })
        {
            if (_deletedRelationships.Contains(key) || GetRelationshipInfo(key) is not { } info ||
                !_columns.TryGetValue(info.Source, out var parent) || !_columns.TryGetValue(info.Target, out var child) ||
                FindRelationshipLine(key) is not { Visibility: Visibility.Visible }) continue;
            foreach (var row in RelationshipMappingRows(key, info.Source, info.Target))
            {
                if (row.ParentColumn == null || row.ChildColumn == null) continue;
                highlighted.Add(row.ParentColumn.Id);
                highlighted.Add(row.ChildColumn.Id);
            }
        }
        var highlightBrush = ThemeBrush(_migratedHighlightColor);
        foreach (var row in Descendants<Border>(DiagramCanvasSurface))
        {
            if (row.Tag is not AttributeSelection selection || row.Child is not Grid grid ||
                !_columns.TryGetValue(selection.EntityKey, out var columns)) continue;
            var column = columns.FirstOrDefault(c => c.Name == selection.AttributeName);
            var active = column is not null && highlighted.Contains(column.Id);
            foreach (var name in grid.Children.OfType<TextBlock>().Where(t => Grid.GetColumn(t) == 1))
            {
                var brush = (SolidColorBrush)(active ? highlightBrush : ThemeBrush("#DCE7F5"));
                var weight = active ? (_migratedHighlightBold ? FontWeights.Bold : FontWeights.Normal) : FontWeights.SemiBold;
                if (name.Foreground is not SolidColorBrush old || old.Color != brush.Color) name.Foreground = brush;
                if (name.FontWeight != weight) name.FontWeight = weight;
            }
        }
    }
}
