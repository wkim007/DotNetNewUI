using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using ErwinStudioSample;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
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
        Console.WriteLine("PASS: display-level rendering, logical/physical settings, Neo4j Graph, JSON overrides, default export, sample import, metadata/index preservation, round trip, invalid references, materialized views.");
        window.Close(); app.Shutdown();
    }
}
