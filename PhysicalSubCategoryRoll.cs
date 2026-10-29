using System.Text.Json.Nodes;
using System.Windows.Controls;
using static ErwinStudioSample.ModelJsonDocument;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private JsonObject? _alternateViewModel;
    private int _rolledViewIndex;
    private bool _switchingRolledView;

    private void SwitchRolledView()
    {
        if (_switchingRolledView || _alternateViewModel is null || ViewModeBox.SelectedIndex == _rolledViewIndex) return;
        var requested = ViewModeBox.SelectedIndex;
        _switchingRolledView = true;
        try
        {
            var current = ExportModelJson();
            current["meta"]!["viewMode"] = _rolledViewIndex == 1 ? "logical" : "physical";
            var next = (JsonObject)_alternateViewModel.DeepClone();
            next["meta"]!["viewMode"] = requested == 1 ? "logical" : "physical";
            next["meta"]!["activeDiagramId"] = _activeDiagram?.Id;
            next["meta"]!["activeSubjectAreaId"] = _activeSubjectAreaId;
            ImportModelJson(Parse(next.ToJsonString()));
            _alternateViewModel = current;
            _rolledViewIndex = requested;
        }
        finally { _switchingRolledView = false; }
    }

    private void RollPhysicalSubCategory(string sourceKey, bool up, string? branchKey = null)
    {
        var originalMode = ViewModeBox.SelectedIndex;
        var sourceId = ObjectJsonId(sourceKey);
        var childId = branchKey is not null && _dynamicRelationships.TryGetValue(branchKey, out var branch)
            ? ObjectJsonId(branch.TargetKey) : null;
        var previousPhysical = originalMode == 1 ? _alternateViewModel : null;
        // Build a separate object graph so collections and attribute records
        // in the logical model cannot be changed by physical transformations.
        if (originalMode == 1 || _alternateViewModel is null)
        {
            var original = ExportModelJson();
            // Every logical-view request starts from today's logical structure,
            // not a previous physical roll that may have removed the parent.
            original.Remove("alternateViewModel");
            _alternateViewModel = (JsonObject)original.DeepClone();
            _rolledViewIndex = originalMode;
            if (originalMode == 0)
            {
                _alternateViewModel["meta"]!["viewMode"] = "logical";
                _switchingRolledView = true;
                try { ImportModelJson(Parse(original.ToJsonString())); }
                finally { _switchingRolledView = false; }
            }
        }
        ViewModeBox.SelectedIndex = 0;
        var key = _modelObjectIds.FirstOrDefault(p => p.Value == sourceId).Key;
        var branchMatch = childId is null ? null : _dynamicRelationships.FirstOrDefault(p =>
            p.Value.SourceKey == key && ObjectJsonId(p.Value.TargetKey) == childId).Key;
        var success = key is not null && (childId is null || branchMatch is not null) && RollSubCategoryCore(key, up, branchMatch);
        var message = success ? (up ? "RollUp Physical" : "RollDown Physical") + " completed. Switch to Physical View to see the result."
            : "Physical roll could not be applied. " + StatusText.Text;
        if (originalMode == 1) ViewModeBox.SelectedIndex = 1;
        if (!success && originalMode == 1) _alternateViewModel = previousPhysical;
        StatusText.Text = message;
    }

    private void AddPhysicalRollMenus(ContextMenu menu, MenuItem styleSource, string sourceKey, string? branchKey = null)
    {
        var up = new MenuItem { Header = "RollUp Physical", Template = styleSource.Template, Padding = styleSource.Padding };
        var down = new MenuItem { Header = "RollDown Physical", Template = styleSource.Template, Padding = styleSource.Padding };
        menu.Items.Insert(2, up); menu.Items.Insert(3, down);
        up.Click += (_, _) => RollPhysicalSubCategory(sourceKey, true, branchKey);
        down.Click += (_, _) => RollPhysicalSubCategory(sourceKey, false, branchKey);
    }
}
