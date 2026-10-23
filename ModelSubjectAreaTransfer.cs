using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static ErwinStudioSample.ModelJsonDocument;

namespace ErwinStudioSample;

internal sealed record SubjectAreaTransferCommand(string Action, string ObjectName, string DestinationArea, string? DestinationDiagram, string? SourceArea);
internal sealed record SubjectAreaTransferResult(JsonObject Model, string ObjectId, string ObjectName, string SourceArea, string? SourceDiagram,
    string DestinationArea, string DestinationDiagram, bool Changed);

internal static class ModelSubjectAreaTransfer
{
    internal static bool IsTransferRequest(string description) =>
        Regex.IsMatch(description, @"^\s*(?:please\s+)?(?:copy|move)\b", RegexOptions.IgnoreCase) &&
        Regex.IsMatch(description, @"\bsubject\s*area\b", RegexOptions.IgnoreCase);

    internal static SubjectAreaTransferCommand Parse(string description)
    {
        var match = Regex.Match(description,
            @"^\s*(?:please\s+)?(?<action>copy|move)\s+(?<object>.+?)\s+(?:to|into|in)\s+(?<target>(?:(?:the\s+)?subject\s*area\s+.+|.+?\s+subject\s*area)(?:\s+(?:in|into|to)\s+(?:the\s+)?diagram\s+.+)?)\s*[.!]?\s*$",
            RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException("Use: copy or move \"Table name\" to Subject Area \"Area name\". You can also specify from Subject Area \"Source name\".");
        var source = match.Groups["object"].Value.Trim();
        string? sourceArea = null;
        var from = Regex.Match(source, @"\s+from\s+(?<area>.+)$", RegexOptions.IgnoreCase);
        if (from.Success) { sourceArea = AreaName(from.Groups["area"].Value); source = source[..from.Index].Trim(); }
        source = Regex.Replace(source, @"^(?:the\s+)?(?:table|entity|view|materialized\s+view)\s+", "", RegexOptions.IgnoreCase);
        source = Regex.Replace(source, @"\s+(?:table|entity|view|materialized\s+view)$", "", RegexOptions.IgnoreCase);
        var target = match.Groups["target"].Value.Trim();
        string? destinationDiagram = null;
        var diagram = Regex.Match(target, @"\s+(?:in|into|to)\s+(?:the\s+)?diagram\s+(?<diagram>.+)$", RegexOptions.IgnoreCase);
        if (diagram.Success) { destinationDiagram = Name(diagram.Groups["diagram"].Value); target = target[..diagram.Index]; }
        var objectName = Name(source);
        var areaName = AreaName(target);
        if (objectName.Length == 0 || areaName.Length == 0) throw new InvalidOperationException("Specify both the object name and destination subject area.");
        return new(match.Groups["action"].Value.ToLowerInvariant(), objectName, areaName, destinationDiagram, sourceArea);
    }

    private static string AreaName(string value)
    {
        value = value.Trim().TrimEnd('.', '!').Trim();
        var match = Regex.Match(value,
            @"^(?:(?:the\s+)?subject\s*area\s+(?<name>.+)|(?<name>.+?)\s+subject\s*area)$",
            RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException("Specify a subject area as Subject Area \"Name\" or \"Name\" subject area.");
        return Name(match.Groups["name"].Value);
    }

    private static string Name(string value)
    {
        value = value.Trim().TrimEnd('.', '!').Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == (char)39 && value[^1] == (char)39) ||
            (value[0] == '“' && value[^1] == '”') || (value[0] == '`' && value[^1] == '`')))
            return value[1..^1].Trim();
        return value;
    }

    internal static SubjectAreaTransferResult Apply(JsonObject original, SubjectAreaTransferCommand command, string? preferredDestinationDiagramId = null)
    {
        var model = (JsonObject)original.DeepClone();
        var workspace = model["workspace"] ?? throw new InvalidOperationException("No workspace is open.");
        var areas = Items(workspace["subjectAreas"]).ToArray();
        bool Equal(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
        var destination = areas.SingleOrDefault(a => Equal(Text(a, "name"), command.DestinationArea))
            ?? throw new InvalidOperationException($"Subject area '{command.DestinationArea}' does not exist. Add it first.");
        var targetDiagrams = Items(destination["diagrams"]).ToArray();
        var target = command.DestinationDiagram is { } requested
            ? targetDiagrams.SingleOrDefault(d => Equal(Text(d, "name"), requested))
                ?? throw new InvalidOperationException($"Diagram '{requested}' was not found in '{command.DestinationArea}'.")
            : targetDiagrams.FirstOrDefault(d => Text(d, "id") == preferredDestinationDiagramId) ?? targetDiagrams.FirstOrDefault()
                ?? throw new InvalidOperationException("The destination subject area has no diagram.");
        var definitions = Kinds.SelectMany(kind => Items(workspace[kind]).Select(obj => (Kind: kind, Object: obj)))
            .Where(o => Equal(Text(o.Object, "name"), command.ObjectName)).ToArray();
        if (definitions.Length == 0) throw new InvalidOperationException($"Object '{command.ObjectName}' was not found.");
        var placements = areas.SelectMany(a => Items(a["diagrams"]).SelectMany(d => Kinds.SelectMany(k => Items(d["modelShapes"]?[k])
            .Select(p => (Area: a, Diagram: d, Shape: p, Kind: k))))).ToArray();
        var candidates = placements.Where(p => definitions.Any(o => Text(o.Object, "id") == Text(p.Shape, "id"))).ToArray();
        if (command.SourceArea is { } sourceArea)
        {
            if (!areas.Any(a => Equal(Text(a, "name"), sourceArea))) throw new InvalidOperationException($"Source subject area '{sourceArea}' does not exist.");
            candidates = candidates.Where(p => Equal(Text(p.Area, "name"), sourceArea)).ToArray();
            if (candidates.Length == 0) throw new InvalidOperationException($"'{command.ObjectName}' is not placed in '{sourceArea}'.");
        }
        var active = candidates.Where(p => Text(p.Diagram, "id") == Text(model["meta"], "activeDiagramId") &&
            Text(p.Area, "id") == Text(model["meta"], "activeSubjectAreaId")).ToArray();
        if (active.Length > 0) candidates = active;
        else
        {
            var inArea = candidates.Where(p => Text(p.Area, "id") == Text(model["meta"], "activeSubjectAreaId")).ToArray();
            if (inArea.Length > 0 && command.SourceArea is null) candidates = inArea;
        }
        if (candidates.Select(p => Text(p.Shape, "id")).Distinct().Count() > 1)
            throw new InvalidOperationException("More than one object has that name. Select its source diagram or specify from Subject Area \"Source name\".");
        if (command.Action == "move" && candidates.Length > 1)
            throw new InvalidOperationException("This object appears in multiple source diagrams. Open the source diagram before moving it.");
        if (candidates.Length == 0 && (command.Action == "move" || definitions.Length > 1))
            throw new InvalidOperationException("Open the object's source diagram before copying or moving it.");
        var source = candidates.FirstOrDefault();
        var definition = candidates.Length > 0
            ? definitions.Single(o => Text(o.Object, "id") == Text(source.Shape, "id"))
            : definitions.Single();
        var id = Text(definition.Object, "id");
        var targetShapes = (JsonArray)target["modelShapes"]![definition.Kind]!;
        var alreadyThere = Items(targetShapes).Any(p => Text(p, "id") == id);
        var sameDiagram = candidates.Length > 0 && ReferenceEquals(source.Diagram, target);
        var changed = false;
        if (!alreadyThere)
        {
            var placement = source.Shape?.DeepClone() as JsonObject ?? new JsonObject { ["id"] = id, ["width"] = 300, ["height"] = 0, ["displayLevelLogical"] = -1, ["displayLevelPhysical"] = -1 };
            placement["name"] = Text(definition.Object, "name");
            var allTargetShapes = Kinds.SelectMany(k => Items(target["modelShapes"]?[k])).ToArray();
            placement["x"] = 50;
            placement["y"] = allTargetShapes.Length == 0 ? 50 : allTargetShapes.Max(p => Number(p, "y") + Math.Max(80, Number(p, "height"))) + 60;
            targetShapes.Add(placement);
            changed = true;
        }
        if (command.Action == "move" && !sameDiagram && candidates.Length > 0)
        {
            ((JsonArray)source.Diagram["modelShapes"]![source.Kind]!).Remove(source.Shape);
            changed = true;
        }
        model["meta"]!["activeSubjectAreaId"] = Text(destination, "id");
        model["meta"]!["activeDiagramId"] = Text(target, "id");
        return new(model, id, Text(definition.Object, "name"), source.Area is null ? "" : Text(source.Area, "name"), source.Diagram is null ? null : Text(source.Diagram, "name"),
            Text(destination, "name"), Text(target, "name"), changed);
    }
}
