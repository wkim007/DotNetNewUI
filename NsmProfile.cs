using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace ErwinStudioSample;

public sealed class NsmTerm
{
    public string Words { get; set; } = "";
    public string Abbreviation { get; set; } = "";
    public string Alternate { get; set; } = "";
    public bool Prime { get; set; }
    public bool Modifier1 { get; set; }
    public bool Modifier2 { get; set; }
    public bool Class { get; set; }
    public string Definition { get; set; } = "";
    internal bool Matches(string role) => role switch { "Prime" => Prime, "Modifier 1" => Modifier1, "Modifier 2" => Modifier2, "Class" => Class, _ => false };
}

public sealed class NsmRule
{
    public string Type { get; set; } = "";
    public bool UseGlossary { get; set; } = true;
    public string Part1 { get; set; } = "";
    public string Part2 { get; set; } = "";
    public string Part3 { get; set; } = "";
    public string Part4 { get; set; } = "";
    internal string[] Parts() => new[] { Part1, Part2, Part3, Part4 }.Where(s => !string.IsNullOrEmpty(s)).ToArray();
}

public sealed class NsmProfile
{
    public string Name { get; set; } = "Naming Standard";
    public bool IsActive { get; set; }
    public bool WholeWords { get; set; } = true;
    public bool WholeName { get; set; }
    public bool PhysicalToLogical { get; set; }
    public string Definition { get; set; } = "";
    public string Notes { get; set; } = "";
    public string ExtendedNotes { get; set; } = "";
    public List<NsmRule> Logical { get; set; } = Rules("Entity", "Attribute", "Relationship", "Domain");
    public List<NsmRule> Physical { get; set; } = Rules("Table", "Column", "Relationship", "Domain");
    public ObservableCollection<NsmTerm> Glossary { get; set; } = [];
    private static List<NsmRule> Rules(params string[] names) => names.Select(n => new NsmRule { Type = n }).ToList();

    internal static NsmProfile Default()
    {
        var profile = new NsmProfile { IsActive = true };
        foreach (var (word, abbreviation, role) in new[] {
            ("Customer","CUST",0), ("Employee","EMP",0), ("Order","ORD",0), ("Product","PROD",0),
            ("Account","ACCT",1), ("First","FIRST",1), ("Billing","BILL",1), ("Primary","PRI",2),
            ("Identifier","ID",3), ("Number","NBR",3), ("Description","DESC",3), ("Name","NM",3),
            ("Amount","AMT",3), ("Quantity","QTY",3), ("Date of Birth","DOB",3), ("Information","INFO",3), ("Address","ADDR",3) })
            profile.Glossary.Add(new() { Words = word, Abbreviation = abbreviation, Prime = role == 0, Modifier1 = role == 1, Modifier2 = role == 2, Class = role == 3 });
        return profile;
    }

