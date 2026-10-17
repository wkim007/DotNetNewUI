using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private string? _latestAiRequestJson;

    private object CaptureAiModelSnapshot()
    {
        double Finite(double value) => double.IsFinite(value) ? value : 0;
        var objects = _columns.Where(p => FindCard(p.Key) is { } card && DiagramCanvasSurface.Children.Contains(card)).Select(p =>
        {
            var card = FindCard(p.Key)!;
            return new
            {
                Key = p.Key, Name = EntityDisplayName(p.Key),
                Kind = _materializedViewKeys.Contains(p.Key) ? "MaterializedView" : _viewCardKeys.Contains(p.Key) ? "View" : "Entity",
                Location = new { X = Finite(Canvas.GetLeft(card)), Y = Finite(Canvas.GetTop(card)) },
                Size = new { Width = Finite(card.ActualWidth), Height = Finite(card.ActualHeight) },
                Description = _objectDefinitions.GetValueOrDefault(p.Key, ""),
                Selected = _selectedCards.Contains(card), Attributes = p.Value.ToArray()
            };
        }).ToArray();
        var relationships = new[] { "CustomerOrder", "OrderProduct", "OrderOrderItem", "ProductOrderItem" }.Concat(_dynamicRelationships.Keys)
            .Where(k => !_deletedRelationships.Contains(k) && FindRelationshipLine(k) is { } line && DiagramCanvasSurface.Children.Contains(line) && GetRelationshipInfo(k).HasValue)
            .Select(k => { var info = GetRelationshipInfo(k)!.Value; return new { Key = k, Source = info.Source, Target = info.Target, Type = info.Type, Cardinality = _relationshipLabels.GetValueOrDefault(k)?.Text, RouteOffset = new { X = _relationshipOffsets.GetValueOrDefault(k).X, Y = _relationshipOffsets.GetValueOrDefault(k).Y } }; }).ToArray();
        return new { Diagram = _activeDiagram?.Title, Database = ProjectDatabaseBox.Text, DatabaseVersion = ProjectVersionBox.Text, ViewMode = ViewModeBox.Text, LogicalNotation = _logicalNotation, PhysicalNotation = _physicalNotation, RelationshipLineStyle = _relationshipLineStyle, Objects = objects, Relationships = relationships };
    }

    private void CaptureAiRequest(AiConfiguration config, Dictionary<string, object> body, string prompt, string? imageData)
    {
        var reviewBody = new Dictionary<string, object>(body);
        if (imageData is not null)
            reviewBody["messages"] = new[] { new { role = "user", content = new object[] { new { type = "text", text = prompt }, new { type = "image_url", image_url = new { url = "[image data omitted from viewer]", detail = "high" } } } } };
        _latestAiRequestJson = JsonSerializer.Serialize(new
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Note = "Request contains the API body sent to AI. InputModelSnapshot is the model before the request. Response is the returned AI content. AppliedModelSnapshot is the resulting model after changes; neither snapshot is an additional API field. Credentials and image bytes are omitted.",
            Engine = "Azure OpenAI", config.Deployment, ApiVersion = config.Version,
            Request = reviewBody,
            Status = "Request sent",
            InputModelSnapshot = CaptureAiModelSnapshot()
        }, new JsonSerializerOptions { WriteIndented = true });
        AiRequestDocument.IsEnabled = true;
    }

    private void CaptureAiResponse(string response)
    {
        if (_latestAiRequestJson is null) return;
        var review = System.Text.Json.Nodes.JsonNode.Parse(_latestAiRequestJson)!;
        review["Status"] = "Response received; not yet applied";
        review["ResponseReceivedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
        var json = response.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal) && json.IndexOf('\n') is var first && first >= 0 && json.LastIndexOf("```", StringComparison.Ordinal) is var last && last > first)
            json = json[(first + 1)..last];
        try { review["Response"] = System.Text.Json.Nodes.JsonNode.Parse(json); }
        catch (JsonException) { review["Response"] = response; }
        _latestAiRequestJson = review.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private void CaptureAppliedAiModel()
    {
        if (_latestAiRequestJson is null) return;
        DiagramCanvasSurface.UpdateLayout();
        var review = System.Text.Json.Nodes.JsonNode.Parse(_latestAiRequestJson)!;
        review["Status"] = "Applied to model";
        review["AppliedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
        review["AppliedModelSnapshot"] = JsonSerializer.SerializeToNode(CaptureAiModelSnapshot());
        _latestAiRequestJson = review.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    private void AiRequestDocument_Click(object sender, RoutedEventArgs e)
    {
        if (_latestAiRequestJson is null) return;
        var dialog = new Window { Title = "Latest AI Request and Result — JSON", Owner = this, Width = 920, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = ThemeBrush("#17232F") };
        var root = new DockPanel { Margin = new Thickness(16) }; dialog.Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var find = new Button { Content = "Find", Style = (Style)FindResource("ProjectSettingsButton"), Margin = new Thickness(0, 0, 10, 0) }; footer.Children.Add(find);
        var copy = new Button { Content = "Copy JSON", Style = (Style)FindResource("ProjectSettingsButton"), Margin = new Thickness(0, 0, 10, 0) };
        var close = new Button { Content = "Close", IsCancel = true, Style = (Style)FindResource("ProjectSettingsButton") }; footer.Children.Add(copy); footer.Children.Add(close);
        var text = new TextBox { Text = _latestAiRequestJson, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, Background = ThemeBrush("#0E1824"), Foreground = ThemeInk, Padding = new Thickness(12), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        text.IsInactiveSelectionHighlightEnabled = true;
        var search = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
        var keyword = new TextBox { Width = 240, Padding = new Thickness(8), Background = ThemeBrush("#0E1824"), Foreground = ThemeInk, CaretBrush = ThemeInk, ToolTip = "Find keyword (case-insensitive)" };
        var previous = new Button { Content = "Previous", Style = (Style)FindResource("ProjectSettingsButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        var next = new Button { Content = "Next", Style = (Style)FindResource("ProjectSettingsButton"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        var status = new TextBlock { Foreground = ThemeInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        search.Children.Add(keyword); search.Children.Add(previous); search.Children.Add(next); search.Children.Add(status);
        var matches = new List<int>(); var current = -1;
        void Navigate(int direction)
        {
            if (matches.Count == 0) return;
            current = (current + direction + matches.Count) % matches.Count;
            var position = matches[current];
            text.Select(position, keyword.Text.Length);
            text.ScrollToLine(text.GetLineIndexFromCharacterIndex(position));
            var bounds = text.GetRectFromCharacterIndex(position);
            if (!bounds.IsEmpty) text.ScrollToHorizontalOffset(Math.Max(0, text.HorizontalOffset + bounds.X - 30));
            status.Text = $"{current + 1} of {matches.Count}";
        }
        keyword.TextChanged += (_, _) =>
        {
            matches.Clear(); current = -1;
            if (keyword.Text.Length > 0)
                for (var start = 0; start <= text.Text.Length - keyword.Text.Length;)
                {
                    var index = text.Text.IndexOf(keyword.Text, start, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) break;
                    matches.Add(index); start = index + keyword.Text.Length;
                }
            next.IsEnabled = previous.IsEnabled = matches.Count > 0;
            status.Text = keyword.Text.Length == 0 ? "Enter a keyword" : "No matches";
            if (matches.Count > 0) Navigate(1); else text.Select(text.CaretIndex, 0);
        };
        void OpenFind() { search.Visibility = Visibility.Visible; keyword.Focus(); keyword.SelectAll(); }
        find.Click += (_, _) => OpenFind();
        next.Click += (_, _) => Navigate(1); previous.Click += (_, _) => Navigate(-1);
        keyword.KeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Enter) { Navigate((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 ? -1 : 1); args.Handled = true; } };
        dialog.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.F && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0) { OpenFind(); args.Handled = true; }
            else if (args.Key == System.Windows.Input.Key.F3) { Navigate((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 ? -1 : 1); args.Handled = true; }
            else if (args.Key == System.Windows.Input.Key.Escape && search.Visibility == Visibility.Visible) { search.Visibility = Visibility.Collapsed; text.Focus(); args.Handled = true; }
        };
        root.Children.Add(text); copy.Click += (_, _) => Clipboard.SetText(text.Text); dialog.ShowDialog();
    }
}
