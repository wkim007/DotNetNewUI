using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using static ErwinStudioSample.ModelJsonDocument;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private List<NsmProfile> _standards = [NsmProfile.Default()];
    private readonly Dictionary<(string Type, string Name, bool Reverse, string? Unmatched), string> _mappedNames = [];
    private NsmProfile? ActiveStandard => UseNsmToggle?.IsChecked == true ? _standards.FirstOrDefault(s => s.IsActive) : null;

    private string NsmName(string name, string type, string fallback, bool reverse = false, string? unmatched = null)
    {
        var active = ActiveStandard;
        if (active == null || reverse && !active.PhysicalToLogical ||
            !(reverse ? active.Logical : active.Physical).Any(r => r.Type == type && r.UseGlossary)) return fallback;
        var key = (type, name, reverse, unmatched);
        if (!_mappedNames.TryGetValue(key, out var mapped))
            _mappedNames[key] = mapped = active.ConvertName(name, type, reverse, unmatched).Name;
        return mapped;
    }

    private string ColumnPhysicalName(ColumnInfo c) => string.IsNullOrWhiteSpace(c.PhysicalName)
        ? NsmName(c.Name, "Column", c.ImportedPhysicalName ?? "", unmatched: c.ImportedPhysicalName) : c.PhysicalName;

    private void NsmChanged(object sender, RoutedEventArgs e) => RefreshNsm();

    private void RefreshNsm()
    {
        _mappedNames.Clear();
        if (_alternateViewModel?["meta"] is JsonObject meta) SaveNsm(meta);
        ApplyProjectViewMode();
        if (_selectedRelationshipKey is { } relationship) ShowRelationshipProperties(relationship);
        else if (_selectedAttributeRow?.Tag is AttributeSelection a && _columns.TryGetValue(a.EntityKey, out var fields) &&
            fields.FirstOrDefault(f => f.Name == a.AttributeName) is { } field) ShowAttributeProperties(a.EntityKey, field);
        else if (_selectedCard?.Tag is string key && _columns.ContainsKey(key)) SelectEntity(key);
    }

    private void SaveNsm(JsonObject meta)
    {
        meta["useNSM"] = UseNsmToggle.IsChecked == true;
        meta["namingStandards"] = JsonSerializer.SerializeToNode(_standards);
    }

    private static void ValidateStandards(IEnumerable<NsmProfile> profiles)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (profile == null) throw new InvalidDataException("Invalid naming standard.");
            profile.Validate();
            if (!names.Add(profile.Name.Trim())) throw new InvalidDataException("Standard names must be unique.");
        }
        if (profiles.Count(p => p.IsActive) > 1) throw new InvalidDataException("Choose one active standard.");
    }

    private void Nsm_Click(object sender, RoutedEventArgs args) => OpenNsmEditor();

    private void NsmTools_Click(object sender, RoutedEventArgs args)
    {
        var menu = new ContextMenu { PlacementTarget = (Button)sender };
        var options = new MenuItem { Header = "Model Naming Options" };
        var mapping = new MenuItem { Header = "Name Mapping" };
        mapping.Click += (_, _) => OpenNsmEditor(physicalRules: true);
        options.Items.Add(mapping);
        menu.Items.Add(options);
        menu.IsOpen = true;
    }

    private void OpenNsmEditor(bool create = false, bool physicalRules = false)
    {
        var profiles = JsonSerializer.Deserialize<ObservableCollection<NsmProfile>>(JsonSerializer.Serialize(_standards))!;
        var dialog = new Window { Title = "Naming Standard Editor", Owner = this, Width = 930, Height = 640, MinWidth = 640,
            MinHeight = 440, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new Thickness(12) };
        dialog.Content = root;
        StackPanel Bar(Dock side)
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,5,0,5) };
            DockPanel.SetDock(bar, side); root.Children.Add(bar); return bar;
        }
        var top = Bar(Dock.Top);
        var bottom = Bar(Dock.Bottom);
        var status = new TextBlock { Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        static DataGrid Grid(bool editRows = false) => new() { AutoGenerateColumns = false, CanUserAddRows = editRows,
            CanUserDeleteRows = editRows, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column };
        static Binding Bind(string prop) => new(prop) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
        static DataGridTextColumn TextCol(string title, string prop) => new() { Header = title, Binding = Bind(prop), Width = DataGridLength.SizeToHeader, MinWidth = 92 };
        static DataGridCheckBoxColumn CheckCol(string title, string prop) => new() { Header = title, Binding = Bind(prop) };
        var list = Grid();
        list.Height = 150; list.Margin = new Thickness(0,0,0,8);
        list.Columns.Add(TextCol("Name", "Name")); list.Columns[0].Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        list.Columns.Add(CheckCol("Match whole words only", "WholeWords"));
        list.Columns.Add(CheckCol("Match whole name only", "WholeName"));
        var activeCell = new FrameworkElementFactory(typeof(CheckBox));
        activeCell.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        activeCell.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsActive") { Mode = BindingMode.OneWay });
        activeCell.AddHandler(CheckBox.ClickEvent, new RoutedEventHandler((s, _) =>
        {
            var box = (CheckBox)s;
            var chosen = (NsmProfile)box.DataContext;
            var active = box.IsChecked == true;
            list.CommitEdit(DataGridEditingUnit.Row, true);
            foreach (var p in profiles) p.IsActive = active && p == chosen;
            list.SelectedItem = chosen; list.Items.Refresh();
        }));
        list.Columns.Add(new DataGridTemplateColumn { Header = "Is Active", CellTemplate = new DataTemplate { VisualTree = activeCell } });
        list.Columns.Add(CheckCol("Physical to logical", "PhysicalToLogical"));
        list.ItemsSource = profiles; DockPanel.SetDock(list,Dock.Top); root.Children.Add(list);
        var tabs = new TabControl();
        root.Children.Add(tabs);
        void Tab(string name, FrameworkElement content) => tabs.Items.Add(new TabItem { Header = name, Content = content });
        DataGrid Rules(string tab)
        {
            var grid = Grid();
            grid.Columns.Add(TextCol("Type", "Type")); grid.Columns[0].IsReadOnly = true;
            grid.Columns.Add(CheckCol("Use glossary", "UseGlossary"));
            foreach (var part in new[] { "Part1","Part2","Part3","Part4" })
                grid.Columns.Add(new DataGridComboBoxColumn { Header=part, ItemsSource=new[] {"","Prime","Modifier 1","Modifier 2","Class"}, SelectedItemBinding=Bind(part), Width=105 });
            grid.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(tab)); Tab(tab,grid); return grid;
        }
        var logical = Rules("Logical"); var physical = Rules("Physical");
        var glossary = Grid(true);
        glossary.Columns.Add(TextCol("Word / Words","Words")); glossary.Columns.Add(TextCol("Abbreviation","Abbreviation"));
        glossary.Columns.Add(TextCol("Alt abbreviation","Alternate"));
        foreach (var (label,prop) in new[] { ("P","Prime"),("M1","Modifier1"),("M2","Modifier2"),("C","Class") })
            glossary.Columns.Add(CheckCol(label,prop));
        glossary.Columns.Add(TextCol("Definition","Definition"));
        glossary.SetBinding(ItemsControl.ItemsSourceProperty,new Binding("Glossary")); Tab("Glossary",glossary);
        foreach (var (label,prop) in new[] { ("Definition","Definition"),("Notes","Notes"),("Extended notes","ExtendedNotes") })
        {
            var box = new TextBox { AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, Padding=new Thickness(8) };
            box.SetBinding(TextBox.TextProperty,Bind(prop)); Tab(label,box);
        }
        var compliance = Grid(); compliance.IsReadOnly = true;
        foreach (var (label,prop) in new[] { ("Type","Type"),("Owner","Owner"),("Logical","Logical"),("Current physical","Current"),("Standard physical","Expected"),("Compliance","Finding") })
            compliance.Columns.Add(TextCol(label,prop));
        Tab("Compliance",compliance);
        void Commit()
        {
            foreach (var grid in new[] {list,logical,physical,glossary})
                if (!grid.CommitEdit(DataGridEditingUnit.Cell,true) || !grid.CommitEdit(DataGridEditingUnit.Row,true))
                    throw new InvalidDataException("Finish editing the current row.");
        }
        void Review()
        {
            compliance.ItemsSource = null;
            if (list.SelectedItem is not NsmProfile p || tabs.SelectedIndex != tabs.Items.Count-1) return;
            if (UseNsmToggle.IsChecked != true) { status.Text = "Enable Use NSM to check compliance."; return; }
            try { Commit(); p.Validate(); compliance.ItemsSource = NamingCompliance(ExportModelJson(),p); status.Text = ""; }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        list.SelectionChanged += (_, e) => { if (e.OriginalSource == list) { tabs.DataContext=list.SelectedItem; tabs.IsEnabled=list.SelectedItem != null; Review(); } };
        tabs.SelectionChanged += (_, e) => { if (e.OriginalSource == tabs) Review(); };
        void Button(Panel bar,string title,Action action)
        {
            var b = new Button {Content=title, Padding=new Thickness(10,4,10,4), Margin=new Thickness(2)};
            b.Click += (_,_) => { try { Commit(); action(); } catch(Exception ex) { status.Text=ex.Message; } }; bar.Children.Add(b);
        }
        void NewStandard()
        {
            var p = new NsmProfile { IsActive = profiles.Count == 0 };
            int n=1; while (profiles.Any(s => s.Name == $"Naming Standard {n}")) n++;
            p.Name=$"Naming Standard {n}"; profiles.Add(p); list.SelectedItem=p;
        }
        Button(top,"New",NewStandard);
        Button(top,"Delete",() => { if (list.SelectedItem is NsmProfile p) { profiles.Remove(p); list.SelectedItem=profiles.FirstOrDefault(); } });
        NsmProfile Active() => profiles.FirstOrDefault(p=>p.IsActive) ?? throw new InvalidDataException("Select Is Active for the standard to save or load.");
        Button(top,"Save active CSV",() => {
            var p=Active(); var csv=NsmCsv.Write(p); var pick=new SaveFileDialog { Filter="Naming standard CSV|*.csv", DefaultExt=".csv" };
            if(pick.ShowDialog(dialog)==true) { File.WriteAllText(pick.FileName,csv,new UTF8Encoding(false)); status.Text="Saved "+p.Name; }
        });
        Button(top,"Load active CSV",() => {
            var p=Active(); var pick=new OpenFileDialog { Filter="Naming standard CSV|*.csv" };
            if(pick.ShowDialog(dialog)!=true) return;
            var imported=NsmCsv.Read(File.ReadAllText(pick.FileName),p);
            var choice = MessageBox.Show(dialog,
                $"Merge CSV entries into the glossary for '{p.Name}'?\n\nYes merges entries and uses the CSV value for matching words.\nNo replaces the glossary with the CSV entries.",
                "Load naming standard CSV", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel || choice == MessageBoxResult.None) return;
            if (choice == MessageBoxResult.Yes) imported.MergeGlossary(p);
            if(profiles.Any(other=>other!=p && other.Name.Equals(imported.Name,StringComparison.OrdinalIgnoreCase))) imported.Name=p.Name;
            profiles[profiles.IndexOf(p)]=imported; list.SelectedItem=imported; status.Text="Loaded "+imported.Name;
        });
        Button(bottom,"OK",() => { ValidateStandards(profiles); _standards=profiles.ToList(); RefreshNsm(); dialog.DialogResult=true; });
        Button(bottom,"Cancel",() => dialog.DialogResult=false);
        list.SelectedItem=profiles.FirstOrDefault(p=>p.IsActive) ?? profiles.FirstOrDefault();
        tabs.IsEnabled=list.SelectedItem!=null;
        if (create) NewStandard();
        if (physicalRules) tabs.SelectedIndex = 1;
        dialog.ShowDialog();
    }

    private sealed record NamingCheck(string Type,string Owner,string Logical,string Current,string Expected,string Finding);

    private static List<NamingCheck> NamingCompliance(JsonObject model,NsmProfile profile)
    {
        var rows = new List<NamingCheck>();
        void Add(string type,string owner,JsonObject item)
        {
            if (!profile.Physical.Any(rule => rule.Type == type && rule.UseGlossary)) return;
            var name=Text(item,"name"); var current=Text(item,"physicalName");
            var (expected,finding)=profile.ConvertName(name,type);
            if (current!=expected) finding = string.Join("; ", new[] {finding,"Physical name differs"}.Where(s=>s.Length>0));
            rows.Add(new(type,owner,name,current,expected,finding.Length==0?"Compliant":finding));
        }
        foreach(var kind in Kinds)
        foreach(var obj in Items(model["workspace"]?[kind]))
        {
            Add("Table","",obj);
            foreach(var col in Items(obj["attributes"])) Add("Column",Text(obj,"name"),col);
        }
        foreach(var link in Items(model["workspace"]?["relationships"])) Add("Relationship","",link);
        return rows;
    }
}
