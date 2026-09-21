using System.Windows;
using System.Windows.Controls;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private readonly Dictionary<string, string> _entityDisplayNames = new();
    private TreeViewItem? _explorerRoot;
    private readonly TreeViewItem _explorerEntities = new() { IsExpanded = true };
    private readonly TreeViewItem _explorerViews = new() { Header = "Views", IsExpanded = true };
    private readonly TreeViewItem _explorerMViews = new() { Header = "MViews", IsExpanded = true };
    private readonly Dictionary<string, TreeViewItem> _explorerItems = new();

    private string EntityDisplayName(string key) => _entityDisplayNames.GetValueOrDefault(key, key == "OrderItem" ? "Order Item" : key);

    private void RefreshModelExplorer()
    {
        if (ModelTree is null || DiagramCanvasSurface is null) return;
        if (_explorerRoot is null)
        {
            ModelTree.Items.Clear();
            _explorerRoot = new TreeViewItem { IsExpanded = true };
            ModelTree.Items.Add(_explorerRoot);
            _explorerRoot.Items.Add(_explorerEntities);
            _explorerRoot.Items.Add(_explorerViews);
            _explorerRoot.Items.Add(_explorerMViews);
        }
        _explorerRoot.Header = _activeDiagram?.Title ?? "ER_Diagram_1";
        _explorerEntities.Header = ViewModeBox.SelectedIndex == 1 ? "Entities" : "Tables";
        var query = SearchBox?.Text.Trim() ?? "";
        var keys = _columns.Keys.Where(key => FindCard(key) is { } card && DiagramCanvasSurface.Children.Contains(card)).ToHashSet();
        foreach (var key in _explorerItems.Keys.Where(key => !keys.Contains(key)).ToArray())
        {
            if (_explorerItems[key].Parent is ItemsControl parent) parent.Items.Remove(_explorerItems[key]);
            _explorerItems.Remove(key);
        }
        foreach (var key in _columns.Keys.Where(keys.Contains))
        {
            if (!_explorerItems.TryGetValue(key, out var item))
            {
                item = new TreeViewItem { Tag = key };
                _explorerItems.Add(key, item);
                var group = _entityCardKeys.Contains(key) ? _explorerEntities
                    : _materializedViewKeys.Contains(key) ? _explorerMViews
                    : _viewCardKeys.Contains(key) ? _explorerViews : _explorerRoot;
                group.Items.Add(item);
            }
            var name = EntityDisplayName(key);
            item.Header = name;
            item.Visibility = name.Contains(query, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }
        // Search filters explorer entries without overlaying the diagram canvas.
        if (EmptyHint is not null) EmptyHint.Visibility = Visibility.Collapsed;
    }
}
