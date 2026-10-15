using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private readonly HashSet<Border> _selectedCards = [];
    private readonly Dictionary<Border, Point> _groupDragOrigins = [];
    private Rectangle? _selectionBox;
    private Point _selectionStart;
    private HashSet<Border> _selectionBase = [];

    private void HighlightCard(Border card)
    {
        _selectedCards.Add(card); _selectedCard = card;
        UpdateAiSelectionCounts();
        card.BorderBrush = ThemeBrush("#F1BF52"); card.BorderThickness = new Thickness(2); Panel.SetZIndex(card, 10);
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
            if (_selectedCards.Remove(card)) { card.ClearValue(Border.BorderBrushProperty); card.ClearValue(Border.BorderThicknessProperty); Panel.SetZIndex(card, 1); _selectedCard = _selectedCards.LastOrDefault(); }
            else HighlightCard(card);
            UpdateAiSelectionCounts();
            StatusText.Text = $"Selected {_selectedCards.Count} objects"; e.Handled = true;
        };
        DiagramCanvasSurface.MouseMove += (_, e) =>
        {
            if (_selectionBox is null || e.LeftButton != MouseButtonState.Pressed) return;
            var rect = new Rect(_selectionStart, e.GetPosition(DiagramCanvasSurface));
            Canvas.SetLeft(_selectionBox, rect.Left); Canvas.SetTop(_selectionBox, rect.Top); _selectionBox.Width = rect.Width; _selectionBox.Height = rect.Height;
            ClearCardSelection();
            foreach (var card in DiagramCanvasSurface.Children.OfType<Border>().Where(c => c.Tag is string key && IsAiModelObject(key)))
                if (_selectionBase.Contains(card) || rect.Contains(new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.ActualWidth, card.ActualHeight))) HighlightCard(card);
            UpdateAiSelectionCounts();
            StatusText.Text = $"Selected {_selectedCards.Count} objects";
        };
        DiagramCanvasSurface.MouseLeftButtonUp += (_, _) => EndMarquee();
        DiagramCanvasSurface.LostMouseCapture += (_, _) => { if (_selectionBox is not null) EndMarquee(); };
    }
    private void BeginMarquee(MouseButtonEventArgs e)
    {
        DiagramCanvasSurface.Focus();
        _selectionBase = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? new(_selectedCards) : [];
        ClearAttributeSelection(); ClearCardSelection(); ClearRelationshipSelection(); EntityPropertiesPanel.Visibility = Visibility.Collapsed;
        foreach (var card in _selectionBase) HighlightCard(card);
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
