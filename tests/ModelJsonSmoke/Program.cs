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
        Console.WriteLine("PASS: default export, sample import, diagram switching, metadata/index preservation, round trip, invalid references, materialized views.");
        window.Close(); app.Shutdown();
    }
}
