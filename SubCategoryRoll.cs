namespace ErwinStudioSample;

public partial class MainWindow
{
    private bool RollSubCategory(string sourceKey, bool up)
        => RollSubCategoryBothViews(sourceKey, up, null);

    private bool RollSubCategoryBothViews(string sourceKey, bool up, string? branchKey)
    {
        if (!RollSubCategoryCore(sourceKey, up, branchKey)) return false;
        // Regular rolls replace the physical-only projection. Both views now
        // render this transformed model; rejected operations keep the projection.
        _alternateViewModel = null;
        _rolledViewIndex = ViewModeBox.SelectedIndex;
        return true;
    }

    private bool RollIdentifiedBranch(string key, bool up)
    {
        if (ViewModeBox.SelectedIndex != 1 || !_dynamicRelationships.TryGetValue(key, out var relation) ||
            !relation.IsSubCategory || !relation.IsSupertypeSubtypeIdentified) return false;
        return RollSubCategoryBothViews(relation.SourceKey, up, key);
    }

    private bool RollSubCategoryCore(string sourceKey, bool up, string? branchKey)
    {
        var group = _dynamicRelationships.Where(p => p.Value.IsSubCategory && p.Value.SourceKey == sourceKey &&
            (branchKey is null || p.Key == branchKey) && DiagramCanvasSurface.Children.Contains(p.Value.Visible)).ToArray();
        if (group.Length == 0 || !_columns.ContainsKey(sourceKey)) return false;
        var children = group.Select(p => p.Value.TargetKey).Distinct().ToArray();
        var removed = (up ? children : new[] { sourceKey }).ToHashSet();
        var groupKeys = group.Select(p => p.Key).ToHashSet();
        var redirected = _dynamicRelationships.Where(p => branchKey is not null && !up &&
            p.Value.IsSubCategory && p.Value.SourceKey == sourceKey && !groupKeys.Contains(p.Key)).ToArray();
        var redirectedKeys = redirected.Select(p => p.Key).ToHashSet();
        var otherEdges = new[] { "CustomerOrder", "OrderProduct", "OrderOrderItem", "ProductOrderItem" }.Concat(_dynamicRelationships.Keys);
        if (otherEdges.Any(k => !groupKeys.Contains(k) && !redirectedKeys.Contains(k) && !_deletedRelationships.Contains(k) &&
            FindRelationshipLine(k) is { } line && DiagramCanvasSurface.Children.Contains(line) &&
            GetRelationshipInfo(k) is { } info && (removed.Contains(info.Source) || removed.Contains(info.Target))))
        {
            StatusText.Text = "Cannot roll Sub-Category: an object to remove has other relationships. Remove those relationships first.";
            return false;
        }
        // Stage every destination first so a conflict never leaves a partial roll.
        var staged = new Dictionary<string, List<ColumnInfo>>();
        try
        {
            foreach (var destination in up ? new[] { sourceKey } : children)
            {
                var result = _columns[destination].ToList();
                foreach (var origin in up ? children : new[] { sourceKey })
                {
                    var mapping = new Dictionary<Guid, Guid>();
                    foreach (var column in _columns[origin])
                    {
                        var existing = result.FirstOrDefault(c => c.Name.Equals(column.Name, StringComparison.OrdinalIgnoreCase));
                        if (existing is not null)
                        {
                            if (!existing.Type.Equals(column.Type, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException($"Attribute '{column.Name}' has conflicting datatypes.");
                            mapping[column.Id] = existing.Id;
                            // Parent keys become the child's own keys after RollDown.
                            if (!up && column.IsPrimaryKey)
                                result[result.IndexOf(existing)] = existing with { IsPrimaryKey = true, IsForeignKey = column.IsForeignKey, Required = column.Required };
                        }
                        else mapping[column.Id] = Guid.NewGuid();
                    }
                    foreach (var column in _columns[origin])
                    {
                        if (result.Any(c => c.Id == mapping[column.Id])) continue;
                        result.Add(column with { Id = mapping[column.Id], ParentId = column.ParentId is { } parent ? mapping[parent] : null });
                    }
                }
                staged[destination] = result;
            }
        }
        catch (InvalidOperationException ex)
        { StatusText.Text = "Cannot roll Sub-Category: " + ex.Message; return false; }

        ClearAttributeSelection(); ClearCardSelection(); ClearRelationshipSelection();
        // Rolling retains the merged keys, unlike deleting the subtype group.
        foreach (var pair in group)
        {
            _dynamicRelationships[pair.Key] = pair.Value with { InheritedAttributeIds = [] };
            DeleteRelationship(pair.Key);
        }
        foreach (var pair in redirected)
            _dynamicRelationships[pair.Key] = pair.Value with { SourceKey = children[0] };
        foreach (var key in removed) DeleteObject(key);
        foreach (var (key, attributes) in staged)
        {
            _columns[key].Clear();
            foreach (var column in attributes) _columns[key].Add(column);
            RebuildNestedRows(key);
        }
        SelectEntity(up ? sourceKey : children[0]);
        UpdateRelationshipLines(); RefreshModelExplorer(); SaveActiveDiagram();
        StatusText.Text = up ? "RollUp completed: child attributes merged into the parent." : "RollDown completed: parent attributes inherited by each child.";
        return true;
    }
}
