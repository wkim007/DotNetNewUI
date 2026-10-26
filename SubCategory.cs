using System.Windows;
using System.Windows.Input;
namespace ErwinStudioSample;
public partial class MainWindow
{
    private bool BlockSubCategoryKeyMove(string entityKey, ColumnInfo column)
    {
        if (!_dynamicRelationships.Any(p => p.Value.IsSubCategory && p.Value.TargetKey == entityKey &&
            !_deletedRelationships.Contains(p.Key))) return false;
        StatusText.Text = column.IsPrimaryKey
            ? "Cannot move Sub-Category child attribute to the non-PK area: the child's primary keys must remain inherited from its parent."
            : "Cannot move Sub-Category child attribute to the PK area: the child's primary keys are defined by its parent.";
        return true;
    }

    private void UpdateDiagramTools()
    {
        if (SubCategoryToolButton is null) return;
        var logical = ViewModeBox.SelectedIndex == 1;
        ViewToolButton.Visibility = MaterializedViewToolButton.Visibility = ViewRelationshipToolButton.Visibility = logical ? Visibility.Collapsed : Visibility.Visible;
        SubCategoryToolButton.Visibility = logical ? Visibility.Visible : Visibility.Collapsed;
        if ((logical && _pendingRelationshipType == "View relationship") || (!logical && _pendingRelationshipType == "Sub-Category"))
        { _pendingRelationshipType = null; _relationshipSourceKey = null; }
    }
    private bool HandleSubCategoryClick(string key, MouseButtonEventArgs e)
    {
        if (_pendingRelationshipType != "Sub-Category") return false;
        var source = _relationshipSourceKey;
        // The tool is one-shot even when validation fails. Never leave ordinary
        // object selection trapped in connection mode after a rejected target.
        CancelPendingRelationshipTool();
        if (ViewModeBox.SelectedIndex != 1 || source is null || !_entityCardKeys.Contains(source) ||
            !_entityCardKeys.Contains(key) || source == key)
            return false;
        e.Handled = true;
        if (!TryCreateSubCategory(source, key) && FindCard(key) is { } card)
        {
            var message = StatusText.Text;
            ClearAttributeSelection(); ClearRelationshipSelection();
            SelectEntity(key); SelectCard(card);
            StatusText.Text = message;
        }
        return true;
    }

    private void CancelPendingRelationshipTool()
    {
        _pendingRelationshipType = null;
        _relationshipSourceKey = null;
    }

    private void DiagramRelationship_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _pendingRelationshipType is null || _inlineRenameActive) return;
        CancelPendingRelationshipTool();
        StatusText.Text = "Relationship tool canceled. Select an object.";
        e.Handled = true;
    }

    private bool TryCreateSubCategory(string sourceKey, string childKey)
    {
        if (ViewModeBox.SelectedIndex != 1 || sourceKey == childKey ||
            !_entityCardKeys.Contains(sourceKey) || !_entityCardKeys.Contains(childKey) ||
            !_columns.TryGetValue(sourceKey, out var parentColumns) || !_columns.TryGetValue(childKey, out var childColumns) ||
            FindCard(sourceKey) is not { } parentCard || FindCard(childKey) is not { } childCard ||
            !DiagramCanvasSurface.Children.Contains(parentCard) || !DiagramCanvasSurface.Children.Contains(childCard))
        { StatusText.Text = "Choose a different child entity in Logical View for the Sub-Category."; return false; }
        var subtypes = _dynamicRelationships.Where(p => p.Value.IsSubCategory && !_deletedRelationships.Contains(p.Key)).Select(p => p.Value).ToArray();
        if (subtypes.Any(r => r.SourceKey == sourceKey && r.TargetKey == childKey))
        { StatusText.Text = "A Sub-Category relationship already exists between these entities."; return false; }
        var visited = new HashSet<string>();
        var pending = new Stack<string>(); pending.Push(childKey);
        while (pending.TryPop(out var next))
        {
            if (!visited.Add(next)) continue;
            if (next == sourceKey)
            { StatusText.Text = "A Sub-Category relationship cannot create an inheritance cycle."; return false; }
            foreach (var subtype in subtypes.Where(r => r.SourceKey == next)) pending.Push(subtype.TargetKey);
        }
        var childKeys = childColumns.Where(c => c.IsPrimaryKey).ToArray();
        if (childKeys.Length > 0)
        {
            StatusText.Text = $"Cannot create Sub-Category: primary key attribute(s) already exist in child '{EntityDisplayName(childKey)}': {string.Join(", ", childKeys.Select(c => c.Name))}. Remove the child's primary keys first.";
            return false;
        }
        var primaryKeys = parentColumns.Where(c => c.IsPrimaryKey).ToArray();
        bool SameName(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
        var existing = primaryKeys.Where(pk => childColumns.Any(c => SameName(c.Name, pk.Name) ||
            !string.IsNullOrWhiteSpace(pk.PhysicalName) && !string.IsNullOrWhiteSpace(c.PhysicalName) && SameName(c.PhysicalName, pk.PhysicalName))).ToArray();
        if (existing.Length > 0)
        {
            StatusText.Text = $"Cannot create Sub-Category: primary key attribute(s) already exist in '{EntityDisplayName(childKey)}': {string.Join(", ", existing.Select(c => c.Name))}.";
            return false;
        }
        // Validate the complete composite key before making any changes.
        var inherited = primaryKeys.Select(pk => pk with { Id = Guid.NewGuid(), ParentId = null, Required = true, IsPrimaryKey = true, IsForeignKey = true }).ToArray();
        for (var i = 0; i < inherited.Length; i++) childColumns.Insert(i, inherited[i]);
        RebuildNestedRows(childKey);
        CreateDynamicRelationship(sourceKey, childKey, "Sub-Category");
        var created = _dynamicRelationships.Single(p => p.Value.IsSubCategory && p.Value.SourceKey == sourceKey && p.Value.TargetKey == childKey);
        _dynamicRelationships[created.Key] = created.Value with { InheritedAttributeIds = inherited.Select(c => c.Id).ToArray() };
        ClearCardSelection(); ClearRelationshipSelection();
        UpdateRelationshipLines();
        SaveActiveDiagram();
        StatusText.Text = $"Created Sub-Category: {EntityDisplayName(sourceKey)} → {EntityDisplayName(childKey)}. Inherited {inherited.Length} primary key attribute(s).";
        return true;
    }

    private void RemoveSubCategoryAttributes(DynamicRelationship relation)
    {
        if (!_columns.TryGetValue(relation.TargetKey, out var children)) return;
        // Older files did not record ownership; only matching PK/FK columns
        // qualify for that compatibility fallback.
        var ids = relation.InheritedAttributeIds?.ToHashSet() ?? children.Where(c => c.IsPrimaryKey && c.IsForeignKey &&
            _columns.TryGetValue(relation.SourceKey, out var parents) && parents.Any(p => p.IsPrimaryKey &&
                (p.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase) ||
                 !string.IsNullOrWhiteSpace(p.PhysicalName) && p.PhysicalName.Equals(c.PhysicalName, StringComparison.OrdinalIgnoreCase))))
            .Select(c => c.Id).ToHashSet();
        foreach (var column in children.Where(c => ids.Contains(c.Id)).ToArray())
        {
            children.Remove(column);
            _collapsedAttributes.Remove(column.Id);
        }
        ClearAttributeSelection();
        ColumnsGrid.SelectedItem = null;
        AttributePropertiesPanel.Children.Clear();
        AttributePropertiesPanel.Visibility = Visibility.Collapsed;
        RebuildNestedRows(relation.TargetKey);
    }
}
