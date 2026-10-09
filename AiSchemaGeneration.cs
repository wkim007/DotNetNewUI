using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private async Task GenerateAiDiagram(AiConfiguration config, string description)
    {
        var prompt = "Generate a relational database model for the user's description. Return ONLY JSON, no SQL or explanation. " +
            "Honor the requested table count. Use data types appropriate for the database. Maximum 50 tables. " +
            "Format: {\"tables\":[{\"name\":\"department\",\"definition\":\"Departments\",\"columns\":[{\"name\":\"department_id\",\"type\":\"INT\",\"required\":true,\"primaryKey\":true}]}]," +
            "\"relationships\":[{\"source\":\"department\",\"target\":\"employee\",\"sourceColumn\":\"department_id\",\"targetColumn\":\"department_id\"}]}. " +
            "Every relationship goes from referenced parent primary key to child foreign-key column. Include all referenced tables and columns. " +
            "Database: " + ProjectDatabaseBox.Text + " " + ProjectVersionBox.Text + "\nUser description: " + description;
        var response = await RequestAi(config, prompt, default, maxTokens: 12000);
        AiSchema schema;
        try { schema = AiSchema.Parse(response); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        { AiStatus.Text = "Could not create the model: " + ex.Message + " Existing diagrams are unchanged."; return; }

        AddDiagram_Click(this, new RoutedEventArgs());
        var diagram = _activeDiagram;
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var across = (int)Math.Ceiling(Math.Sqrt(schema.Tables.Length));
        var rowHeight = 0d;
        var y = 50d;
        for (var i = 0; i < schema.Tables.Length; i++)
        {
            if (i > 0 && i % across == 0) { y += rowHeight + 90; rowHeight = 0; }
            var table = schema.Tables[i];
            AddNewEntity();
            var key = _newEntityNumber == 1 ? "NewEntity" : $"NewEntity{_newEntityNumber}";
            keys.Add(table.Name, key);
            var columns = table.Columns.OrderByDescending(c => c.PrimaryKey).Select(c => new ColumnInfo(c.Name, c.Type, c.Required || c.PrimaryKey, c.PrimaryKey,
                schema.Relationships.Any(r => string.Equals(r.Target, table.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(r.TargetColumn, c.Name, StringComparison.OrdinalIgnoreCase))));
            _columns[key] = new ObservableCollection<ColumnInfo>(columns);
            _entityDisplayNames[key] = table.Name;
            _objectDefinitions[key] = table.Definition ?? "";
            var card = _dynamicCards[key];
            card.Width = 300; card.Height = 85 + table.Columns.Length * 29;
            rowHeight = Math.Max(rowHeight, card.Height);
            Canvas.SetLeft(card, 50 + i % across * 410); Canvas.SetTop(card, y);
            if (card.Child is Grid grid)
            {
                var header = grid.Children.OfType<Border>().First(b => Grid.GetRow(b) == 0);
                var title = Descendants<TextBlock>(header).FirstOrDefault();
                if (title is not null) title.Text = table.Name;
                var fields = grid.Children.OfType<StackPanel>().First(p => Grid.GetRow(p) == 1);
                fields.Children.Clear();
                var dividerAdded = false;
                foreach (var column in _columns[key])
                {
                    if (!column.IsPrimaryKey && !dividerAdded) { fields.Children.Add(new Separator()); dividerAdded = true; }
                    fields.Children.Add(new TextBlock { Text = column.Name });
                }
                if (!dividerAdded) fields.Children.Add(new Separator());
                PrepareAttributeRows(card, key);
            }
        }
        foreach (var link in schema.Relationships)
        {
            var childColumn = schema.Tables.First(t => string.Equals(t.Name, link.Target, StringComparison.OrdinalIgnoreCase)).Columns.First(c => string.Equals(c.Name, link.TargetColumn, StringComparison.OrdinalIgnoreCase));
            CreateDynamicRelationship(keys[link.Source], keys[link.Target], childColumn.PrimaryKey ? "Identifying relationship" : "Non-identifying relationship");
        }
        ClearCardSelection(); ClearRelationshipSelection();
        DiagramCanvasSurface.MinWidth = Math.Max(930, 100 + across * 410);
        DiagramCanvasSurface.MinHeight = Math.Max(650, y + rowHeight + 70);
        ApplyProjectViewMode(); ApplyTheme(); RefreshModelExplorer(); SaveActiveDiagram();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (_activeDiagram == diagram) UpdateRelationshipLines(); }));
        AiStatus.Text = StatusText.Text = $"Generated {schema.Tables.Length} tables and {schema.Relationships.Length} relationships in {diagram?.Title}.";
    }
}


