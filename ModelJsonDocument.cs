using System.Text.Json;
using System.Text.Json.Nodes;

namespace ErwinStudioSample;

internal static class ModelJsonDocument
{
    internal static readonly string[] Kinds = ["entities", "views", "cachedViews"];
    internal static string Text(JsonNode? node, string field, string fallback = "") =>
        node?[field]?.ToString() ?? fallback;
    internal static bool Flag(JsonNode? node, string field, bool fallback = false) =>
        bool.TryParse(node?[field]?.ToString(), out var value) ? value : fallback;
    internal static double Number(JsonNode? node, string field, double fallback = 0) =>
        double.TryParse(node?[field]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;
    internal static IEnumerable<JsonObject> Items(JsonNode? node) =>
        node is JsonArray array ? array.OfType<JsonObject>() : [];

    internal static JsonObject Parse(string text)
    {
        var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = 128 }) as JsonObject
            ?? throw new InvalidOperationException("The file must contain a JSON object.");
        if (root["workspace"] is not JsonObject workspace)
            throw new InvalidOperationException("The JSON must contain a workspace object.");
        root["meta"] ??= new JsonObject();
        NormalizeObjectIds(workspace);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kind in Kinds)
        {
            workspace[kind] ??= new JsonArray();
            RequireArray(workspace[kind], kind);
            foreach (var obj in Items(workspace[kind]))
            {
                var id = RequiredId(obj, kind);
                if (!ids.TryAdd(id, kind)) throw new InvalidOperationException($"Duplicate object ID: {id}");
                if (string.IsNullOrWhiteSpace(Text(obj, "name"))) throw new InvalidOperationException($"Object {id} has no name.");
                obj["attributes"] ??= new JsonArray();
                RequireArray(obj["attributes"], "attributes");
                var attributes = new HashSet<string>();
                foreach (var attribute in Items(obj["attributes"]))
                    if (!attributes.Add(RequiredId(attribute, "attribute"))) throw new InvalidOperationException($"Duplicate attribute ID in {id}.");
                foreach (var attribute in Items(obj["attributes"]))
                {
                    var seen = new HashSet<string> { Text(attribute, "id") };
                    var parent = Text(attribute, "parentId");
                    while (parent.Length > 0)
                    {
                        if (!attributes.Contains(parent) || !seen.Add(parent)) throw new InvalidOperationException($"Invalid attribute hierarchy in {id}.");
                        parent = Text(Items(obj["attributes"]).First(a => Text(a, "id") == parent), "parentId");
                    }
                }
                foreach (var index in Items(obj["indexes"]))
                    foreach (var member in Items(index["members"]))
                        if (!attributes.Contains(Text(member, "columnId"))) throw new InvalidOperationException($"Index in {id} references an unknown attribute.");
            }
        }
        if (ids.Count > 5000) throw new InvalidOperationException("This file exceeds the 5,000-object import limit.");
        workspace["relationships"] ??= new JsonArray();
        RequireArray(workspace["relationships"], "relationships");
        var relationships = new HashSet<string>();
        foreach (var relation in Items(workspace["relationships"]))
        {
            if (!relationships.Add(RequiredId(relation, "relationship"))) throw new InvalidOperationException("Duplicate relationship ID.");
            if (!ids.ContainsKey(Text(relation, "parent")) || !ids.ContainsKey(Text(relation, "child")))
                throw new InvalidOperationException($"Relationship {Text(relation, "id")} references an unknown object.");
        }
        workspace["subjectAreas"] ??= new JsonArray();
        RequireArray(workspace["subjectAreas"], "subjectAreas");
        var areas = (JsonArray)workspace["subjectAreas"]!;
        if (!Items(areas).Any(a => Text(a, "id") == "0"))
            areas.Insert(0, new JsonObject { ["id"] = "0", ["name"] = "<model>", ["locked"] = true, ["diagrams"] = new JsonArray() });
        var areaIds = new HashSet<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diagramIds = new HashSet<string>();
        foreach (var area in Items(areas))
        {
            if (!areaIds.Add(RequiredId(area, "subject area"))) throw new InvalidOperationException("Duplicate subject area ID.");
            if (Text(area, "id") == "0") { area["name"] = "<model>"; area["locked"] = true; }
            if (string.IsNullOrWhiteSpace(Text(area, "name")) || !names.Add(Text(area, "name"))) throw new InvalidOperationException("Subject area names must be nonempty and unique.");
            area["diagrams"] ??= new JsonArray();
            RequireArray(area["diagrams"], "diagrams");
            if (((JsonArray)area["diagrams"]!).Count == 0)
                ((JsonArray)area["diagrams"]!).Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["name"] = "Diagram 1", ["modelShapes"] = new JsonObject() });
            foreach (var diagram in Items(area["diagrams"]))
            {
                if (!diagramIds.Add(RequiredId(diagram, "diagram"))) throw new InvalidOperationException("Duplicate diagram ID.");
                diagram["modelShapes"] ??= new JsonObject();
                var placements = new HashSet<string>();
                foreach (var kind in Kinds)
                {
                    diagram["modelShapes"]![kind] ??= new JsonArray();
                    RequireArray(diagram["modelShapes"]![kind], kind + " placements");
                    foreach (var shape in Items(diagram["modelShapes"]![kind]))
                    {
                        var id = RequiredId(shape, "shape");
                        if (!ids.TryGetValue(id, out var objectKind) || objectKind != kind)
                            throw new InvalidOperationException($"Diagram {Text(diagram, "name")} references an unknown {kind} object: {id}");
                        if (!placements.Add(id)) throw new InvalidOperationException("Duplicate object placement in a diagram.");
                    }
                }
            }
        }
        if (diagramIds.Count > 200) throw new InvalidOperationException("This file exceeds the 200-diagram import limit.");
        return root;
    }
    internal static bool NumericId(string id) => id.Length > 0 && id.All(c => c >= '0' && c <= '9');

    internal static HashSet<string> ReservedIds(JsonNode? node)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        void Visit(JsonNode? value)
        {
            if (value is JsonObject obj)
            {
                if (obj["id"] is { } id) result.Add(id.ToString());
                foreach (var property in obj) Visit(property.Value);
            }
            else if (value is JsonArray array) foreach (var item in array) Visit(item);
        }
        Visit(node);
        return result;
    }

    internal static string AllocateId(HashSet<string> reserved)
    {
        long number = 1;
        while (reserved.Contains(number.ToString(System.Globalization.CultureInfo.InvariantCulture))) number++;
        var id = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        reserved.Add(id);
        return id;
    }

    private static void NormalizeObjectIds(JsonObject workspace)
    {
        // Older exports used internal text keys. Migrate their explicit object references together.
        var reserved = ReservedIds(workspace);
        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var obj in Kinds.SelectMany(k => Items(workspace[k])))
        {
            var old = Text(obj, "id");
            if (old.Length == 0 || NumericId(old)) continue;
            if (!remap.TryGetValue(old, out var id)) remap[old] = id = AllocateId(reserved);
            obj["id"] = id;
        }
        foreach (var relationship in Items(workspace["relationships"]))
            foreach (var field in new[] { "parent", "child" })
                if (remap.TryGetValue(Text(relationship, field), out var id)) relationship[field] = id;
        foreach (var diagram in Items(workspace["subjectAreas"]).SelectMany(a => Items(a["diagrams"])))
            foreach (var shape in Kinds.SelectMany(k => Items(diagram["modelShapes"]?[k])))
                if (remap.TryGetValue(Text(shape, "id"), out var id)) shape["id"] = id;
    }
    private static string RequiredId(JsonObject obj, string label)
    {
        var id = Text(obj, "id");
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException($"A {label} has no ID.");
        return id;
    }
    private static void RequireArray(JsonNode? node, string label)
    {
        if (node is not JsonArray array || array.Any(n => n is not JsonObject))
            throw new InvalidOperationException($"{label} must be an array of objects.");
    }
}
