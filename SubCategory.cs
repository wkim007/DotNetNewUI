using System.Windows;
using System.Windows.Input;
namespace ErwinStudioSample;
public partial class MainWindow
{
    private void UpdateDiagramTools()
    {
        if (SubCategoryToolButton is null) return;
        var logical = ViewModeBox.SelectedIndex == 1;
        ViewToolButton.Visibility = MaterializedViewToolButton.Visibility = ViewRelationshipToolButton.Visibility = logical ? Visibility.Collapsed : Visibility.Visible;
        SubCategoryToolButton.Visibility = logical ? Visibility.Visible : Visibility.Collapsed;
        if ((logical && _pendingRelationshipType == "View relationship") || (!logical && _pendingRelationshipType == "Sub-Category"))
        { _pendingRelationshipType = null; _relationshipSourceKey = null; }
    }
    private bool HandleSubCategoryClick(string key, MouseButtonEventArgs e)
    {
        if (_pendingRelationshipType != "Sub-Category") return false;
        e.Handled = true;
        if (ViewModeBox.SelectedIndex != 1 || _relationshipSourceKey is not { } source || !_entityCardKeys.Contains(source) || !_entityCardKeys.Contains(key) || source == key)
        { StatusText.Text = "Choose a different target entity for the Sub-Category"; return true; }
        CreateDynamicRelationship(source, key, "Sub-Category");
        _pendingRelationshipType = null; _relationshipSourceKey = null;
        return true;
    }
}
