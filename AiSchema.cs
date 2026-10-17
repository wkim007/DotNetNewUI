using System.Text.Json;

namespace ErwinStudioSample;

internal sealed record AiSchemaColumn(string Name, string Type, bool Required, bool PrimaryKey, string Description = "");
internal sealed record AiSchemaTable(string Name, string Definition, AiSchemaColumn[] Columns);
internal sealed record AiSchemaLink(string Source, string Target, string SourceColumn, string TargetColumn);
internal sealed record AiColumnDeletion(string Table, string Column);
internal sealed record AiColumnRename(string Table, string Column, string NewName);
internal sealed record AiSchema(AiSchemaTable[] Tables, AiSchemaLink[] Relationships, string TargetDiagram = "new", AiSchemaTable[]? ColumnAdditions = null, AiColumnDeletion[]? ColumnDeletions = null, AiColumnRename[]? ColumnRenames = null)
{
    public static AiSchema Parse(string text, AiSchemaTable[]? existingTables = null)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var newline = text.IndexOf('\n');
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (newline < 0 || end <= newline) throw new InvalidOperationException("Incomplete schema response. Try Generate again.");
            text = text[(newline + 1)..end];
        }
        var schema = JsonSerializer.Deserialize<AiSchema>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The AI returned an empty schema.");
        var renames = schema.ColumnRenames ?? [];
        if (renames.Length > 100 || (renames.Length > 0 && schema.TargetDiagram != "current")) throw new InvalidOperationException("Column renames require the current diagram.");
        var deletions = schema.ColumnDeletions ?? [];
        if (deletions.Length > 100 || (deletions.Length > 0 && schema.TargetDiagram != "current")) throw new InvalidOperationException("Column deletions require the current diagram.");
        var additions = schema.ColumnAdditions ?? [];
        if (additions.Length > 50 || (additions.Length > 0 && schema.TargetDiagram != "current")) throw new InvalidOperationException("Column additions require the current diagram.");
        if (schema.Tables is null || schema.Tables.Length > 50 || (schema.Tables.Length == 0 && additions.Length == 0 && deletions.Length == 0 && renames.Length == 0) || schema.Relationships is null || schema.Relationships.Length > 200)
            throw new InvalidOperationException("The schema must contain new tables, column additions, column deletions, or column renames and at most 200 relationships.");
        var tables = new Dictionary<string, AiSchemaTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in schema.Tables.Concat(additions))
        {
            if (table is null || string.IsNullOrWhiteSpace(table.Name) || table.Name.Length > 128 || !tables.TryAdd(table.Name, table))
                throw new InvalidOperationException("Each generated table must have a unique, nonempty name.");
            if (table.Columns is null || table.Columns.Length is < 1 or > 100)
                throw new InvalidOperationException("Each table must contain 1–100 columns.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in table.Columns)
                if (column is null || string.IsNullOrWhiteSpace(column.Name) || column.Name.Length > 128 || !names.Add(column.Name) || string.IsNullOrWhiteSpace(column.Type) || column.Type.Length > 128)
                    throw new InvalidOperationException("Generated columns must have unique names and valid data types.");
        }
        if (schema.TargetDiagram is not ("new" or "current")) throw new InvalidOperationException("Invalid target diagram in AI response.");
        if (schema.TargetDiagram == "current")
            foreach (var existing in existingTables ?? [])
            {
                var addition = additions.FirstOrDefault(a => a.Name.Equals(existing.Name, StringComparison.OrdinalIgnoreCase));
                if (addition is not null)
                {
                    if (addition.Columns.Any(c => existing.Columns.Any(old => old.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))))
                        throw new InvalidOperationException("An added column already exists. No changes applied.");
                    tables[existing.Name] = existing with { Columns = existing.Columns.Concat(addition.Columns).ToArray() };
                }
                else if (!tables.TryAdd(existing.Name, existing)) throw new InvalidOperationException("An added table duplicates an existing name. No changes applied.");
            }
        if (additions.Any(a => !(existingTables ?? []).Any(t => t.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Column additions reference an unknown existing entity.");
        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var deletion in deletions)
        {
            if (deletion is null || string.IsNullOrWhiteSpace(deletion.Table) || string.IsNullOrWhiteSpace(deletion.Column) ||
                !deleted.Add(deletion.Table + "\0" + deletion.Column) ||
                !(existingTables ?? []).Any(t => t.Name.Equals(deletion.Table, StringComparison.OrdinalIgnoreCase) && t.Columns.Any(c => c.Name.Equals(deletion.Column, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Column deletion references a missing or duplicate target.");
            var table = tables[deletion.Table];
            tables[deletion.Table] = table with { Columns = table.Columns.Where(c => !c.Name.Equals(deletion.Column, StringComparison.OrdinalIgnoreCase)).ToArray() };
        }
        var renamed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rename in renames)
        {
            if (rename is null || string.IsNullOrWhiteSpace(rename.Table) || string.IsNullOrWhiteSpace(rename.Column) || string.IsNullOrWhiteSpace(rename.NewName) || rename.NewName.Length > 128 ||
                !renamed.Add(rename.Table + "\0" + rename.Column) ||
                !(existingTables ?? []).Any(t => t.Name.Equals(rename.Table, StringComparison.OrdinalIgnoreCase) && t.Columns.Any(c => c.Name.Equals(rename.Column, StringComparison.OrdinalIgnoreCase))) ||
                !tables.TryGetValue(rename.Table, out var table))
                throw new InvalidOperationException("Column rename references an unknown or duplicate target.");
            var old = table.Columns.FirstOrDefault(c => c.Name.Equals(rename.Column, StringComparison.OrdinalIgnoreCase));
            if (old is null || table.Columns.Any(c => c != old && c.Name.Equals(rename.NewName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Column rename conflicts with another operation or existing column name.");
            tables[rename.Table] = table with { Columns = table.Columns.Select(c => c == old ? c with { Name = rename.NewName } : c).ToArray() };
        }
        foreach (var link in schema.Relationships)
        {
            if (link is null || string.IsNullOrWhiteSpace(link.Source) || string.IsNullOrWhiteSpace(link.Target) ||
                !tables.TryGetValue(link.Source, out var source) || !tables.TryGetValue(link.Target, out var target) ||
                !source.Columns.Any(c => c.PrimaryKey && string.Equals(c.Name, link.SourceColumn, StringComparison.OrdinalIgnoreCase)) ||
                !target.Columns.Any(c => string.Equals(c.Name, link.TargetColumn, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A generated relationship references a missing table, primary key, or foreign-key column.");
        }
        return schema;
    }
}
