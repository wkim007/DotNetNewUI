using System.Windows;
using System.Windows.Controls;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private ContextMenu CreateTextDeleteMenu(out MenuItem delete)
    {
        var menu = new ContextMenu { Background = ThemeBrush("#0E1824"), Foreground = ThemeInk, BorderBrush = ThemeBrush("#293D50"), Padding = new Thickness(2) };
        // The ContextMenu template also paints a system-colored icon gutter,
        // independently of the MenuItem template. Replace both layers.
        var menuBorder = new FrameworkElementFactory(typeof(Border));
        menuBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        menuBorder.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        menuBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        menuBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        menuBorder.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        menu.Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = menuBorder };
        delete = new MenuItem { Header = "Delete", Padding = new Thickness(10, 6, 10, 6) };
        // A text-only template avoids WPF's reserved icon/checkmark gutter.
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(MenuItem)) { VisualTree = border };
        var hover = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, ThemeBrush("#304860")));
        template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        template.Triggers.Add(disabled);
        delete.Template = template;
        menu.Items.Add(delete);
        return menu;
    }

    private bool CanDeleteDiagramAttribute(string key, Guid id) =>
        _entityCardKeys.Contains(key) && !_viewCardKeys.Contains(key) &&
        _columns.TryGetValue(key, out var columns) && columns.Any(c => c.Id == id) &&
        FindCard(key) is { } card && DiagramCanvasSurface.Children.Contains(card);

    private void ConfigureAttributeContextMenu(Border row)
    {
        if (row.Tag is not AttributeSelection selection ||
            !_columns.TryGetValue(selection.EntityKey, out var columns) ||
            columns.FirstOrDefault(c => c.Name == selection.AttributeName) is not { } attribute) return;
        var key = selection.EntityKey;
        var id = attribute.Id;
        var menu = CreateTextDeleteMenu(out var delete);
        row.ContextMenu = menu;
        row.PreviewMouseRightButtonDown += (_, _) =>
        {
            if (!_columns.TryGetValue(key, out var current) || current.FirstOrDefault(c => c.Id == id) is not { } selected ||
                FindCard(key) is not { } card || !DiagramCanvasSurface.Children.Contains(card)) return;
            CancelPendingRelationshipTool();
            _entityHoldTimer.Stop(); _entityHoldCard = null; _attributeDragRow = null;
            ClearAttributeSelection(); ClearRelationshipSelection();
            SelectEntity(key); SelectCard(card);
            ColumnsGrid.SelectedItem = selected;
            _selectedAttributeRow = row;
            row.Background = ThemeBrush("#266771"); row.BorderBrush = ThemeBrush("#48ADB4");
            ShowAttributeProperties(key, selected);
        };
        menu.Opened += (_, _) =>
        {
            delete.IsEnabled = CanDeleteDiagramAttribute(key, id);
            delete.ToolTip = _viewCardKeys.Contains(key) ? "View columns are read-only." : "Delete this column";
        };
        // Keep the ID rather than the name or grid selection: the user may rename
        // an attribute or previously have selected a different entity's column.
        delete.Click += (_, _) => DeleteDiagramAttribute(key, id);
    }

    private bool DeleteDiagramAttribute(string key, Guid id)
    {
        if (!CanDeleteDiagramAttribute(key, id)) return false;
        var columns = _columns[key];
        var column = columns.First(c => c.Id == id);
        var removed = columns.Where(c => c.Id == id || IsDescendant(c, column, columns)).ToArray();
        ClearAttributeSelection();
        AttributePropertiesPanel.Children.Clear();
        AttributePropertiesPanel.Visibility = Visibility.Collapsed;
        ColumnsGrid.SelectedItem = null;
        foreach (var item in removed) { columns.Remove(item); _collapsedAttributes.Remove(item.Id); }
        RebuildNestedRows(key);
        SelectEntity(key);
        SaveActiveDiagram();
        StatusText.Text = $"Deleted column '{column.Name}' from '{EntityDisplayName(key)}'.";
        return true;
    }
}
