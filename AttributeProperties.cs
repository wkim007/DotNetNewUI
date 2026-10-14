using System.Windows;
using System.Windows.Controls;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private static (string Name, string Size) SplitAttributeType(string datatype)
    {
        var open = datatype.IndexOf('(');
        var close = datatype.LastIndexOf(')');
        return open > 0 && close > open ? (datatype[..open].Trim(), datatype[(open + 1)..close].Trim()) : (datatype.Trim(), "");
    }

    private void ShowAttributeProperties(string key, ColumnInfo selected)
    {
        EntityPropertiesPanel.Visibility = Visibility.Collapsed;
        RelationshipPropertiesPanel.Visibility = Visibility.Collapsed;
        AttributePropertiesPanel.Visibility = Visibility.Visible;
        AttributePropertiesPanel.Children.Clear();
        var readOnly = _viewCardKeys.Contains(key);
        var id = selected.Id;
        ColumnInfo Current() => _columns[key].First(c => c.Id == id);
        void Save(Func<ColumnInfo, ColumnInfo> update, bool redraw = false)
        {
            if (readOnly) return;
            var columns = _columns[key]; var index = columns.ToList().FindIndex(c => c.Id == id);
            if (index < 0) return;
            columns[index] = update(columns[index]);
            if (redraw) RebuildNestedRows(key);
            ColumnsGrid.SelectedItem = columns[index];
            if (FindCard(key) is { } card)
                foreach (var row in Descendants<Border>(card).Where(r => r.Tag is AttributeSelection a && a.AttributeName == columns[index].Name))
                { _selectedAttributeRow = row; row.Background = ThemeBrush("#266771"); row.BorderBrush = ThemeBrush("#48ADB4"); }
        }
        void Label(string text) => AttributePropertiesPanel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 12, 0, 5), Foreground = ThemeMuted });
        TextBox Field(string label, string value, Action<string>? change = null, bool multiline = false)
        {
            Label(label);
            var box = new TextBox { Text = value, IsReadOnly = readOnly || change is null, Padding = new Thickness(9), Background = ThemeBrush("#0E1824"), Foreground = ThemeInk, CaretBrush = ThemeInk, TextWrapping = TextWrapping.Wrap, AcceptsReturn = multiline, MinHeight = multiline ? 90 : 34 };
            if (change is not null) box.TextChanged += (_, _) => change(box.Text);
            AttributePropertiesPanel.Children.Add(box); return box;
        }
        Label("ATTRIBUTE — " + EntityDisplayName(key));
        Field("Name", selected.Name, value =>
        {
            if (string.IsNullOrWhiteSpace(value) || _columns[key].Any(c => c.Id != id && c.Name.Equals(value, StringComparison.OrdinalIgnoreCase))) return;
            Save(c => c with { Name = value }, true);
        });
        Field("Physical Name", selected.PhysicalName, value => Save(c => c with { PhysicalName = value }));
        Field("Description / Comment", selected.Description, value => Save(c => c with { Description = value }), true);
        Label("Datatype");
        var parts = SplitAttributeType(selected.Type);
        var type = new ComboBox { ItemsSource = new[] { parts.Name }.Concat(DatabaseTypes()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), SelectedItem = parts.Name, Padding = new Thickness(8), IsEnabled = !readOnly && !_columns[key].Any(c => c.ParentId == id), MaxDropDownHeight = 350 };
        AttributePropertiesPanel.Children.Add(type);
        var size = Field("Size / Precision", parts.Size);
        bool AcceptsSize(string name) => new[] { "CHAR", "VARCHAR", "NCHAR", "NVARCHAR", "VARCHAR2", "NVARCHAR2", "BINARY", "VARBINARY", "DECIMAL", "NUMERIC", "NUMBER", "FLOAT", "BIT", "BIT VARYING", "TIME", "TIMESTAMP", "DATETIME2", "DATETIMEOFFSET" }.Contains(name.ToUpperInvariant());
        size.IsReadOnly = readOnly || !type.IsEnabled || !AcceptsSize(parts.Name);
        size.ToolTip = "Length (100), precision and scale (10,2), or MAX where supported.";
        void SaveType()
        {
            if (type.SelectedItem is not string name) return;
            var parameter = size.Text.Trim();
            if (parameter.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(parameter, @"^(\d+(\s*,\s*\d+)?|MAX)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return;
            Save(c => c with { Type = name + (AcceptsSize(name) && parameter.Length > 0 ? "(" + parameter + ")" : "") }, true);
        }
        size.TextChanged += (_, _) => { if (!size.IsReadOnly) SaveType(); };
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedItem is not string name) return;
            size.IsReadOnly = readOnly || !type.IsEnabled || !AcceptsSize(name);
            size.Text = "";
            SaveType();
        };
        if (!type.IsEnabled && !readOnly) Label("Datatype is locked while this attribute has children.");
        Field("Kind", selected.IsPrimaryKey ? "PK" : selected.IsForeignKey ? "FK" : "COL");
        Field("Primary Key", selected.IsPrimaryKey ? "Yes" : "No");
        Label("Nullable");
        var nullable = new CheckBox { IsChecked = !selected.Required, Content = "Allow null", Foreground = ThemeInk, IsEnabled = !readOnly && !selected.IsPrimaryKey };
        nullable.Checked += (_, _) => Save(c => c with { Required = false });
        nullable.Unchecked += (_, _) => Save(c => c with { Required = true });
        AttributePropertiesPanel.Children.Add(nullable);
        Field("Foreign Key", selected.IsForeignKey ? "Yes" : "No");
        var add = new Button { Content = "Add Child", Margin = new Thickness(0, 12, 0, 0), IsEnabled = !readOnly && IsContainer(selected) };
        type.SelectionChanged += (_, _) => add.IsEnabled = !readOnly && IsContainer(Current());
        add.Click += (_, _) => { ColumnsGrid.SelectedItem = Current(); AddChild_Click(add, new RoutedEventArgs()); if (ColumnsGrid.SelectedItem is ColumnInfo child) ShowAttributeProperties(key, child); };
        AttributePropertiesPanel.Children.Add(add);
        foreach (FrameworkElement field in AttributePropertiesPanel.Children)
        {
            var margin = field.Margin;
            field.Margin = new Thickness(16, margin.Top, 16, margin.Bottom + 6);
        }
        add.Margin = new Thickness(16, 16, 16, 20);
    }
}
