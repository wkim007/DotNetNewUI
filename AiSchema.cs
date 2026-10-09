using System.Text.Json;

namespace ErwinStudioSample;

internal sealed record AiSchemaColumn(string Name, string Type, bool Required, bool PrimaryKey);
internal sealed record AiSchemaTable(string Name, string Definition, AiSchemaColumn[] Columns);
internal sealed record AiSchemaLink(string Source, string Target, string SourceColumn, string TargetColumn);
internal sealed record AiSchema(AiSchemaTable[] Tables, AiSchemaLink[] Relationships)
{
    public static AiSchema Parse(string text)
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
        if (schema.Tables is null || schema.Tables.Length is < 1 or > 50 || schema.Relationships is null || schema.Relationships.Length > 200)
            throw new InvalidOperationException("The schema must contain 1–50 tables and at most 200 relationships.");
        var tables = new Dictionary<string, AiSchemaTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in schema.Tables)
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
