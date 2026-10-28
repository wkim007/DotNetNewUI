using System.Text.Json;
using System.Text.RegularExpressions;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private bool TryApplyAiSubCategoryRoll(string description)
    {
        var actions = Regex.Matches(description, @"\broll[ -]?(up|down)\b", RegexOptions.IgnoreCase);
        if (actions.Count == 0) return false;
        try
        {
            TraceAi("Resolving sub-category RollUp/RollDown in the current diagram.");
            if (ViewModeBox.SelectedIndex != 1) throw new InvalidOperationException("Switch to Logical View before performing RollUp or RollDown.");
            if (actions.Select(a => a.Groups[1].Value.ToLowerInvariant()).Distinct().Count() != 1)
                throw new InvalidOperationException("Request either RollUp or RollDown in one command.");
            var up = actions[0].Groups[1].Value.Equals("up", StringComparison.OrdinalIgnoreCase);
            var namesMatch = Regex.Match(description, @"\bfor\s+(.+?)\s*[.!]?\s*$", RegexOptions.IgnoreCase);
            if (!namesMatch.Success) throw new InvalidOperationException("Specify the objects, for example: do rollup operation in current diagram for Parent, Child1, Child2.");
            var names = Regex.Split(namesMatch.Groups[1].Value, @"\s*,\s*|\s+and\s+", RegexOptions.IgnoreCase)
                .Select(n => n.Trim().Trim('"', '\'', '.', '!')).Where(n => n.Length > 0).ToArray();
            var keys = new HashSet<string>();
            foreach (var name in names)
            {
                var matches = _entityCardKeys.Where(k => EntityDisplayName(k).Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException($"Object '{name}' is missing or ambiguous in the current diagram.");
                keys.Add(matches[0]);
            }
            var groups = _dynamicRelationships.Where(p => p.Value.IsSubCategory && DiagramCanvasSurface.Children.Contains(p.Value.Visible))
                .GroupBy(p => p.Value.SourceKey).ToArray();
            var full = groups.Where(g => keys.SetEquals(g.Select(p => p.Value.TargetKey).Append(g.Key)) ||
                keys.Count == 1 && keys.Contains(g.Key)).ToArray();
            var branches = groups.SelectMany(g => g).Where(p => p.Value.IsSupertypeSubtypeIdentified &&
                (keys.SetEquals(new[] { p.Value.SourceKey, p.Value.TargetKey }) || keys.Count == 1 && keys.Contains(p.Value.TargetKey))).ToArray();
            if (full.Length != 1 && branches.Length != 1)
                throw new InvalidOperationException("Name one sub-category parent and its children, or one identified parent/child branch. No changes applied.");
            var before = CaptureAiModelSnapshot();
            TraceAi(up ? "Applying RollUp to the resolved sub-category objects." : "Applying RollDown to the resolved sub-category objects.");
            var applied = full.Length == 1 ? RollSubCategory(full[0].Key, up) : RollIdentifiedBranch(branches[0].Key, up);
            AiStatus.Text = StatusText.Text;
            TraceAi(AiStatus.Text);
            _latestAiRequestJson = JsonSerializer.Serialize(new {
                Engine = "Local model operation", Request = new { description, Operation = up ? "RollUp" : "RollDown", Objects = names },
                Status = applied ? "Applied to model" : "Not applied", Message = AiStatus.Text,
                InputModelSnapshot = before, AppliedModelSnapshot = CaptureAiModelSnapshot()
            }, new JsonSerializerOptions { WriteIndented = true });
            if (applied) { AiSchemaDescription.Clear(); ClearAiAttachment(); }
            UpdateAiControlState();
        }
        catch (InvalidOperationException ex)
        {
            AiStatus.Text = StatusText.Text = "Could not roll Sub-Category: " + ex.Message;
            TraceAi(AiStatus.Text);
        }
        return true;
    }
}
