using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private readonly HashSet<Guid> _collapsedAttributes = [];
    private bool IsContainer(ColumnInfo column) => ProjectDatabaseBox.Text.Contains("mongo", StringComparison.OrdinalIgnoreCase) && (column.Type.Equals("object", StringComparison.OrdinalIgnoreCase) || column.Type.Equals("array", StringComparison.OrdinalIgnoreCase));
    private bool HasChildren(ColumnInfo column) => ColumnsGrid.ItemsSource is IEnumerable<ColumnInfo> columns && columns.Any(c => c.ParentId == column.Id);

    private void NestedSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AddChildButton is null) return;
        var editable = EntityNameBox.Tag is string key && !_viewCardKeys.Contains(key);
        AddChildButton.IsEnabled = editable && ColumnsGrid.SelectedItem is ColumnInfo column && IsContainer(column);
        DeleteAttributeButton.IsEnabled = editable && ColumnsGrid.SelectedItem is ColumnInfo;
    }

    private void AddChild_Click(object sender, RoutedEventArgs e)
    {
        if (EntityNameBox.Tag is not string key || _viewCardKeys.Contains(key) || ColumnsGrid.SelectedItem is not ColumnInfo parent || !IsContainer(parent)) return;
        var columns = _columns[key];
        var number = 1;
        while (columns.Any(c => c.Name.Equals($"Child{number}", StringComparison.OrdinalIgnoreCase))) number++;
        var child = new ColumnInfo($"Child{number}", "string", false) { ParentId = parent.Id };
        var index = columns.IndexOf(parent) + 1;
        while (index < columns.Count && IsDescendant(columns[index], parent, columns)) index++;
        columns.Insert(index, child);
        _collapsedAttributes.Remove(parent.Id);
        RebuildNestedRows(key);
        ColumnsGrid.SelectedItem = child;
        ColumnsGrid.ScrollIntoView(child);
        StatusText.Text = $"Added {child.Name} under {parent.Name}. Hold its name to rename.";
    }

    private static bool IsDescendant(ColumnInfo child, ColumnInfo ancestor, IEnumerable<ColumnInfo> columns)
    {
        var parentId = child.ParentId;
        while (parentId is { } id)
        {
            if (id == ancestor.Id) return true;
            parentId = columns.FirstOrDefault(c => c.Id == id)?.ParentId;
        }
        return false;
    }

    private void DeleteAttribute_Click(object sender, RoutedEventArgs e)
    {
        if (EntityNameBox.Tag is not string key || _viewCardKeys.Contains(key) || ColumnsGrid.SelectedItem is not ColumnInfo column) return;
        DeleteDiagramAttribute(key, column.Id);
    }

    private void RebuildNestedRows(string key)
    {
        if (FindCard(key) is not { Child: Grid grid } card) return;
        var fields = grid.Children.OfType<StackPanel>().FirstOrDefault(p => Grid.GetRow(p) == 1);
        if (fields is null) return;
        ClearAttributeSelection(); fields.Children.Clear();
        var columns = _columns[key];
        var dividerAdded = false;
        void AddRows(Guid? parent, int depth)
        {
            foreach (var column in columns.Where(c => c.ParentId == parent).OrderByDescending(c => c.IsPrimaryKey))
            {
                if (depth == 0 && !column.IsPrimaryKey && !dividerAdded) { fields.Children.Add(CreatePkDivider(key)); dividerAdded = true; }
                var row = new Border { Tag = new AttributeSelection(key, column.Name), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(depth * 18, 1, 0, 1) };
                var layout = CreateAttributeLayout(column, column.Name);
                // Reserve the expander space on every row so each depth adds a full indent.
                layout.Margin = new Thickness(17, 2, 0, 2);
                row.Child = layout;
                row.MouseLeftButtonDown += AttributeRow_MouseLeftButtonDown;
                ConfigureAttributeDrag(row);
                if (IsContainer(column) || columns.Any(c => c.ParentId == column.Id))
                {
                    layout.Margin = new Thickness(17, 2, 0, 2);
                    var toggle = new Button { Content = _collapsedAttributes.Contains(column.Id) ? "▸" : "▾", Width = 17, Padding = new Thickness(0), Margin = new Thickness(-17, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Foreground = column.Type.Equals("array", StringComparison.OrdinalIgnoreCase) ? Brushes.Yellow : Brushes.SkyBlue, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "Expand / collapse child attributes" };
                    toggle.PreviewMouseLeftButtonDown += (_, args) => args.Handled = true;
                    toggle.PreviewMouseLeftButtonUp += (_, args) => { if (!_collapsedAttributes.Add(column.Id)) _collapsedAttributes.Remove(column.Id); RebuildNestedRows(key); args.Handled = true; };
                    toggle.Click += (_, _) => { if (!_collapsedAttributes.Add(column.Id)) _collapsedAttributes.Remove(column.Id); RebuildNestedRows(key); };
                    layout.Children.Add(toggle);
                }
                fields.Children.Add(row);
                if (!_collapsedAttributes.Contains(column.Id)) AddRows(column.Id, depth + 1);
            }
        }
        AddRows(null, 0);
        if (!dividerAdded) fields.Children.Add(CreatePkDivider(key));
        var fullHeight = 80 + fields.Children.Count * 29;
        if (_displayCards.TryGetValue(card, out var displayState) && displayState.LastLevel != 1) displayState.Height = fullHeight;
        card.Height = Math.Max(card.Height, fullHeight);
        ColumnsGrid.Items.Refresh();
        ApplyProjectViewMode(); UpdateRelationshipLines();
    }
}
