using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private readonly List<DiagramState> _diagrams = [];
    private DiagramState? _activeDiagram;
    private int _diagramNumber;

    private sealed class DiagramState
    {
        public required string Title { get; set; }
        public List<UIElement> Elements { get; set; } = [];
        public Dictionary<string, ObservableCollection<ColumnInfo>> Columns { get; set; } = [];
        public Dictionary<string, Border> Cards { get; set; } = [];
        public HashSet<string> Entities { get; set; } = [];
        public HashSet<string> Views { get; set; } = [];
        public Dictionary<string, DynamicRelationship> Relationships { get; set; } = [];
        public Dictionary<string, TextBlock> Labels { get; set; } = [];
        public Dictionary<string, Vector> Offsets { get; set; } = [];
        public HashSet<string> Deleted { get; set; } = [];
        public Dictionary<string, (Path Line, Path Marker)> Endpoints { get; set; } = [];
        public int Zoom { get; set; } = 100;
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
    }

    private void InitializeDiagrams()
    {
        if (_activeDiagram is not null) return;
        _activeDiagram = new DiagramState { Title = $"ER_Diagram_{++_diagramNumber}" };
        _diagrams.Add(_activeDiagram);
        SaveActiveDiagram(); RenderDiagramTabs();
    }

    private bool IsSharedDiagramElement(UIElement element) =>
        element == RelationshipMoveHandle || element == RelationshipDeleteButton || element == EmptyHint;

    private void SaveActiveDiagram()
    {
        if (_activeDiagram is not { } diagram) return;
        diagram.Elements = DiagramCanvasSurface.Children.Cast<UIElement>().Where(e => !IsSharedDiagramElement(e)).ToList();
        diagram.Columns = new(_columns); diagram.Cards = new(_dynamicCards);
        diagram.Entities = new(_entityCardKeys); diagram.Views = new(_viewCardKeys);
        diagram.Relationships = new(_dynamicRelationships); diagram.Labels = new(_relationshipLabels);
        diagram.Offsets = new(_relationshipOffsets); diagram.Deleted = new(_deletedRelationships);
        diagram.Endpoints = new(_notationEndpoints); diagram.Zoom = _zoom;
        diagram.HorizontalOffset = DiagramScrollViewer.HorizontalOffset;
        diagram.VerticalOffset = DiagramScrollViewer.VerticalOffset;
    }

    private static void RestoreMap<T>(Dictionary<string, T> target, Dictionary<string, T> source)
    {
        target.Clear(); foreach (var pair in source) target.Add(pair.Key, pair.Value);
    }

    private void SwitchDiagram(DiagramState diagram)
    {
        if (_activeDiagram == diagram) return;
        _entityHoldTimer.Stop(); _entityHoldCard = null; _draggedCard = null; _attributeDragRow = null;
        _draggingRelationship = false; Mouse.Capture(null);
        _pendingRelationshipType = null; _relationshipSourceKey = null;
        ClearAttributeSelection(); ClearCardSelection(); ClearRelationshipSelection();
        SaveActiveDiagram();
        foreach (var element in DiagramCanvasSurface.Children.Cast<UIElement>().Where(e => !IsSharedDiagramElement(e)).ToList())
            DiagramCanvasSurface.Children.Remove(element);
        _activeDiagram = diagram;
        RestoreMap(_columns, diagram.Columns); RestoreMap(_dynamicCards, diagram.Cards);
        RestoreMap(_dynamicRelationships, diagram.Relationships); RestoreMap(_relationshipLabels, diagram.Labels);
        RestoreMap(_relationshipOffsets, diagram.Offsets); RestoreMap(_notationEndpoints, diagram.Endpoints);
        _entityCardKeys.Clear(); _entityCardKeys.UnionWith(diagram.Entities);
        _viewCardKeys.Clear(); _viewCardKeys.UnionWith(diagram.Views);
        _deletedRelationships.Clear(); _deletedRelationships.UnionWith(diagram.Deleted);
        foreach (var element in diagram.Elements) DiagramCanvasSurface.Children.Add(element);
        SelectedEntityTitle.Text = "NO SELECTION"; EntityNameBox.Text = ""; PhysicalNameBox.Text = ""; ColumnsGrid.ItemsSource = null;
        EmptyHint.Visibility = Visibility.Collapsed;
        SetZoom(diagram.Zoom);
        UpdateRelationshipLines(); ApplyProjectViewMode(); ApplyTheme(); RenderDiagramTabs();
        // Populate the explorer from the active diagram instead of retaining stale entity entries.
        ModelTree.Items.Clear();
        var root = new TreeViewItem { Header = diagram.Title, IsExpanded = true };
        foreach (var key in _columns.Keys) root.Items.Add(new TreeViewItem { Header = key, Tag = key });
        ModelTree.Items.Add(root);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_activeDiagram != diagram) return;
            DiagramScrollViewer.ScrollToHorizontalOffset(diagram.HorizontalOffset);
            DiagramScrollViewer.ScrollToVerticalOffset(diagram.VerticalOffset);
            UpdateRelationshipLines();
        }));
        StatusText.Text = $"Selected {diagram.Title}";
    }

    private void AddDiagram_Click(object sender, RoutedEventArgs e)
    {
        var diagram = new DiagramState { Title = $"ER_Diagram_{++_diagramNumber}" };
        _diagrams.Add(diagram); SwitchDiagram(diagram);
        StatusText.Text = $"Created {diagram.Title}";
    }

    private void RemoveDiagram(DiagramState diagram)
    {
        if (_diagrams.Count <= 1)
        {
            StatusText.Text = "At least one diagram must remain";
            return;
        }
        var index = _diagrams.IndexOf(diagram);
        if (index < 0) return;
        if (_activeDiagram == diagram)
            SwitchDiagram(_diagrams[index > 0 ? index - 1 : 1]);
        _diagrams.Remove(diagram);
        RenderDiagramTabs();
        StatusText.Text = $"Removed {diagram.Title}";
    }

    private Grid CreateDiagramNameEditor(DiagramState diagram, Button select, Button close)
    {
        var host = new Grid();
        var editor = new TextBox
        {
            Visibility = Visibility.Collapsed, MinWidth = 110, MaxWidth = 260,
            Margin = new Thickness(8, 2, 0, 2), Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(14, 24, 36)),
            Foreground = Brushes.White, CaretBrush = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(98, 230, 179))
        };
        System.Windows.Automation.AutomationProperties.SetName(editor, "Diagram name");
        host.Children.Add(select); host.Children.Add(editor);
        select.ToolTip = "Hold for 2 seconds to rename";
        var hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        var editing = false;
        var origin = new Point();
        void Finish(bool save)
        {
            if (!editing) return;
            editing = false;
            var name = editor.Text.Trim();
            if (save && name.Length > 0)
            {
                diagram.Title = name;
                select.Content = name;
                if (_diagrams.Count > 1) close.ToolTip = $"Remove {name}";
                if (_activeDiagram == diagram && ModelTree.Items.Count > 0 && ModelTree.Items[0] is TreeViewItem root)
                    root.Header = name;
                StatusText.Text = $"Renamed diagram: {name}";
            }
            editor.Visibility = Visibility.Collapsed;
            select.Visibility = Visibility.Visible;
        }
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            if (Mouse.LeftButton != MouseButtonState.Pressed || !select.IsMouseOver || !select.IsLoaded) return;
            editing = true;
            select.ReleaseMouseCapture();
            editor.Text = diagram.Title;
            select.Visibility = Visibility.Collapsed;
            editor.Visibility = Visibility.Visible;
            editor.Focus(); Keyboard.Focus(editor); editor.SelectAll();
            StatusText.Text = "Edit diagram name — Enter to save, Escape to cancel";
        };
        select.PreviewMouseLeftButtonDown += (_, args) =>
        {
            origin = args.GetPosition(host); hold.Stop(); hold.Start();
        };
        select.PreviewMouseMove += (_, args) =>
        {
            if ((args.GetPosition(host) - origin).Length > 5 || args.LeftButton != MouseButtonState.Pressed) hold.Stop();
        };
        select.PreviewMouseLeftButtonUp += (_, _) => hold.Stop();
        select.MouseLeave += (_, _) => hold.Stop();
        select.LostMouseCapture += (_, _) => hold.Stop();
        host.Unloaded += (_, _) => { hold.Stop(); Finish(true); };
        editor.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter && args.Key != Key.Escape) return;
            Finish(args.Key == Key.Enter); select.Focus(); args.Handled = true;
        };
        editor.LostKeyboardFocus += (_, _) => Finish(true);
        return host;
    }
    private void RenderDiagramTabs()
    {
        // Both controls share one outer surface; the close button has no separate box.
        var buttonStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                <Setter Property="Foreground" Value="#DCE7F5"/>
                <Setter Property="FontWeight" Value="SemiBold"/>
                <Setter Property="Cursor" Value="Hand"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="Button">
                            <Border Background="Transparent" Padding="{TemplateBinding Padding}">
                                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="#A7FFD1"/></Trigger>
                                <Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="Foreground" Value="#A7FFD1"/></Trigger>
                                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.35"/></Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            """);
        DiagramTabs.Children.Clear();
        foreach (var diagram in _diagrams)
        {
            var tab = new StackPanel { Orientation = Orientation.Horizontal };
            var select = new Button { Content = diagram.Title, Style = buttonStyle, Padding = new Thickness(12, 6, 4, 6) };
            select.Click += (_, _) => SwitchDiagram(diagram);
            var close = new Button
            {
                Content = "×", Style = buttonStyle, Padding = new Thickness(5, 6, 10, 6),
                IsEnabled = _diagrams.Count > 1,
                ToolTip = _diagrams.Count > 1 ? $"Remove {diagram.Title}" : "At least one diagram must remain"
            };
            ToolTipService.SetShowOnDisabled(close, true);
            close.Click += (_, _) => RemoveDiagram(diagram);
            tab.Children.Add(CreateDiagramNameEditor(diagram, select, close)); tab.Children.Add(close);
            DiagramTabs.Children.Add(new Border
            {
                Child = tab, Margin = new Thickness(5, 1, 3, 1), CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(diagram == _activeDiagram ? Color.FromRgb(35, 58, 83) : Color.FromRgb(25, 39, 56)),
                BorderBrush = new SolidColorBrush(diagram == _activeDiagram ? Color.FromRgb(64, 93, 123) : Color.FromRgb(25, 39, 56))
            });
        }
    }
}