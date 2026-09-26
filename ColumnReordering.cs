using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private bool CanReorderAttribute(Border target, DragEventArgs e, out AttributeDragData? drag, out int sourceIndex, out int targetIndex)
    {
        drag = e.Data.GetData(typeof(AttributeDragData)) as AttributeDragData;
        sourceIndex = targetIndex = -1;
        if (drag is null || target.Tag is not AttributeSelection selection || selection.EntityKey != drag.EntityKey ||
            drag.Row == target || target.Parent is not StackPanel || target.Parent != drag.Row.Parent ||
            !_columns.TryGetValue(selection.EntityKey, out var columns)) return false;
        var sourceName = drag.AttributeName;
        sourceIndex = columns.ToList().FindIndex(c => c.Name == sourceName);
        targetIndex = columns.ToList().FindIndex(c => c.Name == selection.AttributeName);
        // Reordering preserves PK membership; the divider remains the explicit PK toggle.
        return sourceIndex >= 0 && targetIndex >= 0 && columns[sourceIndex].IsPrimaryKey == columns[targetIndex].IsPrimaryKey;
    }

    private void ClearReorderHint(Border row)
    {
        row.BorderThickness = new Thickness(1);
        row.BorderBrush = row == _selectedAttributeRow
            ? new SolidColorBrush(Color.FromRgb(72, 173, 180)) : Brushes.Transparent;
    }

    private void AttributeReorder_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border row) return;
        var valid = CanReorderAttribute(row, e, out _, out _, out _);
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        ClearReorderHint(row);
        if (valid)
        {
            var after = e.GetPosition(row).Y >= row.ActualHeight / 2;
            row.BorderBrush = new SolidColorBrush(Color.FromRgb(98, 230, 179));
            row.BorderThickness = after ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
        }
        e.Handled = true;
    }

    private void AttributeReorder_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border row) return;
        var after = e.GetPosition(row).Y >= row.ActualHeight / 2;
        ClearReorderHint(row);
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!CanReorderAttribute(row, e, out var drag, out var sourceIndex, out var targetIndex) || drag is null) return;
        var destination = targetIndex + (after ? 1 : 0);
        if (sourceIndex < destination) destination--;
        _columns[drag.EntityKey].Move(sourceIndex, destination);
        var fields = (StackPanel)row.Parent;
        fields.Children.Remove(drag.Row);
        fields.Children.Insert(fields.Children.IndexOf(row) + (after ? 1 : 0), drag.Row);
        e.Effects = DragDropEffects.Move;
        StatusText.Text = $"Moved {drag.AttributeName} {(after ? "after" : "before")} {((AttributeSelection)row.Tag).AttributeName}";
    }
}
