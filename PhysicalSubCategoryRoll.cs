using System.Text.Json.Nodes;
using System.Windows.Controls;
using static ErwinStudioSample.ModelJsonDocument;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private JsonObject? _alternateViewModel;
    private int _rolledViewIndex;
    private bool _switchingRolledView;

    private void SyncNewObjectsToAlternateView(JsonObject current)
    {
        if (_alternateViewModel?["workspace"] is not JsonObject alternate ||
            _modelJsonSource?["workspace"] is not JsonObject baseline) return;
        // Ordinary shared objects remain synchronized after every view switch.
        // Subtype participants retain their independent rolled representations.
        var known = Kinds.SelectMany(k => Items(baseline[k])).Select(o => Text(o, "id")).ToHashSet();
        var shared = Kinds.SelectMany(k => Items(alternate[k])).Select(o => Text(o, "id")).ToHashSet();
        var rolledObjects = Items(current["workspace"]?["relationships"]).Concat(Items(alternate["relationships"]))
            .Where(r => Flag(r, "isSubCategory")).SelectMany(r => new[] { Text(r, "parent"), Text(r, "child") }).ToHashSet();
        var added = new HashSet<string>();
        foreach (var kind in Kinds)
        foreach (var obj in Items(current["workspace"]?[kind]).Where(o =>
            !known.Contains(Text(o, "id")) || shared.Contains(Text(o, "id")) && !rolledObjects.Contains(Text(o, "id"))))
        {
            var id = Text(obj, "id"); added.Add(id);
            var objects = alternate[kind] as JsonArray ?? new JsonArray();
            if (alternate[kind] is null) alternate[kind] = objects;
            var old = Items(objects).FirstOrDefault(o => Text(o, "id") == id);
            if (old is not null) objects[objects.IndexOf(old)] = obj.DeepClone();
            else objects.Add(obj.DeepClone());
        }
        if (added.Count == 0) return;
        foreach (var area in Items(current["workspace"]?["subjectAreas"]))
        {
            var targetArea = Items(alternate["subjectAreas"]).FirstOrDefault(a => Text(a, "id") == Text(area, "id"));
            if (targetArea is null) continue;
            foreach (var diagram in Items(area["diagrams"]))
            {
                var targetDiagram = Items(targetArea["diagrams"]).FirstOrDefault(d => Text(d, "id") == Text(diagram, "id"));
                if (targetDiagram is null) continue;
                foreach (var kind in Kinds)
                foreach (var shape in Items(diagram["modelShapes"]?[kind]).Where(s => added.Contains(Text(s, "id"))))
                {
                    var shapes = targetDiagram["modelShapes"]![kind]!.AsArray();
                    var old = Items(shapes).FirstOrDefault(s => Text(s, "id") == Text(shape, "id"));
                    if (old is not null) shapes[shapes.IndexOf(old)] = shape.DeepClone();
                    else shapes.Add(shape.DeepClone());
                }
            }
        }
    }

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
