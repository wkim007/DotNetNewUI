using System.Windows;
using System.Windows.Controls;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private readonly Dictionary<string, string> _entityDisplayNames = new();
    private TreeViewItem? _explorerRoot;
    private sealed record ExplorerRelationship(string Key);
    private readonly TreeViewItem _explorerRelationships = new() { Header = "Relationships", IsExpanded = true };
    private readonly Dictionary<string, TreeViewItem> _explorerRelationshipItems = new();
    private readonly TreeViewItem _explorerEntities = new() { IsExpanded = true };
    private readonly TreeViewItem _explorerViews = new() { Header = "Views", IsExpanded = true };
    private readonly TreeViewItem _explorerMViews = new() { Header = "MViews", IsExpanded = true };
    private readonly Dictionary<string, TreeViewItem> _explorerItems = new();

    private string EntityDisplayName(string key) => _entityDisplayNames.GetValueOrDefault(key, key == "OrderItem" ? "Order Item" : key);

    private void ConfigureExplorerDelete(TreeViewItem item)
    {
        // Right-click selects the clicked leaf rather than acting on an earlier selection.
        item.PreviewMouseRightButtonDown += (_, _) => { item.IsSelected = true; item.Focus(); };
        var menu = new ContextMenu();
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += (_, args) =>
        {
            if (item.Tag is ExplorerRelationship relationship &&
                _explorerRelationshipItems.TryGetValue(relationship.Key, out var currentRelationship) && currentRelationship == item)
                DeleteRelationship(relationship.Key);
            else if (item.Tag is string key && _explorerItems.TryGetValue(key, out var currentItem) && currentItem == item &&
                (_entityCardKeys.Contains(key) || _viewCardKeys.Contains(key)))
                DeleteObject(key);
            args.Handled = true;
        };
        menu.Items.Add(delete);
        item.ContextMenu = menu;
    }
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
            _explorerRoot.Items.Add(_explorerRelationships);
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
                if (_entityCardKeys.Contains(key) || _viewCardKeys.Contains(key)) ConfigureExplorerDelete(item);
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
        var relationships = new[] { "CustomerOrder", "OrderProduct", "OrderOrderItem", "ProductOrderItem" }
            .Concat(_dynamicRelationships.Keys)
            .Where(key => !_deletedRelationships.Contains(key)
                && FindRelationshipLine(key) is { } line && DiagramCanvasSurface.Children.Contains(line)
                && GetRelationshipInfo(key) is { } info && keys.Contains(info.Source) && keys.Contains(info.Target))
            .ToHashSet();
        foreach (var key in _explorerRelationshipItems.Keys.Where(key => !relationships.Contains(key)).ToArray())
        {
            _explorerRelationships.Items.Remove(_explorerRelationshipItems[key]);
            _explorerRelationshipItems.Remove(key);
        }
        foreach (var key in relationships)
        {
            if (!_explorerRelationshipItems.TryGetValue(key, out var item))
            {
                item = new TreeViewItem { Tag = new ExplorerRelationship(key) };
                ConfigureExplorerDelete(item);
                _explorerRelationshipItems.Add(key, item);
                _explorerRelationships.Items.Add(item);
            }
            var info = GetRelationshipInfo(key)!.Value;
            var name = $"{EntityDisplayName(info.Source)} → {EntityDisplayName(info.Target)}";
            item.Header = name;
            item.ToolTip = info.Type;
            item.Visibility = name.Contains(query, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }
        // Search filters explorer entries without overlaying the diagram canvas.
        if (EmptyHint is not null) EmptyHint.Visibility = Visibility.Collapsed;
    }
}
