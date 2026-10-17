using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private sealed class AiProcessRun(string action)
    {
        public string Action { get; } = action;
        public StringBuilder Log { get; } = new();
        public string Status { get; set; } = "Processing…";
        public Border Card { get; set; } = null!;
        public TextBlock StatusLabel { get; set; } = null!;
        public TextBlock UpdateLabel { get; set; } = null!;
        public TextBox Details { get; set; } = null!;
        public Expander Expander { get; set; } = null!;
    }
    private readonly List<AiProcessRun> _aiProcessHistory = new();
    private int _aiProcessHistoryIndex = -1;

    private void BeginAiProcessRun(string action)
    {
        var run = new AiProcessRun(action);
        AiConversationWelcome.Visibility = Visibility.Collapsed;
        var prompt = action == "Generate" ? AiSchemaDescription.Text.Trim() : action;
        if (action == "Generate" && _aiImageData is not null)
            prompt += "\nAttached image: " + AiAttachmentName.Text;
        var userText = new TextBox
        {
            Text = prompt, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent, Foreground = ThemeInk, BorderThickness = new Thickness(0),
            MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var userPanel = new StackPanel();
        userPanel.Children.Add(new TextBlock { Text = "You", Foreground = ThemeMuted, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        userPanel.Children.Add(userText);
        AiConversation.Children.Add(new Border
        {
            Child = userPanel, Background = ThemeBrush("#30435F"), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12), Margin = new Thickness(30, 8, 0, 10)
        });
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "AI · " + action, Foreground = ThemeBrush("#62E6B3"), FontWeight = FontWeights.SemiBold });
        run.StatusLabel = new TextBlock { Text = run.Status, Foreground = ThemeMuted, Margin = new Thickness(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
        run.UpdateLabel = new TextBlock { Foreground = ThemeInk, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        run.Details = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent,
            Foreground = ThemeMuted, BorderThickness = new Thickness(0), Padding = new Thickness(0, 8, 0, 0)
        };
        run.Expander = new Expander { Header = "Process details", Foreground = ThemeMuted, Content = run.Details, IsExpanded = AiShowDetails.IsChecked == true };
        void SyncToggle(object sender, RoutedEventArgs e)
        {
            if (_aiProcessHistoryIndex >= 0 && _aiProcessHistory[_aiProcessHistoryIndex] == run)
                AiProcessToggle.Content = run.Expander.IsExpanded ? "−" : "+";
        }
        run.Expander.Expanded += SyncToggle;
        run.Expander.Collapsed += SyncToggle;
        panel.Children.Add(run.StatusLabel);
        panel.Children.Add(run.UpdateLabel);
        panel.Children.Add(run.Expander);
        run.Card = new Border { Child = panel, Background = ThemeBrush("#142230"), BorderBrush = ThemeBrush("#293D50"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 12, 16) };
        AiConversation.Children.Add(run.Card);
        _aiProcessHistory.Add(run);
        UpdateAiControlState();
        _aiProcessHistoryIndex = _aiProcessHistory.Count - 1;
        ShowAiProcessRun();
        AiConversationScroll.ScrollToEnd();
    }

    private void ShowAiProcessRun()
    {
        if (_aiProcessHistoryIndex < 0) return;
        var run = _aiProcessHistory[_aiProcessHistoryIndex];
        AiProcessRunLabel.Text = $"{_aiProcessHistoryIndex + 1} / {_aiProcessHistory.Count} — {run.Action}";
        AiProcessTime.Text = run.Status;
        foreach (var item in _aiProcessHistory)
            item.Card.BorderBrush = ThemeBrush(item == run ? "#62E6B3" : "#293D50");
        AiProcessToggle.Content = run.Expander.IsExpanded ? "−" : "+";
        AiProcessToggle.ToolTip = "Expand or collapse selected process details";
        run.Card.BringIntoView();
    }

    private void SetAiProcessStatus(string status)
    {
        if (_aiProcessHistory.Count == 0) return;
        var run = _aiProcessHistory[^1];
        run.Status = status;
        run.StatusLabel.Text = status;
        if (_aiProcessHistoryIndex == _aiProcessHistory.Count - 1)
            AiProcessTime.Text = status;
    }

    private void AiProcessToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_aiProcessHistoryIndex < 0) return;
        var run = _aiProcessHistory[_aiProcessHistoryIndex];
        run.Expander.IsExpanded = !run.Expander.IsExpanded;
        ShowAiProcessRun();
    }

    private void AiDetailChanged(object sender, RoutedEventArgs e)
    {
        if (_aiProcessHistoryIndex < 0) return;
        _aiProcessHistory[_aiProcessHistoryIndex].Expander.IsExpanded = AiShowDetails.IsChecked == true;
        ShowAiProcessRun();
    }

    private void TraceAi(string message)
    {
        // Application milestones only; never log credentials or raw model responses.
        if (_aiProcessHistory.Count == 0) return;
        var follow = _aiProcessHistoryIndex == _aiProcessHistory.Count - 1
            && AiConversationScroll.VerticalOffset >= AiConversationScroll.ScrollableHeight - 24;
        var run = _aiProcessHistory[^1];
        var line = $"[{_aiElapsed.Elapsed.TotalSeconds:0.0}s] {message}\n";
        run.Log.Append(line);
        run.Details.AppendText(line);
        run.UpdateLabel.Text = message;
        if (follow) AiConversationScroll.ScrollToEnd();
    }
}
