using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;
using static ErwinStudioSample.ModelJsonDocument;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private void ImportModelJson(JsonObject root)
    {
        var workspace = root["workspace"]!;
        var areas = Items(workspace["subjectAreas"]).Select(a => new SubjectAreaState {
            Id = Text(a, "id"), Name = Text(a, "name"), Locked = Text(a, "id") == "0" || Flag(a, "locked") }).ToList();
        var objectIds = new Dictionary<string, string>();
        var attributeIds = new Dictionary<Guid, string>();
        var relationIds = new Dictionary<string, string>();
        var names = new Dictionary<string, string>();
        var physicalNames = new Dictionary<string, string>();
        var definitions = new Dictionary<string, string>();
        var objects = new Dictionary<string, (string Key, string Kind, ObservableCollection<ColumnInfo> Columns)>();
        foreach (var kind in Kinds)
        foreach (var obj in Items(workspace[kind]))
        {
            var id = Text(obj, "id");
            var key = "Imported_" + id;
            objectIds[key] = id; names[key] = Text(obj, "name");
            physicalNames[key] = Text(obj, "physicalName");
            definitions[key] = Text(obj, "definition", Text(obj, "comment"));
            var ids = Items(obj["attributes"]).ToDictionary(a => Text(a, "id"), a => Guid.TryParse(Text(a, "id"), out var guid) ? guid : Guid.NewGuid());
            var columns = new ObservableCollection<ColumnInfo>();
            foreach (var a in Items(obj["attributes"]))
            {
                var aid = ids[Text(a, "id")];
                attributeIds[aid] = Text(a, "id");
                columns.Add(new ColumnInfo(Text(a, "name"), Text(a, "datatype", "VARCHAR"), !Flag(a, "isNullable", true), Flag(a, "isPrimary"), Flag(a, "isFK"))
                {
                    Id = aid, ParentId = ids.TryGetValue(Text(a, "parentId"), out var parent) ? parent : null,
                    PhysicalName = Text(a, "physicalName"), Description = Text(a, "definition", Text(a, "comment"))
                });
            }
            objects[id] = (key, kind, columns);
        }
        var diagrams = new List<DiagramState>();
        foreach (var area in Items(workspace["subjectAreas"]))
        foreach (var source in Items(area["diagrams"]))
        {
            var diagram = new DiagramState { Id = Text(source, "id"), Title = Text(source, "name", "Diagram"), SubjectAreaId = Text(area, "id") };
            var placements = Kinds.SelectMany(k => Items(source["modelShapes"]?[k])).ToArray();
            var arrange = placements.Length > 1 && placements.All(p => Number(p, "x") == 0 && Number(p, "y") == 0);
            var across = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(placements.Length)));
            var rowHeight = 0d; var y = 50d; var index = 0;
            foreach (var placement in placements)
            {
                var obj = objects[Text(placement, "id")];
                diagram.Columns[obj.Key] = obj.Columns;
                if (obj.Kind == "entities") diagram.Entities.Add(obj.Key);
                else { diagram.Views.Add(obj.Key); if (obj.Kind == "cachedViews") diagram.MaterializedViews.Add(obj.Key); }
                var width = Math.Clamp(Number(placement, "width", 300), 200, 10000);
                var height = Math.Max(Math.Clamp(Number(placement, "height"), 0, 10000), 85 + obj.Columns.Count * 29);
                var card = CreateInteractiveCard(obj.Key, width, height);
                var grid = new Grid { Background = Brushes.Transparent };
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.Children.Add(new Border { Background = ThemeBrush(obj.Kind == "entities" ? "#0D8B97" : "#202B3A"), CornerRadius = new CornerRadius(6, 6, 0, 0),
                    Child = new TextBlock { Text = names[obj.Key], Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center } });
                var fields = new StackPanel { Margin = new Thickness(10, 7, 10, 7) }; Grid.SetRow(fields, 1); grid.Children.Add(fields);
                grid.Children.Add(CreateResizeThumb(obj.Key)); card.Child = grid;
                Panel.SetZIndex(card, 8);
                if (arrange && index > 0 && index % across == 0) { y += rowHeight + 80; rowHeight = 0; }
                Canvas.SetLeft(card, arrange ? 50 + index % across * 390 : Number(placement, "x"));
                Canvas.SetTop(card, arrange ? y : Number(placement, "y"));
                rowHeight = Math.Max(rowHeight, height); index++;
                diagram.Cards[obj.Key] = card; diagram.Elements.Add(card);
            }
            foreach (var r in Items(workspace["relationships"]))
            {
                var from = objects[Text(r, "parent")].Key; var to = objects[Text(r, "child")].Key;
                if (!diagram.Columns.ContainsKey(from) || !diagram.Columns.ContainsKey(to)) continue;
                var key = "ImportedRelationship_" + Text(r, "id"); relationIds[key] = Text(r, "id");
                var hit = new Path { Tag = key, Stroke = Brushes.Transparent, StrokeThickness = 16, Cursor = Cursors.SizeAll };
                hit.MouseLeftButtonDown += Relationship_MouseLeftButtonDown;
                hit.MouseMove += RelationshipHit_MouseMove; hit.MouseLeftButtonUp += RelationshipHit_MouseLeftButtonUp;
                var view = Flag(r, "isView") || Flag(r, "isCachedView");
                var identifying = Text(r, "relationshipType") == "1";
                var line = new Path { Tag = key, Stroke = ThemeBrush("#42D9D4"), StrokeThickness = 2.5, IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
                if (!identifying) line.StrokeDashArray = new DoubleCollection(view ? new double[] { .1, 2.4 } : new double[] { 2, 1.5 });
                diagram.Relationships[key] = new DynamicRelationship(from, to, line, hit, identifying, view);
                diagram.Elements.Add(hit); diagram.Elements.Add(line);
                var label = new TextBlock { Tag = key, Text = Text(r, "cardinality", "1:N"), Foreground = ThemeBrush("#B9DCF4"), Background = ThemeBrush("#0D1520"), FontSize = 12, Padding = new Thickness(4, 1, 4, 1), IsHitTestVisible = false };
                Panel.SetZIndex(label, 6); diagram.Labels[key] = label; diagram.Elements.Add(label);
            }
            diagrams.Add(diagram);
        }

        // All definitions, references and cards are staged before the live workspace changes.
        SaveActiveDiagram();
        var oldDiagrams = _diagrams.ToList(); var oldAreas = _subjectAreas.ToList(); var oldActive = _activeDiagram;
        var oldAreaId = _activeSubjectAreaId; var oldSource = _modelJsonSource;
        var oldNames = new Dictionary<string, string>(_entityDisplayNames); var oldDefinitions = new Dictionary<string, string>(_objectDefinitions);
        var oldPhysical = new Dictionary<string, string>(_objectPhysicalNames);
        var oldObjectIds = new Dictionary<string, string>(_modelObjectIds); var oldAttributeIds = new Dictionary<Guid, string>(_modelAttributeIds); var oldRelationIds = new Dictionary<string, string>(_modelRelationshipIds);
        var target = diagrams.FirstOrDefault(d => d.Id == Text(root["meta"], "activeDiagramId") && d.SubjectAreaId == Text(root["meta"], "activeSubjectAreaId"))
            ?? diagrams.FirstOrDefault(d => d.SubjectAreaId == Text(root["meta"], "activeSubjectAreaId")) ?? diagrams.First();
        try
        {
            _modelJsonSource = root.DeepClone() as JsonObject;
            RestoreMap(_entityDisplayNames, names); RestoreMap(_objectDefinitions, definitions); RestoreMap(_objectPhysicalNames, physicalNames);
            RestoreMap(_modelObjectIds, objectIds); RestoreMap(_modelAttributeIds, attributeIds); RestoreMap(_modelRelationshipIds, relationIds);
            _diagrams.Clear(); _diagrams.AddRange(diagrams);
            _activeSubjectAreaId = target.SubjectAreaId;
            _subjectAreas.Clear(); foreach (var area in areas) _subjectAreas.Add(area);
            ProjectSubjectBox.SelectedItem = areas.First(a => a.Id == target.SubjectAreaId);
            SwitchDiagram(target);
            _diagramNumber = Math.Max(diagrams.Count, (int)Number(root["meta"], "nextDiagramSeq", diagrams.Count + 1) - 1);
            if (!string.IsNullOrWhiteSpace(Text(root["meta"], "databaseName"))) ProjectDatabaseBox.Text = Text(root["meta"], "databaseName");
            if (!string.IsNullOrWhiteSpace(Text(root["meta"], "databaseVersion"))) ProjectVersionBox.Text = Text(root["meta"], "databaseVersion");
            ViewModeBox.SelectedIndex = Text(root["meta"], "viewMode") == "logical" ? 1 : 0;
            SaveActiveDiagram();
            StatusText.Text = $"Imported {objects.Count} objects, {diagrams.Count} diagrams and {areas.Count} subject areas.";
        }
        catch
        {
            _modelJsonSource = oldSource;
            RestoreMap(_entityDisplayNames, oldNames); RestoreMap(_objectDefinitions, oldDefinitions); RestoreMap(_objectPhysicalNames, oldPhysical);
            RestoreMap(_modelObjectIds, oldObjectIds); RestoreMap(_modelAttributeIds, oldAttributeIds); RestoreMap(_modelRelationshipIds, oldRelationIds);
            _diagrams.Clear(); _diagrams.AddRange(oldDiagrams); _activeSubjectAreaId = oldAreaId;
            _subjectAreas.Clear(); foreach (var area in oldAreas) _subjectAreas.Add(area);
            ProjectSubjectBox.SelectedItem = oldAreas.First(a => a.Id == oldAreaId);
            if (oldActive is not null) SwitchDiagram(oldActive);
            throw;
        }
    }

    private void RefreshImportedDiagram()
    {
        foreach (var key in _columns.Keys.Where(_modelObjectIds.ContainsKey).ToArray())
        {
            if (FindCard(key) is not { } card) continue;
            if (card.Child is Grid grid && grid.Children.OfType<Border>().FirstOrDefault() is { } header && Descendants<TextBlock>(header).FirstOrDefault() is { } title) title.Text = EntityDisplayName(key);
            EnsureCloseButton(card, key);
            RebuildNestedRows(key);
        }
        DiagramCanvasSurface.MinWidth = Math.Max(930, _dynamicCards.Values.Select(c => Canvas.GetLeft(c) + c.Width + 80).DefaultIfEmpty(930).Max());
        DiagramCanvasSurface.MinHeight = Math.Max(650, _dynamicCards.Values.Select(c => Canvas.GetTop(c) + c.Height + 80).DefaultIfEmpty(650).Max());
    }
}
