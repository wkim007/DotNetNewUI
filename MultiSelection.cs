using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private readonly HashSet<Border> _selectedCards = [];
    private readonly Dictionary<FrameworkElement, Point> _groupDragOrigins = [];
    private Rectangle? _selectionBox;
    private Point _selectionStart;
    private HashSet<FrameworkElement> _selectionBase = [];

    private void HighlightCard(Border card)
    {
        _selectedCards.Add(card); _selectedCard = card;
        UpdateAiSelectionCounts();
        card.BorderBrush = ThemeBrush("#F1BF52"); card.BorderThickness = new Thickness(2); Panel.SetZIndex(card, 10);
    }
    private void CaptureGroupDragOrigins()
    {
        _groupDragOrigins.Clear();
        foreach (var item in SelectedMovableObjects()) _groupDragOrigins[item] = new Point(Canvas.GetLeft(item), Canvas.GetTop(item));
    }

    private bool MoveSelectedGroup(Vector delta)
    {
        if (_groupDragOrigins.Count <= 1) return false;
        delta.X = Math.Max(delta.X, 8 - _groupDragOrigins.Values.Min(p => p.X));
        delta.Y = Math.Max(delta.Y, 8 - _groupDragOrigins.Values.Min(p => p.Y));
        foreach (var (item, origin) in _groupDragOrigins)
        {
            Canvas.SetLeft(item, origin.X + delta.X);
            Canvas.SetTop(item, origin.Y + delta.Y);
        }
        UpdateRelationshipLines();
        return true;
    }

    private void InitializeMultiSelection()
    {
        DiagramCanvasSurface.Focusable = true;
        PreviewKeyDown += DiagramDelete_KeyDown;
        DiagramCanvasSurface.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || _pendingRelationshipType is not null || _connectorSource is not null) return;
            var card = DiagramCanvasSurface.Children.OfType<Border>().FirstOrDefault(c => c.IsMouseOver && c.Tag is string key && IsAiModelObject(key));
            if (card is null) return;
            DiagramCanvasSurface.Focus();
            ClearAttributeSelection(); ClearRelationshipSelection(); EntityPropertiesPanel.Visibility = Visibility.Collapsed;
            if (_selectedCards.Remove(card)) { card.ClearValue(Border.BorderBrushProperty); card.ClearValue(Border.BorderThicknessProperty); Panel.SetZIndex(card, 8); _selectedCard = _selectedCards.LastOrDefault(); }
            else HighlightCard(card);
            UpdateAiSelectionCounts();
            StatusText.Text = $"Selected {_selectedCards.Count} objects"; e.Handled = true;
        };
        DiagramCanvasSurface.MouseMove += (_, e) =>
        {
            if (_selectionBox is null || e.LeftButton != MouseButtonState.Pressed) return;
            var rect = new Rect(_selectionStart, e.GetPosition(DiagramCanvasSurface));
            Canvas.SetLeft(_selectionBox, rect.Left); Canvas.SetTop(_selectionBox, rect.Top); _selectionBox.Width = rect.Width; _selectionBox.Height = rect.Height;
            SelectMarqueeObjects(rect);
        };
        DiagramCanvasSurface.MouseLeftButtonUp += (_, _) => EndMarquee();
        DiagramCanvasSurface.LostMouseCapture += (_, _) => { if (_selectionBox is not null) EndMarquee(); };
    }
    private readonly Dictionary<FrameworkElement, (Path Outline, Brush Stroke, double Width)> _marqueeStrokes = [];

    private IEnumerable<FrameworkElement> SelectedMovableObjects() =>
        _selectedCards.Cast<FrameworkElement>().Concat(_marqueeStrokes.Keys.OfType<Grid>());

    private void HighlightMarqueeItem(FrameworkElement item)
    {
        if (item is Border card) { HighlightCard(card); return; }
        var outline = item is Grid drawing ? drawing.Children.OfType<Path>().First() : (Path)item;
        if (!_marqueeStrokes.TryAdd(item, (outline, outline.Stroke, outline.StrokeThickness))) return;
        outline.Stroke = ThemeBrush("#F1BF52");
        outline.StrokeThickness = Math.Max(3, outline.StrokeThickness + 1);
    }

    private void ClearMarqueeHighlights()
    {
        foreach (var (outline, stroke, width) in _marqueeStrokes.Values)
        {
            outline.Stroke = stroke;
            outline.StrokeThickness = width;
        }
        _marqueeStrokes.Clear();
    }

    private void SelectMarqueeObjects(Rect rect)
    {
        ClearCardSelection();
        foreach (var item in DiagramCanvasSurface.Children.OfType<FrameworkElement>())
        {
            if (item.Visibility != Visibility.Visible) continue;
            Rect bounds;
            if (item is Border { Tag: string key } && _columns.ContainsKey(key) || item is Grid { Tag: "DrawingShape" })
                bounds = new Rect(Canvas.GetLeft(item), Canvas.GetTop(item), item.ActualWidth, item.ActualHeight);
            else if (item is Path line && (line.Tag is DrawingConnection ||
                line.Tag is string link && !_deletedRelationships.Contains(link) && FindRelationshipLine(link) == line) &&
                line.Data is { } geometry && !geometry.Bounds.IsEmpty)
                bounds = line.TransformToAncestor(DiagramCanvasSurface).TransformBounds(geometry.Bounds);
            else continue;
            if (_selectionBase.Contains(item) || rect.Contains(bounds)) HighlightMarqueeItem(item);
        }
        StatusText.Text = $"Selected {_selectedCards.Count + _marqueeStrokes.Count} objects";
    }

    private void BeginMarquee(MouseButtonEventArgs e)
    {
        DiagramCanvasSurface.Focus();
        _selectionBase = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? SelectedMovableObjects().Concat(_marqueeStrokes.Keys.OfType<Path>()).ToHashSet() : [];
        ClearAttributeSelection(); ClearCardSelection(); ClearRelationshipSelection(); EntityPropertiesPanel.Visibility = Visibility.Collapsed;
        foreach (var item in _selectionBase) HighlightMarqueeItem(item);
        _selectionStart = e.GetPosition(DiagramCanvasSurface);
        _selectionBox = new Rectangle { Stroke = ThemeBrush("#D6BF61"), StrokeThickness = 1, Fill = new SolidColorBrush(Color.FromArgb(35, 240, 220, 110)), IsHitTestVisible = false };
        Panel.SetZIndex(_selectionBox, 100); DiagramCanvasSurface.Children.Add(_selectionBox); DiagramCanvasSurface.CaptureMouse(); e.Handled = true;
    }
    private void EndMarquee()
    {
        if (_selectionBox is null) return;
        DiagramCanvasSurface.Children.Remove(_selectionBox); _selectionBox = null;
        DiagramCanvasSurface.ReleaseMouseCapture();
    }
}
