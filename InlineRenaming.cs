using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private bool _inlineRenameActive;

    private void ConfigureHoldRename(FrameworkElement target, Action begin, Border? card = null)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        Point start = default;
        var eventSurface = (FrameworkElement?)card ?? target;
        target.PreviewMouseLeftButtonDown += (_, args) =>
        {
            timer.Stop();
            if (_inlineRenameActive || _pendingRelationshipType is not null || _connectorSource is not null) return;
            if (card is not null)
            {
                for (var source = args.OriginalSource as DependencyObject; source is not null && source != target; source = VisualTreeHelper.GetParent(source))
                    if (source is Button || source is TextBox) return;
                Entity_MouseLeftButtonDown(card, args);
                _entityHoldTimer.Stop(); _entityHoldCard = null;
            }
            start = args.GetPosition(DiagramCanvasSurface); timer.Start();
        };
        eventSurface.PreviewMouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed || (args.GetPosition(DiagramCanvasSurface) - start).Length > 5) timer.Stop();
        };
        eventSurface.PreviewMouseLeftButtonUp += (_, _) => timer.Stop();
        target.MouseLeave += (_, _) => { if (card is null) timer.Stop(); };
        eventSurface.LostMouseCapture += (_, _) => timer.Stop();
        target.Unloaded += (_, _) => timer.Stop();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var pointer = Mouse.GetPosition(target);
            var inside = new Rect(0, 0, target.ActualWidth, target.ActualHeight).Contains(pointer);
            // Card capture redirects IsMouseOver away from the header during a hold.
            if (_inlineRenameActive || !target.IsLoaded || !inside || Mouse.LeftButton != MouseButtonState.Pressed) return;
            _entityHoldTimer.Stop(); _entityHoldCard = null; _draggedCard = null; _attributeDragRow = null;
            Mouse.Capture(null); begin();
        };
    }

    private void BeginTitleRename(Border card, string key, TextBlock title)
    {
        if (title.Parent is not Grid grid) return;
        SelectCard(card); SelectEntity(key);
        ShowInlineNameEditor(grid, title, EntityDisplayName(key), name =>
        {
            _entityDisplayNames[key] = name;
            title.Text = name;
            SelectEntity(key);
            RefreshModelExplorer();
            return true;
        });
    }

    private void BeginColumnRename(Border row)
    {
        if (row.Tag is not AttributeSelection selection || _viewCardKeys.Contains(selection.EntityKey) || row.Child is not Grid grid || !_columns.TryGetValue(selection.EntityKey, out var columns)) return;
        var index = columns.ToList().FindIndex(c => c.Name == selection.AttributeName);
        if (index < 0) return;
        var original = columns[index];
        var label = grid.Children.OfType<FrameworkElement>().FirstOrDefault(child => Grid.GetColumn(child) == 1);
        if (label is null) return;
        ShowInlineNameEditor(grid, label, original.Name, name =>
        {
            var currentIndex = columns.IndexOf(original);
            if (currentIndex < 0) return false;
            if (columns.Where((_, i) => i != currentIndex).Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { StatusText.Text = "A column with that name already exists"; return false; }
            columns[currentIndex] = original with { Name = name };
            row.Tag = new AttributeSelection(selection.EntityKey, name);
            if (label is TextBlock text) { text.Text = name; text.ToolTip = name; }
            else if (label is TextBox textbox) textbox.Text = name;
            ColumnsGrid.Items.Refresh();
            StatusText.Text = $"Renamed column to {name}";
            return true;
        });
    }

    private void ShowInlineNameEditor(Grid host, FrameworkElement label, string name, Func<string, bool> save)
    {
        if (_inlineRenameActive) return;
        _inlineRenameActive = true;
        var editor = new TextBox
        {
            Text = name, Background = ThemeBrush("#0E1824"), Foreground = Brushes.White, CaretBrush = Brushes.White,
            BorderBrush = ThemeBrush("#62E6B3"), Padding = new Thickness(3, 1, 3, 1),
            VerticalContentAlignment = VerticalAlignment.Center, MinWidth = 35
        };
        Grid.SetColumn(editor, Grid.GetColumn(label)); Grid.SetRow(editor, Grid.GetRow(label));
        label.Visibility = Visibility.Hidden; host.Children.Add(editor);
        var done = false;
        void Finish(bool commit)
        {
            if (done) return;
            var value = editor.Text.Trim();
            if (commit && value.Length > 0 && !save(value)) return;
            done = true; _inlineRenameActive = false;
            host.Children.Remove(editor); label.Visibility = Visibility.Visible;
        }
        editor.PreviewMouseLeftButtonDown += (_, args) => args.Handled = false;
        editor.MouseLeftButtonDown += (_, args) => args.Handled = true;
        editor.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is Key.Enter or Key.Escape) { Finish(args.Key == Key.Enter); args.Handled = true; }
        };
        editor.LostKeyboardFocus += (_, _) => { Finish(true); if (!done) Finish(false); };
        host.Unloaded += OnUnload;
        void OnUnload(object sender, RoutedEventArgs args) { Finish(false); host.Unloaded -= OnUnload; }
        editor.Focus(); Keyboard.Focus(editor); editor.SelectAll();
    }
}
