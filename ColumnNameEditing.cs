using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private void ColumnNameEditor_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox editor) editor.Tag = editor.Text;
    }

    private void ColumnNameEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { IsLoaded: true, Tag: string previous, DataContext: ColumnInfo column } editor ||
            ColumnsGrid.IsReadOnly || EntityNameBox.Tag is not string key || !_columns.TryGetValue(key, out var columns)) return;
        var name = editor.Text;
        if (string.IsNullOrWhiteSpace(name) || columns.Any(c => c.Id != column.Id && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            editor.ToolTip = "Enter a nonempty, unique column name.";
            return;
        }
        // Explicit updates keep invalid intermediate text out of the model.
        editor.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        editor.ToolTip = null;
        if (FindCard(key) is { } card)
        {
            foreach (var row in Descendants<Border>(card).Where(r => r.Tag is AttributeSelection selection && selection.AttributeName == previous))
            {
                row.Tag = new AttributeSelection(key, name);
                if (row.Child is Grid layout && layout.Children.OfType<TextBlock>().FirstOrDefault(t => Grid.GetColumn(t) == 1) is { } label)
                { label.Text = name; label.ToolTip = name; }
            }
        }
        editor.Tag = name;
    }

    private void ColumnNameEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox editor) editor.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }
}
