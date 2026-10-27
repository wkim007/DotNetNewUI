using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using ErwinStudioSample;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class TestPresentationSource : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new App(); app.InitializeComponent();
        var window = new MainWindow();
        object? Call(string name, params object[] values)
        {
            try { return typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, values); }
            catch (TargetInvocationException ex) { throw ex.InnerException!; }
        }
        JsonObject Parse(string text)
        {
            try { return (JsonObject)typeof(MainWindow).Assembly.GetType("ErwinStudioSample.ModelJsonDocument")!.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [text])!; }
            catch (TargetInvocationException ex) { throw ex.InnerException!; }
        }
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Call("InitializeDiagrams");
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, Private | BindingFlags.Public)!.GetValue(window)!;
        var displayBox = Field<System.Windows.Controls.ComboBox>("DiagramDisplayLevelBox");
        var viewBox = Field<System.Windows.Controls.ComboBox>("ViewModeBox");
        var databaseBox = Field<System.Windows.Controls.ComboBox>("ProjectDatabaseBox");
        var orderCard = (System.Windows.Controls.Border)Call("FindCard", "Order")!;
        Call("PrepareAttributeRows", orderCard, "Order");
        var orderFields = ((System.Windows.Controls.Grid)orderCard.Child).Children.OfType<System.Windows.Controls.StackPanel>().First();
        void SetLevel(int level)
        {
            displayBox.SelectedItem = displayBox.Items.Cast<object>().First(o => (int)o.GetType().GetProperty("Value")!.GetValue(o)! == level);
        }
        void CheckConnected()
        {
            foreach (var (key, sourceKey, targetKey) in new[] {
                ("CustomerOrder", "Customer", "Order"), ("OrderProduct", "Order", "Product"),
                ("OrderOrderItem", "Order", "OrderItem"), ("ProductOrderItem", "Product", "OrderItem") })
            {
                var line = (System.Windows.Shapes.Path)Call("FindRelationshipLine", key)!;
                var geometry = (System.Windows.Media.PathGeometry)line.Data;
                var figure = geometry.Figures[0];
                var end = ((System.Windows.Media.LineSegment)figure.Segments[^1]).Point;
                void OnBoundary(string objectKey, Point point)
                {
                    var card = (System.Windows.Controls.Border)Call("FindCard", objectKey)!;
                    var left = System.Windows.Controls.Canvas.GetLeft(card); var top = System.Windows.Controls.Canvas.GetTop(card);
                    var right = left + card.Width; var bottom = top + card.Height;
                    const double epsilon = 0.01;
                    Check(point.X >= left - epsilon && point.X <= right + epsilon && point.Y >= top - epsilon && point.Y <= bottom + epsilon &&
                        (Math.Abs(point.X - left) < epsilon || Math.Abs(point.X - right) < epsilon || Math.Abs(point.Y - top) < epsilon || Math.Abs(point.Y - bottom) < epsilon),
                        key + " disconnected from " + objectKey);
                }
                OnBoundary(sourceKey, figure.StartPoint);
                OnBoundary(targetKey, end);
            }
        }
        int VisibleRows() => orderFields.Children.OfType<System.Windows.Controls.Border>().Count(b => b.Tag?.GetType().Name == "AttributeSelection" && b.Visibility == Visibility.Visible);
        Call("ApplyProjectViewMode");
        Check((string)displayBox.SelectedItem!.GetType().GetProperty("Name")!.GetValue(displayBox.SelectedItem)! == "Column", "Default display is not Column.");
        SetLevel(5); Check(VisibleRows() == 1, "PrimaryKey must show only primary keys.");
        SetLevel(12); Check(VisibleRows() == 2, "Key must show PK and FK.");
        SetLevel(0); CheckConnected(); Check(orderFields.Visibility == Visibility.Collapsed && orderCard.Height == 40, "Table mode must hide attributes.");
        SetLevel(1); CheckConnected(); Check(VisibleRows() == 4 && orderFields.Visibility == Visibility.Visible, "Column mode did not restore rows.");
        SetLevel(3); CheckConnected(); Check(orderFields.Visibility == Visibility.Collapsed, "Comment mode must hide columns.");
        viewBox.SelectedIndex = 1;
        Check((string)displayBox.SelectedItem!.GetType().GetProperty("Name")!.GetValue(displayBox.SelectedItem)! == "Attribute", "Logical level must be independent.");
        SetLevel(7); Check(orderFields.Visibility == Visibility.Collapsed, "Icon mode must hide rows.");
        Check(!displayBox.Items.Cast<object>().Any(o => (int)o.GetType().GetProperty("Value")!.GetValue(o)! is 6 or 13), "Logical SQL options include Order or Graph.");
        viewBox.SelectedIndex = 0;
        Check((int)displayBox.SelectedItem!.GetType().GetProperty("Value")!.GetValue(displayBox.SelectedItem)! == 3, "Physical level not remembered.");
        databaseBox.Text = "Neo4j"; Call("ApplyProjectViewMode");
        SetLevel(13); CheckConnected(); Check(orderCard.Width == 120 && orderCard.Height == 120, "Graph mode must render a node.");
        databaseBox.Text = "SQL Server"; Call("ApplyProjectViewMode");
        Check(!displayBox.Items.Cast<object>().Any(o => (int)o.GetType().GetProperty("Value")!.GetValue(o)! == 13), "Graph visible outside Neo4j.");
        SetLevel(1);
        var current = (JsonObject)Call("ExportModelJson")!;
        Parse(current.ToJsonString());
        Check(((JsonArray)current["workspace"]!["entities"]!).Count == 4, "Default entities not exported.");
        Check(((JsonArray)current["workspace"]!["relationships"]!).Count == 4, "Default relationships not exported.");
        var defaultIds = ((JsonArray)current["workspace"]!["entities"]!).Select(o => o!["id"]!.ToString()).ToArray();
        Check(defaultIds.All(id => id.All(char.IsAsciiDigit)) && defaultIds.Distinct().Count() == defaultIds.Length, "Object IDs must be unique numeric strings.");
        var names = (Dictionary<string, string>)typeof(MainWindow).GetField("_entityDisplayNames", Private)!.GetValue(window)!;
        names["Customer"] = "SameName"; names["Order"] = "SameName";
        var renamed = (JsonObject)Call("ExportModelJson")!;
        Check(defaultIds.SequenceEqual(((JsonArray)renamed["workspace"]!["entities"]!).Select(o => o!["id"]!.ToString())), "Renaming changed IDs.");
        Parse(renamed.ToJsonString()); // Checks numeric relationship and placement references.
        var legacy = (JsonObject)current.DeepClone();
        var firstId = legacy["workspace"]!["entities"]![0]!["id"]!.ToString();
        legacy["workspace"]!["entities"]![0]!["id"] = "LegacyCustomer";
        foreach (var r in ((JsonArray)legacy["workspace"]!["relationships"]!).OfType<JsonObject>())
            foreach (var field in new[] { "parent", "child" })
                if (r[field]!.ToString() == firstId) r[field] = "LegacyCustomer";
        foreach (var a in ((JsonArray)legacy["workspace"]!["subjectAreas"]!).OfType<JsonObject>())
        foreach (var d in ((JsonArray)a["diagrams"]!).OfType<JsonObject>())
        foreach (var shape in ((JsonArray)d["modelShapes"]!["entities"]!).OfType<JsonObject>())
            if (shape["id"]!.ToString() == firstId) shape["id"] = "LegacyCustomer";
        var migrated = Parse(legacy.ToJsonString());
        Check(migrated["workspace"]!["entities"]![0]!["id"]!.ToString().All(char.IsAsciiDigit), "Legacy text ID not migrated.");
        var source = Parse(System.IO.File.ReadAllText(args[0]));
        Call("ImportModelJson", source);
        var levelSource = (JsonObject)source.DeepClone();
        var firstDiagram = levelSource["workspace"]!["subjectAreas"]![0]!["diagrams"]![0]!;
        firstDiagram["displayLevelPhysical"] = 5; firstDiagram["displayLevelLogical"] = 3;
        firstDiagram["modelShapes"]!["entities"]![0]!["displayLevelPhysical"] = 0;
        Call("ImportModelJson", Parse(levelSource.ToJsonString()));
        var levelsExport = (JsonObject)Call("ExportModelJson")!;
        var levelsDiagram = levelsExport["workspace"]!["subjectAreas"]![0]!["diagrams"]![0]!;
        Check(levelsDiagram["displayLevelPhysical"]!.GetValue<int>() == 5 && levelsDiagram["displayLevelLogical"]!.GetValue<int>() == 3, "Diagram display levels lost.");
        Check(levelsDiagram["modelShapes"]!["entities"]![0]!["displayLevelPhysical"]!.GetValue<int>() == 0, "Object override lost.");
        Call("ImportModelJson", source);
        var exported = (JsonObject)Call("ExportModelJson")!;
        Parse(exported.ToJsonString());
        foreach (var kind in new[] { "entities", "views", "cachedViews", "relationships", "subjectAreas", "schemas" })
            Check(((JsonArray)source["workspace"]![kind]!).Count == ((JsonArray)exported["workspace"]![kind]!).Count, kind + " count changed.");
        foreach (var obj in ((JsonArray)source["workspace"]!["entities"]!).OfType<JsonObject>())
        {
            var copy = ((JsonArray)exported["workspace"]!["entities"]!).OfType<JsonObject>().First(o => o["id"]!.ToString() == obj["id"]!.ToString());
            Check(JsonNode.DeepEquals(obj["indexes"], copy["indexes"]), "Index metadata changed.");
            Check(JsonNode.DeepEquals(obj["props"], copy["props"]), "Object metadata changed.");
            Check(((JsonArray)obj["attributes"]!).Count == ((JsonArray)copy["attributes"]!).Count, "Attributes lost.");
        }
        var diagrams = (System.Collections.IEnumerable)typeof(MainWindow).GetField("_diagrams", Private)!.GetValue(window)!;
        foreach (var diagram in diagrams) Call("SwitchDiagram", diagram);
        // The UI dropdown normally sets the active subject; switch back through import for round-trip assertions.
        Call("ImportModelJson", Parse(exported.ToJsonString()));
        var second = (JsonObject)Call("ExportModelJson")!;
        Check(JsonNode.DeepEquals(exported["workspace"], second["workspace"]), "Import/export round trip changed the workspace.");
        var savedPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "model-json-test-" + Guid.NewGuid() + ".erwin_json");
        try
        {
            System.IO.File.WriteAllText(savedPath, second.ToJsonString());
            Check(JsonNode.DeepEquals(Parse(System.IO.File.ReadAllText(savedPath)), second), "Saved file changed JSON.");
        }
        finally { System.IO.File.Delete(savedPath); }
        var badShape = (JsonObject)source.DeepClone();
        badShape["workspace"]!["subjectAreas"]![0]!["diagrams"]![0]!["modelShapes"]!["entities"]![0]!["id"] = "missing";
        try { Parse(badShape.ToJsonString()); throw new Exception("Missing placement reference accepted."); }
        catch (InvalidOperationException) { }
        var bad = (JsonObject)source.DeepClone();
        bad["workspace"]!["relationships"]![0]!["parent"] = "missing";
        try { Parse(bad.ToJsonString()); throw new Exception("Missing reference accepted."); }
        catch (InvalidOperationException) { }
        Check(JsonNode.DeepEquals(second["workspace"], ((JsonObject)Call("ExportModelJson")!)["workspace"]), "Validation failure changed model.");
        // Exercise materialized-view reconstruction and metadata preservation.
        var materialized = (JsonObject)source.DeepClone();
        var mv = (JsonObject)materialized["workspace"]!["views"]![0]!.DeepClone(); mv["id"] = "test-mv";
        ((JsonArray)materialized["workspace"]!["cachedViews"]!).Add(mv);
        ((JsonArray)materialized["workspace"]!["subjectAreas"]![0]!["diagrams"]![0]!["modelShapes"]!["cachedViews"]!).Add(new JsonObject { ["id"] = "test-mv", ["x"] = 400, ["y"] = 100, ["width"] = 300, ["height"] = 200 });
        Call("ImportModelJson", Parse(materialized.ToJsonString()));
        var mvOutput = (JsonObject)Call("ExportModelJson")!;
        Check(((JsonArray)mvOutput["workspace"]!["cachedViews"]!).Count == 1, "Materialized view lost.");
        Call("ImportModelJson", source);
        JsonObject CurrentModel() => (JsonObject)Call("ExportModelJson")!;
        JsonObject AreaDiagram(JsonObject model, string areaName) =>
            ((JsonArray)model["workspace"]!["subjectAreas"]!).OfType<JsonObject>()
                .First(a => a["name"]!.ToString() == areaName)["diagrams"]!.AsArray()[0]!.AsObject();
        bool HasMovieCopy(JsonObject model, string areaName) =>
            AreaDiagram(model, areaName)["modelShapes"]!["entities"]!.AsArray().OfType<JsonObject>().Any(s => s["id"]!.ToString() == "160");
        bool SameDefinitions(JsonObject a, JsonObject b)
        {
            var left = a["workspace"]!["entities"]!.AsArray().OfType<JsonObject>().ToDictionary(o => o["id"]!.ToString());
            var right = b["workspace"]!["entities"]!.AsArray().OfType<JsonObject>().ToDictionary(o => o["id"]!.ToString());
            return left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out var obj) && JsonNode.DeepEquals(p.Value, obj));
        }
        var beforeCopy = CurrentModel();
        Check((bool)Call("TryApplySubjectAreaTransfer", "copy \"MOVIE COPY\" table in Subject Area \"Accounting\"")!, "Copy command not recognized.");
        var copied = CurrentModel();
        Check(HasMovieCopy(copied, "Accounting") && HasMovieCopy(copied, "<model>"), "Copy must preserve source and add destination placement.");
        Check(SameDefinitions(beforeCopy, copied), "Copy changed existing definitions or indexes.");
        Check(copied["workspace"]!["relationships"]!.AsArray().Count == source["workspace"]!["relationships"]!.AsArray().Count, "Copy lost relationships.");
        Check((bool)Call("TryApplySubjectAreaTransfer", "move \"MOVIE COPY\" table to Subject Area \"Employee\"")!, "Move command not recognized.");
        var moved = CurrentModel();
        Check(HasMovieCopy(moved, "Employee") && !HasMovieCopy(moved, "Accounting") && HasMovieCopy(moved, "<model>"), "Move removed the wrong placement.");
        Check(SameDefinitions(copied, moved), "Move changed model identity or properties.");
        Call("TryApplySubjectAreaTransfer", "copy table MOVIE COPY to Subject Area Employee");
        var repeated = CurrentModel();
        Check(JsonNode.DeepEquals(moved["workspace"], repeated["workspace"]), "Repeated copy duplicated an object.");
        Call("TryApplySubjectAreaTransfer", "move \"MOVIE COPY\" to Subject Area \"Missing\"");
        Check(JsonNode.DeepEquals(repeated["workspace"], CurrentModel()["workspace"]), "Missing target changed the workspace.");
        Call("TryApplySubjectAreaTransfer", "copy \"MOVIE COPY\" from Subject Area \"<model>\" to Subject Area \"Accounting\" in diagram \"Display1\"");
        Check(HasMovieCopy(CurrentModel(), "Accounting"), "Qualified source and destination diagram failed.");
        Call("TryApplySubjectAreaTransfer", "move \"MOVIE COPY\" table from \"Accounting\" subject area to \"Employee\" subject Area");
        var naturalMove = CurrentModel();
        Check(!HasMovieCopy(naturalMove, "Accounting") && HasMovieCopy(naturalMove, "Employee") && HasMovieCopy(naturalMove, "<model>"), "Source-name-first move failed or removed an unrelated placement.");
        Check(SameDefinitions(copied, naturalMove), "Source-name-first move changed object properties.");
        Call("TryApplySubjectAreaTransfer", "copy \"MOVIE COPY\" table from Subject Area \"Employee\" to \"Accounting\" subject area in diagram \"Display1\"");
        Check(HasMovieCopy(CurrentModel(), "Accounting") && HasMovieCopy(CurrentModel(), "Employee"), "Mixed prefix/suffix copy failed.");
        Parse(CurrentModel().ToJsonString());
        Call("ImportModelJson", source);
        viewBox.SelectedIndex = 1;
        var columns = Field<Dictionary<string, System.Collections.ObjectModel.ObservableCollection<ColumnInfo>>>("_columns");
        const string parentKey = "Imported_139", childA = "Imported_141", childB = "Imported_143", conflictChild = "Imported_158";
        columns[parentKey].Clear();
        columns[parentKey].Add(new ColumnInfo("aa", "INT", true, true) { PhysicalName = "AA", Description = "First parent key" });
        columns[parentKey].Add(new ColumnInfo("bb", "VARCHAR(30)", true, true) { PhysicalName = "BB", Description = "Second parent key" });
        foreach (var child in new[] { childA, childB, conflictChild }) columns[child].Clear();
        columns[childA].Add(new ColumnInfo("cc", "INT", false));
        columns[childB].Add(new ColumnInfo("dd", "INT", false));
        columns[conflictChild].Add(new ColumnInfo("AA", "INT", true, true));
        foreach (var entity in new[] { parentKey, childA, childB, conflictChild }) Call("RebuildNestedRows", entity);
        Check((bool)Call("TryCreateSubCategory", parentKey, childA)!, "First Sub-Category creation failed.");
        Check((bool)Call("TryCreateSubCategory", parentKey, childB)!, "Second Sub-Category creation failed.");
        Check((bool)Call("BlockSubCategoryKeyMove", childA, columns[childA].First(c => c.IsPrimaryKey))!, "Subtype PK demotion was allowed.");
        Check(Field<System.Windows.Controls.TextBlock>("StatusText").Text.Contains("non-PK area"), "PK demotion warning missing.");
        Check((bool)Call("BlockSubCategoryKeyMove", childA, columns[childA].First(c => !c.IsPrimaryKey))!, "Subtype non-PK promotion was allowed.");
        Check(Field<System.Windows.Controls.TextBlock>("StatusText").Text.Contains("to the PK area"), "PK promotion warning missing.");
        Check(!(bool)Call("BlockSubCategoryKeyMove", parentKey, columns[parentKey][0])!, "Ordinary parent key move was blocked.");
        foreach (var child in new[] { childA, childB })
        {
            Check(columns[child].Count == 3, "Inherited attributes missing or other attributes removed.");
            foreach (var pk in columns[parentKey])
            {
                var inherited = columns[child].Single(c => c.Name == pk.Name);
                Check(inherited.IsPrimaryKey && inherited.IsForeignKey && inherited.Required && inherited.Id != pk.Id &&
                    inherited.Type == pk.Type && inherited.PhysicalName == pk.PhysicalName && inherited.Description == pk.Description,
                    "Inherited key identity, flags, type, or documentation incorrect.");
            }
        }
        var dynamicRelations = Field<Dictionary<string, DynamicRelationship>>("_dynamicRelationships");
        var countBeforeReject = dynamicRelations.Count;
        Check(!(bool)Call("TryCreateSubCategory", parentKey, conflictChild)!, "Existing child primary key was accepted.");
        Check(columns[conflictChild].Count == 1 && dynamicRelations.Count == countBeforeReject, "Duplicate rejection partially changed model.");
        Check(Field<System.Windows.Controls.TextBlock>("StatusText").Text.Contains("already exist"), "Duplicate error message missing.");
        columns[conflictChild][0] = columns[conflictChild][0] with { Name = "UnrelatedChildKey" };
        Check(!(bool)Call("TryCreateSubCategory", parentKey, conflictChild)!, "Differently named child PK was accepted.");
        Check(columns[conflictChild].Count == 1 && dynamicRelations.Count == countBeforeReject, "Existing PK rejection mutated the model.");
        Check(Field<System.Windows.Controls.TextBlock>("StatusText").Text.Contains("UnrelatedChildKey") &&
            Field<System.Windows.Controls.TextBlock>("StatusText").FontWeight == FontWeights.Bold, "Existing PK warning is missing or not bold.");
        Check(!(bool)Call("TryCreateSubCategory", parentKey, childA)!, "Duplicate Sub-Category accepted.");
        Check(!(bool)Call("TryCreateSubCategory", childA, parentKey)!, "Sub-Category cycle accepted.");
        var subtypes = dynamicRelations.Where(p => p.Value.IsSubCategory).ToArray();
        Check(subtypes.Length == 2 && subtypes.All(p => p.Value.IsIdentifying), "Sub-Category flags incorrect.");
        var junctions = subtypes.Select(p => ((System.Windows.Media.PathGeometry)p.Value.Visible.Data).Figures[1].StartPoint).ToArray();
        Check(junctions[0] == junctions[1], "Children do not share a Sub-Category junction.");
        var markers = Field<Dictionary<string, (System.Windows.Shapes.Path Line, System.Windows.Shapes.Path Marker)>>("_notationEndpoints");
        Check(subtypes.Count(p => !markers[p.Key].Marker.Data.IsEmpty()) == 1, "A parent must have exactly one shared junction marker.");
        viewBox.SelectedIndex = 0;
        Call("UpdateRelationshipLines"); // The smoke window is intentionally never loaded/shown.
        Check(subtypes.All(p => ((System.Windows.Media.PathGeometry)p.Value.Visible.Data).Figures.Count == 1), "Physical subtype still has a shared junction route.");
        Check(subtypes.All(p => markers[p.Key].Marker.Data.IsEmpty() && !markers[p.Key].Marker.IsHitTestVisible), "Physical subtype symbol remains visible or clickable.");
        Check(subtypes.Select(p => ((System.Windows.Media.PathGeometry)p.Value.Visible.Data).Figures[0].StartPoint).Distinct().Count() == 2, "Physical subtype parent anchors overlap.");
        viewBox.SelectedIndex = 1;
        Call("UpdateRelationshipLines");
        Check(subtypes.All(p => ((System.Windows.Media.PathGeometry)p.Value.Visible.Data).Figures.Count == 2) &&
            subtypes.Count(p => !markers[p.Key].Marker.Data.IsEmpty()) == 1, "Logical subtype junction did not return.");
        var subcategoryJson = CurrentModel();
        Check(subcategoryJson["workspace"]!["relationships"]!.AsArray().OfType<JsonObject>().Count(r => r["isSubCategory"]?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true) == 2,
            "Sub-Category type not exported.");
        Call("ImportModelJson", Parse(subcategoryJson.ToJsonString()));
        dynamicRelations = Field<Dictionary<string, DynamicRelationship>>("_dynamicRelationships");
        Check(dynamicRelations.Values.Count(r => r.IsSubCategory) == 2, "Sub-Category type lost on import.");
        columns = Field<Dictionary<string, System.Collections.ObjectModel.ObservableCollection<ColumnInfo>>>("_columns");
        Check(columns[childA].Count(c => c.IsPrimaryKey && c.IsForeignKey) == 2, "Import duplicated or lost inherited keys.");
        void ArmSubCategory(string parent)
        {
            typeof(MainWindow).GetField("_pendingRelationshipType", Private)!.SetValue(window, "Sub-Category");
            typeof(MainWindow).GetField("_relationshipSourceKey", Private)!.SetValue(window, parent);
        }
        bool IsToolArmed() => typeof(MainWindow).GetField("_pendingRelationshipType", Private)!.GetValue(window) is not null ||
            typeof(MainWindow).GetField("_relationshipSourceKey", Private)!.GetValue(window) is not null;
        System.Windows.Input.MouseButtonEventArgs Click() =>
            new(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent };
        ArmSubCategory(parentKey);
        Check((bool)Call("HandleSubCategoryClick", childA, Click())!, "Armed Sub-Category click was not handled.");
        Check(!IsToolArmed(), "Rejected Sub-Category keeps intercepting selection.");
        Check((string)Field<System.Windows.Controls.Border>("_selectedCard").Tag == childA, "Rejected child cannot be selected.");
        Check(Field<System.Windows.Controls.TextBlock>("StatusText").Text.Contains("already exist"), "Selection cleared the validation message.");
        Check(!(bool)Call("HandleSubCategoryClick", childB, Click())!, "Subsequent ordinary click is still intercepted.");
        ArmSubCategory(parentKey);
        Check(!(bool)Call("HandleSubCategoryClick", parentKey, Click())! && !IsToolArmed(), "Clicking the source should cancel and allow selection.");
        Call("SelectCard", Call("FindCard", parentKey)!);
        var subtypeTool = Field<System.Windows.Controls.Button>("SubCategoryToolButton");
        Call("DiagramTool_Click", subtypeTool, new RoutedEventArgs());
        Check(IsToolArmed(), "Sub-Category tool did not activate.");
        Call("DiagramTool_Click", subtypeTool, new RoutedEventArgs());
        Check(!IsToolArmed(), "Clicking Sub-Category again did not cancel.");
        ArmSubCategory(parentKey);
        var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, new TestPresentationSource(), 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent };
        Call("DiagramRelationship_KeyDown", window, escape);
        Check(!IsToolArmed() && escape.Handled, "Escape did not cancel the pending relationship.");
        var firstSubtype = dynamicRelations.First(p => p.Value.IsSubCategory).Key;
        Call("DeleteRelationship", firstSubtype);
        markers = Field<Dictionary<string, (System.Windows.Shapes.Path Line, System.Windows.Shapes.Path Marker)>>("_notationEndpoints");
        Check(dynamicRelations.Where(p => p.Value.IsSubCategory).All(p => !markers[p.Key].Marker.Data.IsEmpty()), "Deleting a child removed the remaining child's junction.");
        Check(columns[childA].All(c => !c.IsForeignKey), "Individual subtype deletion left inherited keys.");
        Call("ImportModelJson", Parse(subcategoryJson.ToJsonString()));
        dynamicRelations = Field<Dictionary<string, DynamicRelationship>>("_dynamicRelationships");
        columns = Field<Dictionary<string, System.Collections.ObjectModel.ObservableCollection<ColumnInfo>>>("_columns");
        System.Windows.Controls.Border AttributeRow(string entityKey, string attributeName)
        {
            var card = (System.Windows.Controls.Border)Call("FindCard", entityKey)!;
            var fields = ((System.Windows.Controls.Grid)card.Child).Children.OfType<System.Windows.Controls.StackPanel>().First();
            return fields.Children.OfType<System.Windows.Controls.Border>().First(r =>
                r.Tag?.GetType().GetProperty("AttributeName")?.GetValue(r.Tag)?.ToString() == attributeName);
        }
        Call("SelectEntity", parentKey);
        Field<System.Windows.Controls.DataGrid>("ColumnsGrid").SelectedItem = columns[parentKey][0];
        var clickedAttribute = columns[childA].Single(c => c.Name == "aa");
        var attributeRow = AttributeRow(childA, clickedAttribute.Name);
        Check(attributeRow.ContextMenu is not null, "Attribute context menu missing.");
        attributeRow.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent });
        Check(Field<System.Windows.Controls.DataGrid>("ColumnsGrid").SelectedItem is ColumnInfo selected && selected.Id == clickedAttribute.Id,
            "Right-click did not select the clicked attribute.");
        var attributeIndex = columns[childA].IndexOf(clickedAttribute);
        columns[childA][attributeIndex] = clickedAttribute with { Name = "RenamedBeforeDelete" };
        var deleteColumnMenu = (System.Windows.Controls.MenuItem)attributeRow.ContextMenu!.Items[0];
        deleteColumnMenu.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Check(columns[childA].All(c => c.Id != clickedAttribute.Id) && columns[parentKey].Any(c => c.Name == "aa"),
            "Context Delete removed the wrong attribute or failed after rename.");
        Check(Field<System.Windows.Controls.StackPanel>("AttributePropertiesPanel").Visibility == Visibility.Collapsed &&
            Field<System.Windows.Controls.StackPanel>("AttributePropertiesPanel").Children.Count == 0, "Deleted attribute properties remain visible.");
        const string viewKey = "Imported_276";
        var viewCount = columns[viewKey].Count;
        var viewRow = AttributeRow(viewKey, columns[viewKey][0].Name);
        viewRow.ContextMenu!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.ContextMenu.OpenedEvent));
        Check(!((System.Windows.Controls.MenuItem)viewRow.ContextMenu.Items[0]).IsEnabled, "Readonly view deletion menu is enabled.");
        ((System.Windows.Controls.MenuItem)viewRow.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Check(columns[viewKey].Count == viewCount, "Readonly view attribute deleted.");
        var nestedParent = new ColumnInfo("Nested", "object", false);
        var nestedChild = new ColumnInfo("NestedChild", "string", false) { ParentId = nestedParent.Id };
        columns[childB].Add(nestedParent); columns[childB].Add(nestedChild);
        Call("RebuildNestedRows", childB);
        var nestedRow = AttributeRow(childB, "Nested");
        ((System.Windows.Controls.MenuItem)nestedRow.ContextMenu!.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Check(columns[childB].All(c => c.Id != nestedParent.Id && c.Id != nestedChild.Id), "Nested descendants were left behind after Delete.");
        Parse(CurrentModel().ToJsonString());
        Call("ImportModelJson", Parse(subcategoryJson.ToJsonString()));
        dynamicRelations = Field<Dictionary<string, DynamicRelationship>>("_dynamicRelationships");
        markers = Field<Dictionary<string, (System.Windows.Shapes.Path Line, System.Windows.Shapes.Path Marker)>>("_notationEndpoints");
        var group = dynamicRelations.Where(p => p.Value.IsSubCategory).ToArray();
        var circle = group.Select(p => markers[p.Key].Marker).Single(m => !m.Data.IsEmpty());
        Check(circle.IsHitTestVisible && circle.ContextMenu is not null, "Sub-Category circle is not interactive.");
        circle.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Check(Field<System.Windows.Shapes.Path>("_selectedSubCategoryMarker") == circle, "Circle click did not select it.");
        circle.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.MouseRightButtonDownEvent });
        var unrelatedCount = dynamicRelations.Values.Count(r => !r.IsSubCategory);
        var inheritedIds = group.SelectMany(p => p.Value.InheritedAttributeIds!).ToHashSet();
        var circleDelete = circle.ContextMenu!.Items.OfType<System.Windows.Controls.MenuItem>().Single(m => Equals(m.Header, "Delete"));
        Check(circleDelete.Template is not null && deleteColumnMenu.Template is not null, "Text-only Delete template missing.");
        circleDelete.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Check(!dynamicRelations.Values.Any(r => r.IsSubCategory), "Circle Delete left Sub-Category branches behind.");
        Check(dynamicRelations.Values.Count(r => !r.IsSubCategory) == unrelatedCount, "Circle Delete removed unrelated relationships.");
        columns = Field<Dictionary<string, System.Collections.ObjectModel.ObservableCollection<ColumnInfo>>>("_columns");
        Check(columns.Values.SelectMany(c => c).All(c => !inheritedIds.Contains(c.Id)), "Circle Delete left inherited columns.");
        Check(columns[parentKey].Count(c => c.IsPrimaryKey) == 2 && columns[childA].Any(c => !c.IsPrimaryKey), "Deletion removed parent keys or child-owned columns.");
        Check(!markers.Values.Any(p => p.Marker == circle), "Deleted circle remains on the canvas.");
        Parse(CurrentModel().ToJsonString());
        var rollJson = (JsonObject)subcategoryJson.DeepClone();
        var rollRelations = rollJson["workspace"]!["relationships"]!.AsArray();
        foreach (var r in rollRelations.OfType<JsonObject>().Where(r => r["isSubCategory"]?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) != true).ToArray())
            rollRelations.Remove(r);
        Call("ImportModelJson", Parse(rollJson.ToJsonString()));
        columns = Field<Dictionary<string, System.Collections.ObjectModel.ObservableCollection<ColumnInfo>>>("_columns");
        var childNames = columns[childA].Concat(columns[childB]).Select(c => c.Name).Distinct().ToArray();
        Check((bool)Call("RollSubCategory", parentKey, true)!, "RollUp failed.");
        Check(!columns.ContainsKey(childA) && !columns.ContainsKey(childB), "RollUp kept children.");
        Check(childNames.All(n => columns[parentKey].Any(c => c.Name == n)), "RollUp lost attributes.");
        Parse(CurrentModel().ToJsonString());
        Call("ImportModelJson", Parse(rollJson.ToJsonString()));
        columns = Field<Dictionary<string, System.Collections.ObjectModel.ObservableCollection<ColumnInfo>>>("_columns");
        columns[parentKey].Add(new ColumnInfo("ParentDetail", "VARCHAR(80)", false) { Description = "Preserved description" });
        Check((bool)Call("RollSubCategory", parentKey, false)!, "RollDown failed.");
        Check(!columns.ContainsKey(parentKey), "RollDown kept parent.");
        foreach (var child in new[] { childA, childB })
        {
            Check(columns[child].Single(c => c.Name == "ParentDetail").Description == "Preserved description", "RollDown lost metadata.");
            Check(columns[child].Count(c => c.IsPrimaryKey) == 2 && columns[child].Where(c => c.IsPrimaryKey).All(c => !c.IsForeignKey), "RollDown did not retain standalone keys.");
        }
        Parse(CurrentModel().ToJsonString());
        Call("ImportModelJson", Parse(subcategoryJson.ToJsonString()));
        var identifyBefore = CurrentModel()["workspace"]!["entities"]!.ToJsonString();
        Call("IdentifySubCategory", parentKey);
        dynamicRelations = Field<Dictionary<string, DynamicRelationship>>("_dynamicRelationships");
        markers = Field<Dictionary<string, (System.Windows.Shapes.Path Line, System.Windows.Shapes.Path Marker)>>("_notationEndpoints");
        var identified = dynamicRelations.Where(p => p.Value.IsSubCategory).ToArray();
        Check(identified.Length == 2 && identified.All(p => p.Value.IsSupertypeSubtypeIdentified), "Identify did not update every branch.");
        Check(identified.All(p => ((System.Windows.Media.PathGeometry)p.Value.Visible.Data).Figures.Count == 1), "Identify retained shared routing.");
        foreach (var p in identified)
        {
            var glyphs = (System.Windows.Media.GeometryGroup)markers[p.Key].Marker.Data;
            Check(glyphs.Children.OfType<System.Windows.Media.LineGeometry>().Count() == 2 &&
                glyphs.Children.OfType<System.Windows.Media.EllipseGeometry>().Count() == 1, "Identify endpoint shapes incorrect.");
        }
        Check(CurrentModel()["workspace"]!["entities"]!.ToJsonString() == identifyBefore, "Identify modified attributes.");
        viewBox.SelectedIndex = 0;
        Call("UpdateRelationshipLines");
        Call("SyncNotationEndpoints");
        foreach (var p in identified)
        {
            Check(markers[p.Key].Marker.Data is System.Windows.Media.EllipseGeometry { RadiusX: 4.5, RadiusY: 4.5 }, "Physical Identify must show only a small child circle.");
            Check(((System.Windows.Media.SolidColorBrush)markers[p.Key].Marker.Fill).Color == System.Windows.Media.Color.FromRgb(145, 163, 178), "Physical child circle is not filled.");
        }
        viewBox.SelectedIndex = 1;
        Call("UpdateRelationshipLines");
        Check(identified.All(p => markers[p.Key].Marker.Data is System.Windows.Media.GeometryGroup), "Logical Identify notation not restored.");
        Call("ImportModelJson", Parse(CurrentModel().ToJsonString()));
        Check(Field<Dictionary<string, DynamicRelationship>>("_dynamicRelationships").Values.Where(r => r.IsSubCategory).All(r => r.IsSupertypeSubtypeIdentified), "Identify lost on JSON round trip.");
        foreach (var rollUpBranch in new[] { true, false })
        {
            Call("ImportModelJson", Parse(rollJson.ToJsonString()));
            Call("IdentifySubCategory", parentKey);
            dynamicRelations = Field<Dictionary<string, DynamicRelationship>>("_dynamicRelationships");
            columns = Field<Dictionary<string, System.Collections.ObjectModel.ObservableCollection<ColumnInfo>>>("_columns");
            var branch = dynamicRelations.Single(p => p.Value.IsSubCategory && p.Value.TargetKey == childA);
            Check(branch.Value.Hit.ContextMenu!.Items.Count == 2, "Logical identified line menu missing.");
            viewBox.SelectedIndex = 0; Call("UpdateRelationshipLines");
            Check(branch.Value.Hit.ContextMenu is null && !(bool)Call("RollIdentifiedBranch", branch.Key, rollUpBranch)!, "Physical branch roll is available.");
            viewBox.SelectedIndex = 1; Call("UpdateRelationshipLines");
            Check((bool)Call("RollIdentifiedBranch", branch.Key, rollUpBranch)!, "Selected branch roll failed.");
            Check(columns.ContainsKey(childB), "Branch roll removed sibling.");
            var remaining = dynamicRelations.Single(p => p.Value.IsSubCategory).Value;
            Check(remaining.TargetKey == childB && remaining.SourceKey == (rollUpBranch ? parentKey : childA), "Sibling connection was not preserved or redirected.");
            Check(rollUpBranch ? !columns.ContainsKey(childA) && columns[parentKey].Any(c => c.Name == "cc")
                : !columns.ContainsKey(parentKey) && columns[childA].Count(c => c.IsPrimaryKey) == 2, "Branch roll attributes incorrect.");
            Parse(CurrentModel().ToJsonString());
        }
        Console.WriteLine("PASS: display levels, endpoints, JSON round trips, metadata/index preservation, materialized views, subject-area transfers, Sub-Category composite PK inheritance, duplicates, cycles, shared junctions and persistence.");
        window.Close(); app.Shutdown();
    }
}
