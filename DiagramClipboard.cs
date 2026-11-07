using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private sealed record ClipboardItem(string? SourceKey, Point Position, Func<Dictionary<Guid, Guid>, FrameworkElement> Create);
    private List<ClipboardItem> _clipboardItems = [];
    private sealed record ClipboardRelationship(string Source, string Target, string Type, string Cardinality,
        string Name, string Comment, Vector Offset, Point? SourceAnchor, Point? TargetAnchor, ColumnMapping[]? Mappings, Guid[]? Inherited, bool Identified);
    private ClipboardRelationship[] _clipboardRelationships = [];
    private Point _clipboardOrigin;
    private Point _nextPastePosition;

    private void InitializeDiagramClipboard()
    {
        FrameworkElement? SelectedObject()
        {
            var item = (FrameworkElement?)_selectedCard ?? _selectedDrawing;
            return item != null && DiagramCanvasSurface.Children.Contains(item) ? item : null;
        }

        var copyCommand = new RoutedCommand();
        var pasteCommand = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(copyCommand, (_, _) =>
        {
            if (SelectedObject() is { } item) CopyDiagramObject(item);
        }, (_, e) => e.CanExecute = SelectedObject() is Border or Grid { Tag: "DrawingShape" }));
        CommandBindings.Add(new CommandBinding(pasteCommand, (_, _) => PasteDiagramObject(_nextPastePosition),
            (_, e) => e.CanExecute = _clipboardItems.Count > 0));
        DiagramCopyButton.Command = copyCommand;
        DiagramPasteButton.Command = pasteCommand;
        DiagramCopyButton.CommandTarget = DiagramPasteButton.CommandTarget = this;

        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || e.Key is not (Key.C or Key.V) || _inlineRenameActive) return;
            for (var n = Keyboard.FocusedElement as DependencyObject; n != null; n = n is Visual ? VisualTreeHelper.GetParent(n) : LogicalTreeHelper.GetParent(n))
                if (n is TextBoxBase or PasswordBox or ComboBox or DataGrid) return;
            if (e.Key == Key.V && _clipboardItems.Count > 0) { PasteDiagramObject(_nextPastePosition); e.Handled = true; }
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
        var paste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V", IsEnabled = _clipboardItems.Count > 0 };
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
        FrameworkElement[] items = item is Border selected && _selectedCards.Contains(selected)
            ? DiagramCanvasSurface.Children.OfType<Border>().Where(_selectedCards.Contains).ToArray()
            : [item];
        var snapshots = new List<ClipboardItem>();
        foreach (var selectedItem in items)
        {
            var create = SnapshotDiagramObject(selectedItem);
            if (create == null) return false;
            snapshots.Add(new((selectedItem as Border)?.Tag as string, new(Canvas.GetLeft(selectedItem), Canvas.GetTop(selectedItem)), create));
        }
        if (snapshots.Count == 0) return false;
        _clipboardRelationships = SnapshotRelationships(snapshots.Select(s => s.SourceKey).OfType<string>().ToHashSet()).ToArray();
        _clipboardItems = snapshots;
        _clipboardOrigin = new(snapshots.Min(s => s.Position.X), snapshots.Min(s => s.Position.Y));
        _nextPastePosition = _clipboardOrigin + new Vector(28, 28);
        CommandManager.InvalidateRequerySuggested();
        StatusText.Text = $"Copied {snapshots.Count} diagram object(s)";
        return true;
    }

    private Func<Dictionary<Guid, Guid>, FrameworkElement>? SnapshotDiagramObject(FrameworkElement item)
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
            return ids =>
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
            return ids =>
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
                    foreach (var column in columns) ids[column.Id] = Guid.NewGuid();
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
        return null;
    }

    private IEnumerable<ClipboardRelationship> SnapshotRelationships(HashSet<string> keys)
    {
        foreach (var hit in DiagramCanvasSurface.Children.OfType<Path>())
        {
            if (hit.Tag is not string key || _deletedRelationships.Contains(key) ||
                hit.Resources[typeof(RelationshipText)] is not RelationshipText text ||
                GetRelationshipInfo(key) is not { } info || !keys.Contains(info.Source) || !keys.Contains(info.Target)) continue;
            var relation = _dynamicRelationships.GetValueOrDefault(key);
            var type = info.Type switch
            {
                "Identifying" => "Identifying relationship",
                "Non-identifying" => "Non-identifying relationship",
                "View / Materialized view" => "View relationship",
                _ => info.Type
            };
            var line = FindRelationshipLine(key);
            var endpoints = line != null && _endpointPositions.TryGetValue(line, out var positions) ? positions : null;
            yield return new(info.Source, info.Target, type, _relationshipLabels.GetValueOrDefault(key)?.Text ?? "1:N",
                text.Name, text.Comment, _relationshipOffsets.GetValueOrDefault(key), endpoints?.Source, endpoints?.Target,
                (relation?.ColumnMappings ?? _sampleColumnMappings.GetValueOrDefault(key))?.ToArray(),
                relation?.InheritedAttributeIds?.ToArray(), relation?.IsSupertypeSubtypeIdentified == true);
        }
    }

    private void PasteDiagramObject(Point position)
    {
        if (_clipboardItems.Count == 0) return;
        var pastedCards = new List<Border>();
        var keys = new Dictionary<string, string>();
        var columns = new Dictionary<string, Dictionary<Guid, Guid>>();
        foreach (var snapshot in _clipboardItems)
        {
            var ids = new Dictionary<Guid, Guid>();
            var copy = snapshot.Create(ids);
            var offset = snapshot.Position - _clipboardOrigin;
            Canvas.SetLeft(copy, Math.Max(0, position.X) + offset.X);
            Canvas.SetTop(copy, Math.Max(0, position.Y) + offset.Y);
            if (copy is Border card && snapshot.SourceKey is { } original)
            {
                pastedCards.Add(card);
                keys.Add(original, (string)card.Tag);
                columns.Add(original, ids);
            }
        }
        foreach (var link in _clipboardRelationships)
        {
            var parentIds = columns[link.Source];
            var childIds = columns[link.Target];
            var mappings = link.Mappings?.Where(m => parentIds.ContainsKey(m.Parent) && childIds.ContainsKey(m.Child))
                .Select(m => new ColumnMapping(parentIds[m.Parent], childIds[m.Child])).ToArray();
            var key = CreateDynamicRelationship(keys[link.Source], keys[link.Target], link.Type, mappings);
            if (key == null) continue;
            var relation = _dynamicRelationships[key];
            _dynamicRelationships[key] = relation with {
                InheritedAttributeIds = link.Inherited?.Where(childIds.ContainsKey).Select(id => childIds[id]).ToArray(),
                IsSupertypeSubtypeIdentified = link.Identified
            };
            var text = (RelationshipText)relation.Hit.Resources[typeof(RelationshipText)];
            text.Name = link.Name;
            text.Comment = link.Comment;
            _relationshipLabels[key].Text = link.Cardinality;
            _relationshipOffsets[key] = link.Offset;
            var endpoints = _endpointPositions.GetOrCreateValue(relation.Visible);
            endpoints.Source = link.SourceAnchor;
            endpoints.Target = link.TargetAnchor;
        }
        if (_clipboardRelationships.Length > 0)
        {
            ClearRelationshipSelection();
            UpdateRelationshipLines();
            if (pastedCards.Count == 1)
            {
                SelectEntity((string)pastedCards[0].Tag);
                SelectCard(pastedCards[0]);
            }
        }
        if (pastedCards.Count > 1)
        {
            ClearAttributeSelection();
            EntityPropertiesPanel.Visibility = Visibility.Collapsed;
            foreach (var card in pastedCards) HighlightCard(card);
        }
        _nextPastePosition = new(position.X + 28, position.Y + 28);
        SaveActiveDiagram();
        RefreshModelExplorer();
        StatusText.Text = $"Pasted {_clipboardItems.Count} diagram object(s)";
    }
}
