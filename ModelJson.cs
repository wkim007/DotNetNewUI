using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;
using static ErwinStudioSample.ModelJsonDocument;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private JsonObject? _modelJsonSource;
    private readonly Dictionary<string, string> _modelObjectIds = [];
    private readonly Dictionary<Guid, string> _modelAttributeIds = [];
    private readonly Dictionary<string, string> _modelRelationshipIds = [];
    private readonly Dictionary<string, string> _objectPhysicalNames = [];
    private string ObjectJsonId(string key)
    {
        if (_modelObjectIds.TryGetValue(key, out var id)) return id;
        var reserved = ReservedIds(_modelJsonSource);
        reserved.UnionWith(_modelObjectIds.Values);
        reserved.UnionWith(_modelAttributeIds.Values);
        reserved.UnionWith(_modelRelationshipIds.Values);
        reserved.UnionWith(_subjectAreas.Select(a => a.Id));
        reserved.UnionWith(_diagrams.Select(d => d.Id));
        id = AllocateId(reserved);
        _modelObjectIds[key] = id;
        return id;
    }
    private string AttributeJsonId(Guid id) => _modelAttributeIds.GetValueOrDefault(id, id.ToString());

    private JsonObject ExportModelJson()
    {
        SaveActiveDiagram();
        var root = _modelJsonSource?.DeepClone() as JsonObject ?? new JsonObject();
        var workspace = root["workspace"] as JsonObject ?? new JsonObject();
        root["workspace"] = null; root["workspace"] = workspace;
        var meta = root["meta"] as JsonObject ?? new JsonObject();
        root["meta"] = null; root["meta"] = meta;
        meta["db"] ??= ProjectDatabaseBox.Text;
        meta["dbMajorVersion"] ??= ProjectVersionBox.Text.Split('.')[0];
        meta["dbMinorVersion"] ??= "0";
        meta["modelType"] ??= "3";
        meta["databaseName"] = ProjectDatabaseBox.Text;
        meta["databaseVersion"] = ProjectVersionBox.Text;
        meta["viewMode"] = ViewModeBox.SelectedIndex == 1 ? "logical" : "physical";
        meta["activeSubjectAreaId"] = _activeSubjectAreaId;
        meta["activeDiagramId"] = _activeDiagram?.Id;
        meta["nextDiagramSeq"] = _diagramNumber + 1;
        meta["nextSubjectAreaSeq"] = _subjectAreas.Count + 1;

        foreach (var kind in Kinds)
        {
            var original = Items(workspace[kind]).ToDictionary(o => Text(o, "id"));
            var objects = new JsonArray();
            var seen = new HashSet<string>();
            foreach (var diagram in _diagrams.OrderByDescending(d => d == _activeDiagram))
            foreach (var pair in diagram.Columns)
            {
                var key = pair.Key;
                var objectKind = diagram.MaterializedViews.Contains(key) ? "cachedViews" : diagram.Views.Contains(key) ? "views" : "entities";
                if (objectKind != kind || !diagram.Elements.OfType<Border>().Any(c => Equals(c.Tag, key))) continue;
                var id = ObjectJsonId(key);
                if (!seen.Add(id)) continue;
                var obj = original.GetValueOrDefault(id)?.DeepClone() as JsonObject ?? new JsonObject();
                obj["id"] = id; obj["name"] = EntityDisplayName(key);
                obj["physicalName"] = _objectPhysicalNames.GetValueOrDefault(key, Text(obj, "physicalName", key.ToLowerInvariant()));
                obj["definition"] = obj["comment"] = null;
                obj["definition"] = _objectDefinitions.GetValueOrDefault(key, "");
                obj["comment"] = _objectDefinitions.GetValueOrDefault(key, "");
                obj["physicalOnly"] ??= false; obj["logicalOnly"] ??= false;
                var previousAttributes = Items(obj["attributes"]).ToDictionary(a => Text(a, "id"));
                var attributes = new JsonArray();
                foreach (var c in pair.Value)
                {
                    var aid = AttributeJsonId(c.Id);
                    var attribute = previousAttributes.GetValueOrDefault(aid)?.DeepClone() as JsonObject ?? new JsonObject();
                    attribute["id"] = aid; attribute["name"] = c.Name; attribute["physicalName"] = c.PhysicalName;
                    attribute["datatype"] = c.Type; attribute["comment"] = c.Description; attribute["definition"] = c.Description;
                    attribute["isPrimary"] = c.IsPrimaryKey; attribute["isFK"] = c.IsForeignKey; attribute["isNullable"] = !c.Required;
                    attribute["parentId"] = c.ParentId is { } parent ? AttributeJsonId(parent) : null;
                    attribute["physicalOnly"] ??= false; attribute["logicalOnly"] ??= false;
                    attributes.Add(attribute);
                }
                obj["attributes"] = attributes;
                obj["indexes"] ??= new JsonArray();
                var validIds = pair.Value.Select(c => AttributeJsonId(c.Id)).ToHashSet();
                foreach (var index in Items(obj["indexes"]))
                    if (index["members"] is JsonArray members)
                        for (var i = members.Count - 1; i >= 0; i--)
                            if (!validIds.Contains(Text(members[i], "columnId"))) members.RemoveAt(i);
                objects.Add(obj);
            }
            // Workspace objects without placements still belong to the imported model.
            var originallyPlaced = Items(_modelJsonSource?["workspace"]?["subjectAreas"])
                .SelectMany(a => Items(a["diagrams"])).SelectMany(d => Items(d["modelShapes"]?[kind])).Select(s => Text(s, "id")).ToHashSet();
            foreach (var pair in original)
                if (!seen.Contains(pair.Key) && !originallyPlaced.Contains(pair.Key)) objects.Add(pair.Value.DeepClone());
            workspace[kind] = objects;
        }
        var originalRelations = Items(workspace["relationships"]).ToDictionary(r => Text(r, "id"));
        var relations = new JsonArray();
        var seenRelations = new HashSet<string>();
        foreach (var diagram in _diagrams.OrderByDescending(d => d == _activeDiagram))
        {
            foreach (var key in new[] { "CustomerOrder", "OrderProduct", "OrderOrderItem", "ProductOrderItem" }.Concat(diagram.Relationships.Keys))
            {
                if (diagram.Deleted.Contains(key)) continue;
                string source, target; bool identifying = false, view = false, subCategory = false;
                if (diagram.Relationships.TryGetValue(key, out var relation))
                { source = relation.SourceKey; target = relation.TargetKey; identifying = relation.IsIdentifying; view = relation.IsViewRelationship; subCategory = relation.IsSubCategory; }
                else
                {
                    if (!diagram.Elements.OfType<Path>().Any(p => Equals(p.Tag, key))) continue;
                    var info = GetRelationshipInfo(key); if (info is null) continue;
                    source = info.Value.Source; target = info.Value.Target;
                }
                var id = _modelRelationshipIds.GetValueOrDefault(key, key);
                if (!seenRelations.Add(id)) continue;
                var obj = originalRelations.GetValueOrDefault(id)?.DeepClone() as JsonObject ?? new JsonObject();
                obj["id"] = id; obj["name"] ??= EntityDisplayName(source) + " → " + EntityDisplayName(target);
                obj["parent"] = ObjectJsonId(source); obj["child"] = ObjectJsonId(target);
                obj["cardinality"] = diagram.Labels.GetValueOrDefault(key)?.Text ?? "1:N";
                obj["relationshipType"] ??= identifying ? "1" : "2";
                obj["isSubCategory"] = subCategory;
                if (relation?.InheritedAttributeIds is { } inheritedIds)
                    obj["inheritedAttributeIds"] = new JsonArray(inheritedIds.Select(a => (JsonNode?)JsonValue.Create(AttributeJsonId(a))).ToArray());
                obj["isView"] = view; obj["description"] ??= "";
                relations.Add(obj);
            }
        }
        var allObjectIds = Kinds.SelectMany(k => Items(workspace[k])).Select(o => Text(o, "id")).ToHashSet();
        foreach (var pair in originalRelations)
            if (!seenRelations.Contains(pair.Key) && allObjectIds.Contains(Text(pair.Value, "parent")) && allObjectIds.Contains(Text(pair.Value, "child")))
            {
                // Retain relationships never displayed together in any diagram.
                var displayed = _diagrams.Any(d => d.Columns.Keys.Any(k => ObjectJsonId(k) == Text(pair.Value, "parent")) && d.Columns.Keys.Any(k => ObjectJsonId(k) == Text(pair.Value, "child")));
                if (!displayed) relations.Add(pair.Value.DeepClone());
            }
        workspace["relationships"] = relations;
        var areas = new JsonArray();
        foreach (var area in _subjectAreas)
        {
            var originalArea = Items(_modelJsonSource?["workspace"]?["subjectAreas"]).FirstOrDefault(a => Text(a, "id") == area.Id);
            var a = originalArea?.DeepClone() as JsonObject ?? new JsonObject();
            a["id"] = area.Id; a["name"] = area.Name; a["locked"] = area.Locked;
            var diagrams = new JsonArray();
            foreach (var diagram in _diagrams.Where(d => d.SubjectAreaId == area.Id))
            {
                var d = Items(originalArea?["diagrams"]).FirstOrDefault(x => Text(x, "id") == diagram.Id)?.DeepClone() as JsonObject ?? new JsonObject();
                d["id"] = diagram.Id; d["name"] = diagram.Title;
                d["displayLevelLogical"] = diagram.DisplayLevelLogical; d["displayLevelPhysical"] = diagram.DisplayLevelPhysical;
                var shapes = new JsonObject();
                foreach (var kind in Kinds)
                {
                    var placements = new JsonArray();
                    foreach (var card in diagram.Elements.OfType<Border>())
                    {
                        if (card.Tag is not string key || !diagram.Columns.ContainsKey(key)) continue;
                        var objectKind = diagram.MaterializedViews.Contains(key) ? "cachedViews" : diagram.Views.Contains(key) ? "views" : "entities";
                        if (objectKind != kind) continue;
                        var placement = Items(d["modelShapes"]?[kind]).FirstOrDefault(p => Text(p, "id") == ObjectJsonId(key))?.DeepClone() as JsonObject ?? new JsonObject();
                        var overrides = diagram.DisplayOverrides.GetValueOrDefault(key, (-1, -1));
                        placement["displayLevelLogical"] = overrides.Item1; placement["displayLevelPhysical"] = overrides.Item2;
                        placement["id"] = ObjectJsonId(key); placement["name"] = EntityDisplayName(key);
                        placement["x"] = double.IsFinite(Canvas.GetLeft(card)) ? Canvas.GetLeft(card) : 0;
                        placement["y"] = double.IsFinite(Canvas.GetTop(card)) ? Canvas.GetTop(card) : 0;
                        placement["width"] = double.IsFinite(card.Width) ? card.Width : card.ActualWidth;
                        placement["height"] = double.IsFinite(card.Height) ? card.Height : card.ActualHeight;
                        if (_displayCards.TryGetValue(card, out var displayState) && displayState.LastLevel != 1)
                        { placement["width"] = displayState.Width; placement["height"] = displayState.Height; }
                        placements.Add(placement);
                    }
                    shapes[kind] = placements;
                }
                d["modelShapes"] = shapes; diagrams.Add(d);
            }
            a["diagrams"] = diagrams; areas.Add(a);
        }
        workspace["subjectAreas"] = areas;
        workspace["schemas"] ??= new JsonArray();
        return root;
    }

    private void ObjectPhysicalName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (EntityNameBox.Tag is string key && _columns.ContainsKey(key))
            _objectPhysicalNames[key] = PhysicalNameBox.Text;
    }

    private void ViewModelJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window { Title = "Model JSON", Owner = this, Width = 1050, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = ThemeBrush("#142230") };
        var root = new DockPanel { Margin = new Thickness(16) }; dialog.Content = root;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        header.Children.Add(new TextBlock { Text = "Model JSON", Foreground = ThemeInk, FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var status = new TextBlock { Foreground = ThemeMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var text = new TextBox { IsReadOnly = true, Text = ExportModelJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true }), FontFamily = new FontFamily("Consolas"), Background = ThemeBrush("#0E1824"), Foreground = ThemeInk, Padding = new Thickness(12), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(text);
        void Button(string label, Action action)
        {
            var button = new Button { Content = label, ToolTip = label, Style = (Style)FindResource("ProjectSettingsButton"), Margin = new Thickness(6, 0, 0, 0) };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { status.Text = ex.Message; } };
            actions.Children.Add(button);
        }
        Button("Copy", () => { Clipboard.SetText(text.Text); status.Text = "JSON copied."; });
        Button("Save", () =>
        {
            var picker = new SaveFileDialog { Title = "Save Model JSON", Filter = "JSON model|*.json|erwin JSON model|*.erwin_json", FileName = "model.json", AddExtension = true };
            if (picker.ShowDialog(dialog) != true) return;
            File.WriteAllText(picker.FileName, text.Text, new System.Text.UTF8Encoding(false));
            status.Text = "Saved " + picker.FileName;
        });
        Button("Open", () =>
        {
            if (_aiBusy) { status.Text = "Wait for the AI process to finish before importing a model."; return; }
            var picker = new OpenFileDialog { Title = "Import Model JSON", Filter = "JSON models|*.json;*.erwin_json|JSON|*.json|erwin JSON|*.erwin_json" };
            if (picker.ShowDialog(dialog) != true) return;
            if (new FileInfo(picker.FileName).Length > 50_000_000) throw new InvalidOperationException("Choose a model smaller than 50 MB.");
            var parsed = ModelJsonDocument.Parse(File.ReadAllText(picker.FileName));
            if (MessageBox.Show(dialog, "Import this model and replace the current workspace? Save the current model first if you need to keep it.", "Import Model", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            ImportModelJson(parsed);
            text.Text = ExportModelJson().ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            status.Text = $"Imported {_subjectAreas.Count} subject areas and {_diagrams.Count} diagrams.";
        });
        Button("Close", () => dialog.Close());
        dialog.ShowDialog();
    }
}
