using System.Windows;
using System.Windows.Controls;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private string[] DatabaseTypes()
    {
        var database = ProjectDatabaseBox.Text.ToLowerInvariant();
        var types = database.Contains("mongo")
            ? "double|string|object|array|binData|undefined|objectId|bool|date|null|regex|dbPointer|javascript|symbol|javascriptWithScope|int|timestamp|long|decimal|minKey|maxKey"
            : database.Contains("postgres") || database.Contains("alloy")
            ? "smallint|integer|bigint|decimal|numeric|real|double precision|smallserial|serial|bigserial|money|char|varchar|text|bytea|boolean|date|time|timetz|timestamp|timestamptz|interval|uuid|json|jsonb|xml|bit|bit varying|inet|cidr|macaddr|macaddr8|point|line|lseg|box|path|polygon|circle|tsvector|tsquery|int4range|int8range|numrange|tsrange|tstzrange|daterange"
            : database.Contains("mysql")
            ? "TINYINT|SMALLINT|MEDIUMINT|INT|BIGINT|DECIMAL|NUMERIC|FLOAT|DOUBLE|BIT|BOOLEAN|DATE|DATETIME|TIMESTAMP|TIME|YEAR|CHAR|VARCHAR|BINARY|VARBINARY|TINYBLOB|BLOB|MEDIUMBLOB|LONGBLOB|TINYTEXT|TEXT|MEDIUMTEXT|LONGTEXT|ENUM|SET|JSON|GEOMETRY|POINT|LINESTRING|POLYGON|MULTIPOINT|MULTILINESTRING|MULTIPOLYGON|GEOMETRYCOLLECTION"
            : database.Contains("oracle")
            ? "CHAR|NCHAR|VARCHAR2|NVARCHAR2|NUMBER|FLOAT|BINARY_FLOAT|BINARY_DOUBLE|DATE|TIMESTAMP|TIMESTAMP WITH TIME ZONE|TIMESTAMP WITH LOCAL TIME ZONE|INTERVAL YEAR TO MONTH|INTERVAL DAY TO SECOND|RAW|LONG RAW|LONG|ROWID|UROWID|CLOB|NCLOB|BLOB|BFILE|XMLTYPE"
            : database.Contains("sqlite") ? "INTEGER|REAL|TEXT|BLOB|NUMERIC"
            : database.Contains("sql server") || database.Contains("mssql")
            ? "BIGINT|INT|SMALLINT|TINYINT|BIT|DECIMAL|NUMERIC|MONEY|SMALLMONEY|FLOAT|REAL|DATE|TIME|DATETIME|DATETIME2|SMALLDATETIME|DATETIMEOFFSET|CHAR|VARCHAR|TEXT|NCHAR|NVARCHAR|NTEXT|BINARY|VARBINARY|IMAGE|UNIQUEIDENTIFIER|ROWVERSION|TIMESTAMP|SQL_VARIANT|XML|HIERARCHYID|GEOMETRY|GEOGRAPHY"
            : "";
        return types.Split('|', StringSplitOptions.RemoveEmptyEntries);
    }

    private void ColumnType_Loaded(object sender, RoutedEventArgs e) => PopulateColumnTypes((ComboBox)sender);
    private void ColumnType_Opened(object sender, EventArgs e) => PopulateColumnTypes((ComboBox)sender);
    private void PopulateColumnTypes(ComboBox box)
    {
        if (box.DataContext is not ColumnInfo column) return;
        box.SelectionChanged -= ColumnType_Changed;
        // Preserve parameterized and user-defined types already present in the model.
        box.ItemsSource = new[] { column.Type }.Concat(DatabaseTypes()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        box.SelectedItem = column.Type;
        box.IsEnabled = !ColumnsGrid.IsReadOnly && !HasChildren(column);
        box.ToolTip = HasChildren(column) ? "Datatype is locked because this attribute has child attributes." : "Select datatype";
        box.SelectionChanged += ColumnType_Changed;
    }

    private void ColumnType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: ColumnInfo old, SelectedItem: string type } || type == old.Type ||
            ColumnsGrid.IsReadOnly || HasChildren(old) || EntityNameBox.Tag is not string key || !_columns.TryGetValue(key, out var columns)) return;
        var index = columns.IndexOf(old);
        if (index < 0) return;
        var updated = old with { Type = type };
        columns[index] = updated;
        if (FindCard(key) is { } card)
            foreach (var row in Descendants<Border>(card).Where(row => row.Tag is AttributeSelection selection && selection.AttributeName == old.Name))
                row.Child = CreateAttributeLayout(updated, updated.Name);
        RebuildNestedRows(key);
        ColumnsGrid.SelectedItem = updated;
        StatusText.Text = $"Changed {old.Name} datatype to {type}";
    }
}
