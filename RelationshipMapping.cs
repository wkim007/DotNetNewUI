namespace ErwinStudioSample;

public partial class MainWindow
{
    private readonly Dictionary<string, ColumnMapping[]> _sampleColumnMappings = new();

    private void InitializeSampleColumnMappings()
    {
        foreach (var key in new[] { "CustomerOrder", "OrderOrderItem", "ProductOrderItem" })
        {
            var info = GetRelationshipInfo(key)!.Value;
            var parent = _columns[info.Source].Single(c => c.IsPrimaryKey);
            var child = _columns[info.Target].Single(c => c.IsForeignKey && c.Name == parent.Name);
            _sampleColumnMappings[key] = [new(parent.Id, child.Id)];
        }
    }

    private sealed record MappingRow(ColumnInfo? ParentColumn, ColumnInfo? ChildColumn, bool Inferred = false)
    {
        public string Parent => ParentColumn?.Name ?? "(missing column)";
        public string Child => ChildColumn == null ? "Unmapped" : ChildColumn.Name + (Inferred ? " (inferred)" : "");
    }

    private IEnumerable<MappingRow> RelationshipMappingRows(string key, string source, string target)
    {
        if (!_columns.TryGetValue(source, out var parents) || !_columns.TryGetValue(target, out var children)) yield break;
        _dynamicRelationships.TryGetValue(key, out var relationship);
        if ((relationship?.ColumnMappings ?? _sampleColumnMappings.GetValueOrDefault(key)) is { } mappings)
        {
            foreach (var pair in mappings)
                yield return new MappingRow(parents.FirstOrDefault(c => c.Id == pair.Parent), children.FirstOrDefault(c => c.Id == pair.Child));
            yield break;
        }
        if (relationship?.IsViewRelationship == true) yield break;
        var primary = parents.Where(c => c.IsPrimaryKey).ToArray();
        var parallel = DiagramCanvasSurface.Children.OfType<System.Windows.Shapes.Path>().Any(p => p.Tag is string other && other != key &&
            !_deletedRelationships.Contains(other) && GetRelationshipInfo(other) is { } info && info.Source == source && info.Target == target);
        for (var i = 0; i < primary.Length; i++)
        {
            var pk = primary[i];
            ColumnInfo? child = null;
            var inferred = false;
            if (relationship?.InheritedAttributeIds is { } ids && ids.Length == primary.Length)
                child = children.FirstOrDefault(c => c.Id == ids[i]);
            else if (!parallel)
            {
                var matches = children.Where(c => c.IsForeignKey && (pk.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(pk.PhysicalName) && pk.PhysicalName.Equals(c.PhysicalName, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (matches.Length == 1) { child = matches[0]; inferred = true; }
            }
            yield return new MappingRow(pk, child, inferred);
        }
    }
}