    internal void MergeGlossary(NsmProfile existing)
    {
        var incoming = Glossary.ToDictionary(term => Normalize(term.Words), StringComparer.OrdinalIgnoreCase);
        var merged = new ObservableCollection<NsmTerm>();
        foreach (var term in existing.Glossary)
            merged.Add(incoming.Remove(Normalize(term.Words), out var replacement) ? replacement : term);
        foreach (var term in Glossary)
            if (incoming.ContainsKey(Normalize(term.Words))) merged.Add(term);
        Glossary = merged;
        Validate();
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Enter a standard name.");
        if (Glossary == null || Logical == null || Physical == null) throw new InvalidDataException("Missing NSM glossary or rules.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in Glossary)
        {
            if (term == null || string.IsNullOrWhiteSpace(term.Words) || Normalize(term.Words).Length == 0 || string.IsNullOrWhiteSpace(term.Abbreviation))
                throw new InvalidDataException("Each glossary row needs words and an abbreviation.");
            if (!seen.Add(Normalize(term.Words))) throw new InvalidDataException("Duplicate glossary phrase: " + term.Words);
        }
        foreach (var rule in Logical.Concat(Physical))
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.Type)) throw new InvalidDataException("Missing object type in naming rule.");
            var parts = rule.Parts();
            if (parts.Any(p => p is not ("Prime" or "Modifier 1" or "Modifier 2" or "Class")) || parts.Distinct().Count() != parts.Length)
                throw new InvalidDataException("Choose a valid component once per part for " + rule.Type + ".");
        }
    }

    private static string Normalize(string input) => string.Join(" ", Regex.Matches(
        Regex.Replace(input, @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " "), @"[\p{L}\p{Nd}]+").Select(m => m.Value));

    private List<(string Text, NsmTerm? Term)> GlossaryParts(string input, bool reverse)
    {
        var normalized = Normalize(input);
        var terms = new Dictionary<string,NsmTerm>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in Glossary)
        {
            terms.TryAdd(Normalize(reverse ? term.Abbreviation : term.Words), term);
            if (reverse && !string.IsNullOrWhiteSpace(term.Alternate)) terms.TryAdd(Normalize(term.Alternate), term);
        }
        var keys = terms.Keys.Where(k => k.Length > 0).OrderByDescending(k => k.Length).ToArray();
        var parts = new List<(string Text, NsmTerm? Term)>();
        for (var i = 0; i < normalized.Length;)
        {
            if (normalized[i] == ' ') { i++; continue; }
            var match = keys.FirstOrDefault(k => normalized.AsSpan(i).StartsWith(k, StringComparison.OrdinalIgnoreCase)
                && (!WholeName || k.Length == normalized.Length)
                && (!WholeWords || (i == 0 || normalized[i-1] == ' ') && (i+k.Length == normalized.Length || normalized[i+k.Length] == ' ')));
            if (match != null)
            {
                var term = terms[match];
                parts.Add((reverse ? term.Words : term.Abbreviation, term));
                i += match.Length;
            }
            else
            {
                int start = i++;
                while (i < normalized.Length && normalized[i] != ' ' &&
                    (WholeWords || WholeName || !keys.Any(k => normalized.AsSpan(i).StartsWith(k, StringComparison.OrdinalIgnoreCase)))) i++;
                parts.Add((normalized[start..i], null));
            }
        }
        return parts;
    }

    internal (string Name, string Finding) ConvertName(string input, string type, bool reverse = false, string? unmatched = null)
    {
        var rule = (reverse ? Logical : Physical).FirstOrDefault(r => r.Type == type);
        if (rule?.UseGlossary != true || string.IsNullOrWhiteSpace(input)) return (input, "");
        var parts = GlossaryParts(input, reverse);
        var order = rule.Parts();
        int Rank(NsmTerm? term)
        {
            var index = Array.FindIndex(order, role => term?.Matches(role) == true);
            return index < 0 ? order.Length : index;
        }
        var unknown = parts.Where(p => p.Term == null).Select(p => p.Text).Distinct().ToArray();
        var findings = new List<string>();
        if (unknown.Length > 0) findings.Add("Outside glossary: " + string.Join(", ", unknown));
        if (!reverse)
        {
            var logicalType = type switch { "Table" => "Entity", "Column" => "Attribute", _ => type };
            var logical = Logical.FirstOrDefault(r => r.Type == logicalType && r.UseGlossary);
            if (logical != null && logical.Parts() is { Length: > 0 } sequence)
            {
                var ranks = parts.Select(p => Array.FindIndex(sequence, s => p.Term?.Matches(s) == true)).Where(n => n >= 0).ToArray();
                if (!ranks.SequenceEqual(ranks.Order())) findings.Add("Logical component order");
            }
        }
        if (parts.All(p => p.Term == null)) return (unmatched ?? input, string.Join("; ", findings));
        var name = string.Join(reverse ? " " : "_", parts.OrderBy(p => Rank(p.Term)).Select(p => p.Text));
        return (name, string.Join("; ", findings));
    }

    internal string CheckStandard(string name, string type, bool physical, bool checkPosition)
    {
        var parts = GlossaryParts(name, physical);
        var missing = parts.Where(p => p.Term == null).Select(p => p.Text).Distinct(StringComparer.OrdinalIgnoreCase);
        if (parts.Count == 0 || missing.Any())
            return "Outside the active glossary: " + (parts.Count == 0 ? name : string.Join(", ", missing));
        var roles = (physical ? Physical : Logical).FirstOrDefault(r => r.Type == type)?.Parts() ?? [];
        if (checkPosition && roles.Length > 0)
            for (int i = 0; i < parts.Count; i++)
                if (i >= roles.Length || !parts[i].Term!.Matches(roles[i]))
                    return i >= roles.Length ? $"Unexpected component at position {i + 1}." : $"Position {i + 1} requires {roles[i]}.";
        return "";
    }
}

internal static class NsmCsv
{
    internal static string Write(NsmProfile profile)
    {
        profile.Validate();
        var output = new StringBuilder();
        static string Escape(string s) => s.IndexOfAny([',','"', '\n','\r']) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        void Row(params string[] fields) => output.AppendLine(string.Join(",", fields.Select(Escape)));
        foreach (var t in profile.Glossary)
            Row(t.Words,t.Abbreviation,t.Alternate,t.Prime?"X":"",t.Modifier1?"X":"",t.Modifier2?"X":"",t.Class?"X":"",t.Definition);
        // An optional trailer carries the profile options alongside the headerless glossary rows.
        var options = JsonSerializer.SerializeToNode(profile)!.AsObject();
        options.Remove(nameof(NsmProfile.Glossary));
        Row("#NSM-settings-v1", options.ToJsonString());
        return output.ToString();
    }

    internal static NsmProfile Read(string csv, NsmProfile current)
    {
        var result = JsonSerializer.Deserialize<NsmProfile>(JsonSerializer.Serialize(current))!;
        var terms = new ObservableCollection<NsmTerm>();
        using var parser = new TextFieldParser(new StringReader(csv)) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        while (!parser.EndOfData)
        {
            var f = parser.ReadFields()!;
            if (f.Length == 2 && f[0] == "#NSM-settings-v1")
            { result = JsonSerializer.Deserialize<NsmProfile>(f[1]) ?? throw new InvalidDataException("Invalid NSM settings."); continue; }
            if (f.Length < 2 || f.Skip(8).Any(value => !string.IsNullOrWhiteSpace(value)))
                throw new InvalidDataException("Expected words, abbreviation and up to six optional glossary fields per CSV row.");
            f = f.Concat(Enumerable.Repeat("", Math.Max(0, 8 - f.Length))).ToArray();
            bool Flag(int i) => f[i].Trim().ToUpperInvariant() switch { "X" => true, "" => false, _ => throw new InvalidDataException("Use X or an empty field for component flags.") };
            terms.Add(new() { Words=f[0], Abbreviation=f[1], Alternate=f[2], Prime=Flag(3), Modifier1=Flag(4), Modifier2=Flag(5), Class=Flag(6), Definition=f[7] });
        }
        result.Glossary = terms;
        result.IsActive = true;
        result.Validate();
        return result;
    }
}
