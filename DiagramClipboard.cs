using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private Func<FrameworkElement>? _createClipboardCopy;
    private Point _nextPastePosition;

    private void InitializeDiagramClipboard()
    {
        FrameworkElement? SelectedObject()
        {
            var item = _selectedDrawing ?? _selectedCard;
            return item != null && DiagramCanvasSurface.Children.Contains(item) ? item : null;
        }

        var copyCommand = new RoutedCommand();
        var pasteCommand = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(copyCommand, (_, _) =>
        {
            if (SelectedObject() is { } item) CopyDiagramObject(item);
        }, (_, e) => e.CanExecute = SelectedObject() is Border or Grid { Tag: "DrawingShape" }));
        CommandBindings.Add(new CommandBinding(pasteCommand, (_, _) => PasteDiagramObject(_nextPastePosition),
            (_, e) => e.CanExecute = _createClipboardCopy != null));
        DiagramCopyButton.Command = copyCommand;
        DiagramPasteButton.Command = pasteCommand;
        DiagramCopyButton.CommandTarget = DiagramPasteButton.CommandTarget = this;

        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || e.Key is not (Key.C or Key.V) || _inlineRenameActive) return;
            for (var n = Keyboard.FocusedElement as DependencyObject; n != null; n = n is Visual ? VisualTreeHelper.GetParent(n) : LogicalTreeHelper.GetParent(n))
                if (n is TextBoxBase or PasswordBox or ComboBox or DataGrid) return;
            if (e.Key == Key.V && _createClipboardCopy != null) { PasteDiagramObject(_nextPastePosition); e.Handled = true; }
            else if (e.Key == Key.C && SelectedObject() is { } item)
                e.Handled = CopyDiagramObject(item);
        };

        DiagramCanvasSurface.ContextMenuOpening += (_, e) =>
        {
            var node = e.OriginalSource as DependencyObject;
            while (node is Visual && node != DiagramCanvasSurface)
            {
                if (node is FrameworkElement { ContextMenu: not null } existing && existing.Tag is not "DrawingShape") return;
                if (node is FrameworkElement item && DiagramCanvasSurface.Children.Contains(item))
                {
                    if (item is not Border { Tag: string } && item.Tag is not "DrawingShape") return;
                    OpenClipboardMenu(item, Mouse.GetPosition(DiagramCanvasSurface));
                    e.Handled = true;
                    return;
                }
                node = VisualTreeHelper.GetParent(node);
            }
            if (node != DiagramCanvasSurface) return;
            OpenClipboardMenu(null, Mouse.GetPosition(DiagramCanvasSurface));
            e.Handled = true;
        };
    }

    private void OpenClipboardMenu(FrameworkElement? item, Point point)
    {
        var menu = new ContextMenu { PlacementTarget = item ?? (FrameworkElement)DiagramCanvasSurface, Placement = PlacementMode.MousePoint };
        if (item != null)
        {
            var copy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
            copy.Click += (_, _) => CopyDiagramObject(item);
            menu.Items.Add(copy);
        }
        var paste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V", IsEnabled = _createClipboardCopy != null };
        paste.Click += (_, _) => PasteDiagramObject(point);
        menu.Items.Add(paste);
        if (item?.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(i => Equals(i.Header, "Delete")) is { } original)
        {
            var delete = new MenuItem { Header = "Delete" };
            delete.Click += (_, _) => original.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Items.Add(delete);
        }
        menu.IsOpen = true;
    }

    private bool CopyDiagramObject(FrameworkElement item)
    {
        if (item is Grid { Tag: "DrawingShape" } shape)
        {
            var outline = shape.Children.OfType<Path>().First();
            var editor = shape.Children.OfType<TextBox>().First();
            var name = (string)outline.ToolTip;
            var geometry = outline.Data.Clone();
            var width = shape.Width;
            var height = shape.Height;
            var label = editor.Text;
            var margin = editor.Margin;
            _createClipboardCopy = () =>
            {
                var copy = AddDrawingShape(name);
                var path = copy.Children.OfType<Path>().First();
                path.Data = geometry.Clone();
                copy.Width = path.Width = width;
                copy.Height = path.Height = height;
                var text = copy.Children.OfType<TextBox>().First();
                text.Text = label;
                text.Margin = margin;
                Keyboard.ClearFocus();
                SelectDrawing(copy);
                return copy;
            };
        }
        else if (item is Border card && card.Tag is string key && _columns.TryGetValue(key, out var fields))
        {
            var name = EntityDisplayName(key);
            var description = _objectDefinitions.GetValueOrDefault(key, "");
            var physical = _objectPhysicalNames.GetValueOrDefault(key, key.ToLowerInvariant());
            var columns = fields.ToArray();
            var view = _viewCardKeys.Contains(key);
            var materialized = _materializedViewKeys.Contains(key);
            var annotation = (card.Child as Grid)?.Children.OfType<TextBox>().FirstOrDefault()?.Text;
            var width = card.Width;
            var height = card.Height;
            _createClipboardCopy = () =>
            {
                if (annotation != null) AddNewAnnotation();
                else if (view) AddNewView(materialized);
                else AddNewEntity();
                var copy = _selectedCard!;
                var target = (string)copy.Tag;
                copy.Width = width;
                copy.Height = height;
                if (annotation != null) ((Grid)copy.Child).Children.OfType<TextBox>().First().Text = annotation;
                else
                {
                    var ids = columns.ToDictionary(c => c.Id, _ => Guid.NewGuid());
                    _columns[target] = new(columns.Select(c => c with { Id = ids[c.Id], ParentId = c.ParentId is Guid p && ids.TryGetValue(p, out var parent) ? parent : null }));
                    var suffix = " Copy";
                    for (var i = 2; _columns.Keys.Any(k => EntityDisplayName(k).Equals(name + suffix, StringComparison.OrdinalIgnoreCase)); i++)
                        suffix = $" Copy {i}";
                    _entityDisplayNames[target] = name + suffix;
                    _objectDefinitions[target] = description;
                    _objectPhysicalNames[target] = physical + suffix.Replace(' ', '_');
                    var header = ((Grid)copy.Child).Children.OfType<Border>().First(b => Grid.GetRow(b) == 0);
                    Descendants<TextBlock>(header).First().Text = name + suffix;
                    RebuildNestedRows(target);
                    SelectEntity(target);
                }
                return copy;
            };
        }
        else return false;
        _nextPastePosition = new(Canvas.GetLeft(item) + 28, Canvas.GetTop(item) + 28);
        CommandManager.InvalidateRequerySuggested();
        StatusText.Text = "Copied diagram object";
        return true;
    }

    private void PasteDiagramObject(Point position)
    {
        if (_createClipboardCopy == null) return;
        var copy = _createClipboardCopy();
        Canvas.SetLeft(copy, Math.Max(0, position.X));
        Canvas.SetTop(copy, Math.Max(0, position.Y));
        _nextPastePosition = new(position.X + 28, position.Y + 28);
        SaveActiveDiagram();
        RefreshModelExplorer();
        StatusText.Text = "Pasted diagram object";
    }
}
