using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private sealed class SubjectAreaState
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public required string Name { get; init; }
        public bool Locked { get; init; }
        public DiagramState? LastDiagram { get; set; }
    }
    private readonly ObservableCollection<SubjectAreaState> _subjectAreas =
        [new SubjectAreaState { Id = "0", Name = "<model>", Locked = true }];
    private string _activeSubjectAreaId = "0";
    private IEnumerable<DiagramState> SubjectDiagrams => _diagrams.Where(d => d.SubjectAreaId == _activeSubjectAreaId);

    private void InitializeSubjectAreas()
    {
        ProjectSubjectBox.ItemsSource = _subjectAreas;
        ProjectSubjectBox.SelectedItem = _subjectAreas[0];
    }

    private void SubjectArea_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectSubjectBox.SelectedItem is not SubjectAreaState area || area.Id == _activeSubjectAreaId) return;
        var previous = _subjectAreas.First(a => a.Id == _activeSubjectAreaId);
        previous.LastDiagram = _activeDiagram;
        _activeSubjectAreaId = area.Id;
        var target = area.LastDiagram is { } last && _diagrams.Contains(last) ? last : SubjectDiagrams.FirstOrDefault();
        if (target is null) AddDiagram_Click(this, new RoutedEventArgs());
        else SwitchDiagram(target);
    }

    private void AddSubjectArea_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Window { Title = "Add Subject Area", Owner = this, Width = 380, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = ThemeBrush("#142230") };
        var panel = new StackPanel { Margin = new Thickness(18) };
        dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Subject Area name", Foreground = ThemeInk });
        var name = new TextBox { Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(8) };
        panel.Children.Add(name);
        var error = new TextBlock { Foreground = ThemeBrush("#F06D82"), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Style = (Style)FindResource("ProjectSettingsButton"), Margin = new Thickness(0, 0, 8, 0) };
        var add = new Button { Content = "Add", IsDefault = true, Style = (Style)FindResource("ProjectSettingsButton") };
        buttons.Children.Add(cancel); buttons.Children.Add(add); panel.Children.Add(buttons);
        add.Click += (_, _) =>
        {
            var value = name.Text.Trim();
            if (value.Length == 0 || value.Length > 100) { error.Text = "Enter a name between 1 and 100 characters."; return; }
            if (_subjectAreas.Any(a => a.Name.Equals(value, StringComparison.OrdinalIgnoreCase))) { error.Text = "A subject area with this name already exists."; return; }
            var area = new SubjectAreaState { Name = value };
            _subjectAreas.Add(area);
            ProjectSubjectBox.SelectedItem = area;
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => name.Focus();
        dialog.ShowDialog();
    }

    private object CaptureSubjectAreas()
    {
        SaveActiveDiagram();
        double Finite(double v) => double.IsFinite(v) ? v : 0;
        return _subjectAreas.Select(area => new
        {
            id = area.Id, name = area.Name, locked = area.Locked,
            diagrams = _diagrams.Where(d => d.SubjectAreaId == area.Id).Select(d =>
            {
                object[] Shapes(Func<string, bool> include) => d.Elements.OfType<Border>()
                    .Where(c => c.Tag is string key && d.Columns.ContainsKey(key) && include(key))
                    .Select(c => { var key = (string)c.Tag; return (object)new {
                        id = key, name = EntityDisplayName(key), x = Finite(Canvas.GetLeft(c)), y = Finite(Canvas.GetTop(c)),
                        width = Finite(c.ActualWidth), height = Finite(c.ActualHeight),
                        description = _objectDefinitions.GetValueOrDefault(key, ""), attributes = d.Columns[key].ToArray()
                    }; }).ToArray();
                return new { id = d.Id, name = d.Title, modelShapes = new {
                    entities = Shapes(d.Entities.Contains),
                    views = Shapes(k => d.Views.Contains(k) && !d.MaterializedViews.Contains(k)),
                    cachedViews = Shapes(d.MaterializedViews.Contains)
                }};
            }).ToArray()
        }).ToArray();
    }
}
