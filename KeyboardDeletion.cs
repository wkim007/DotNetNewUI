using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private void DiagramDelete_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None || _inlineRenameActive) return;
        // Never treat text editing or a properties-grid edit as a diagram deletion.
        for (var node = Keyboard.FocusedElement as DependencyObject; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is TextBoxBase or PasswordBox or ComboBox or DataGrid) return;
        var keys = _selectedCards.Where(c => DiagramCanvasSurface.Children.Contains(c)).Select(c => c.Tag as string)
            .Where(k => k is not null && FindCard(k) is not null).Cast<string>().Distinct().ToArray();
        if (keys.Length == 0) return;
        // Deletion can clear selection; snapshot every target before removing anything.
        _entityHoldTimer.Stop(); _entityHoldCard = null; _draggedCard = null;
        EndMarquee(); Mouse.Capture(null); _groupDragOrigins.Clear();
        foreach (var key in keys) DeleteObject(key);
        ClearAttributeSelection(); ClearCardSelection();
        AttributePropertiesPanel.Visibility = Visibility.Collapsed;
        EntityPropertiesPanel.Visibility = Visibility.Collapsed;
        SaveActiveDiagram();
        StatusText.Text = $"Deleted {keys.Length} selected object(s).";
        e.Handled = true;
    }
}
