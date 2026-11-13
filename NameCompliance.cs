using System.Windows;
using System.Windows.Controls;
using static ErwinStudioSample.ModelJsonDocument;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private sealed record ComplianceName(string Type, string Scope, string Name, Action<string> Rename);

    private List<ComplianceName> ComplianceNames(bool physical, bool objects, bool attributes, bool domains)
    {
        SaveActiveDiagram();
        var names = new List<ComplianceName>();
        var seen = new HashSet<string>();
        foreach (var diagram in _diagrams)
        foreach (var (key, columns) in diagram.Columns)
        {
            if ((!physical && diagram.Views.Contains(key)) || !seen.Add(key)) continue;
            if (objects)
                names.Add(new(physical ? "Table" : "Entity", "", physical ? ObjectPhysicalName(key) : EntityDisplayName(key),
                    value => (physical ? _objectPhysicalNames : _entityDisplayNames)[key] = value));
            if (!attributes) continue;
            foreach (var column in columns)
            {
                var id = column.Id;
                var name = physical ? CanvasName(column.Name, ColumnPhysicalName(column)) : column.Name;
                names.Add(new(physical ? "Column" : "Attribute", key, name, value =>
                {
                    var index = columns.ToList().FindIndex(c => c.Id == id);
                    var current = columns[index];
                    columns[index] = physical ? current with { PhysicalName = value } : current with { Name = value };
                    if (_columns.ContainsKey(key)) RebuildNestedRows(key);
                }));
            }
        }
        if (domains)
            foreach (var domain in Items(_modelJsonSource?["workspace"]?["domains"]))
            {
                var name = physical ? Text(domain, "physicalName") : "";
                if (string.IsNullOrWhiteSpace(name)) name = Text(domain, "name");
                names.Add(new("Domain", "", name, value => domain[physical ? "physicalName" : "name"] = value));
            }
        return names;
    }

    private void NameCompliance_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveStandard is not { } profile)
        {
            MessageBox.Show(this, "Select an active naming standard in NSM.", "Name Standard Compliance");
            return;
        }
        bool physical = ViewModeBox.SelectedIndex == 0;
        var dialog = new Window { Owner = this, Title = "Check Name Standards Compliance", Width = 490,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(16) };
        dialog.Content = panel;
        TextBlock Label(string text)
        {
            var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) };
            panel.Children.Add(label);
            return label;
        }
        CheckBox Option(string title, bool enabled)
        {
            var box = new CheckBox { Content = title, IsChecked = enabled, Margin = new Thickness(0, 3, 0, 3) };
            panel.Children.Add(box);
            return box;
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Button(string title, Action action)
        {
            var button = new Button { Content = title, Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(2) };
            button.Click += (_, _) => action();
            buttons.Children.Add(button);
        }
        Label($"Select objects to check against {profile.Name}");
        var objects = Option(physical ? "Tables" : "Entities", true);
        var attributes = Option(physical ? "Columns" : "Attributes", true);
        var domains = Option("Domains", true);
        var positions = Option("Check Word Order and Position", false);
        panel.Children.Add(buttons);
        var pending = new List<ComplianceName>();
        var currentNames = new Dictionary<ComplianceName, string>();
        void ShowFinding()
        {
            panel.Children.Clear();
            buttons.Children.Clear();
            if (pending.Count == 0)
            {
                Label("Name check finished!");
                panel.Children.Add(buttons);
                Button("Finish", dialog.Close);
                return;
            }
            var item = pending[0];
            var reason = profile.CheckStandard(item.Name, item.Type, physical, positions.IsChecked == true);
            Label($"{item.Type} · {reason}");
            Label("Non-conforming name");
            panel.Children.Add(new TextBox { Text = item.Name, IsReadOnly = true });
            Label("Replace the name with");
            var replacement = new TextBox();
            panel.Children.Add(replacement);
            var error = Label("");
            panel.Children.Add(buttons);
            void Advance(bool all, bool replace)
            {
                var targets = all ? pending.Where(n => n.Type == item.Type && string.Equals(n.Name, item.Name, StringComparison.OrdinalIgnoreCase)).ToList() : [item];
                if (replace)
                {
                    var value = replacement.Text.Trim();
                    var violation = profile.CheckStandard(value, item.Type, physical, positions.IsChecked == true);
                    if (violation.Length > 0) { error.Text = violation; return; }
                    if (targets.GroupBy(n => n.Scope).Any(g => g.Count() > 1) ||
                        targets.Any(t => currentNames.Any(p => p.Key != t && p.Key.Scope == t.Scope && p.Key.Type == t.Type &&
                            string.Equals(p.Value, value, StringComparison.OrdinalIgnoreCase))))
                    { error.Text = "That name already exists in the same scope."; return; }
                    foreach (var target in targets) { target.Rename(value); currentNames[target] = value; }
                    RefreshNsm();
                    RefreshModelExplorer();
                }
                foreach (var target in targets) pending.Remove(target);
                ShowFinding();
            }
            Button("Ignore", () => Advance(false, false));
            Button("Ignore All", () => Advance(true, false));
            Button("Replace", () => Advance(false, true));
            Button("Replace All", () => Advance(true, true));
            Button("Cancel", dialog.Close);
        }
        Button("Start", () =>
        {
            foreach (var item in ComplianceNames(physical, objects.IsChecked == true, attributes.IsChecked == true, domains.IsChecked == true))
            {
                currentNames.Add(item, item.Name);
                if (profile.CheckStandard(item.Name, item.Type, physical, positions.IsChecked == true).Length > 0) pending.Add(item);
            }
            ShowFinding();
        });
        Button("Cancel", dialog.Close);
        dialog.ShowDialog();
    }
}
