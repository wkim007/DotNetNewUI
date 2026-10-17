using System.Text;
using System.Windows;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private bool _aiProcessHasRun;
    private sealed class AiProcessRun(string action)
    {
        public string Action { get; } = action;
        public StringBuilder Log { get; } = new();
        public string Status { get; set; } = "Processing…";
    }
    private readonly List<AiProcessRun> _aiProcessHistory = new();
    private int _aiProcessHistoryIndex = -1;

    private void BeginAiProcessRun(string action)
    {
        _aiProcessHistory.Add(new AiProcessRun(action));
        _aiProcessHistoryIndex = _aiProcessHistory.Count - 1;
        ShowAiProcessRun();
    }

    private void ShowAiProcessRun()
    {
        AiProcessPrevious.IsEnabled = _aiProcessHistoryIndex > 0;
        AiProcessNext.IsEnabled = _aiProcessHistoryIndex >= 0 && _aiProcessHistoryIndex < _aiProcessHistory.Count - 1;
        if (_aiProcessHistoryIndex < 0) return;
        var run = _aiProcessHistory[_aiProcessHistoryIndex];
        AiProcessRunLabel.Text = $"{_aiProcessHistoryIndex + 1} / {_aiProcessHistory.Count} — {run.Action}";
        AiProcessTime.Text = run.Status;
        AiProcessLog.Text = run.Log.ToString();
        AiProcessLog.ScrollToEnd();
    }

    private void SetAiProcessStatus(string status)
    {
        if (_aiProcessHistory.Count == 0) return;
        _aiProcessHistory[^1].Status = status;
        if (_aiProcessHistoryIndex == _aiProcessHistory.Count - 1)
            AiProcessTime.Text = status;
    }

    private void AiProcessPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_aiProcessHistoryIndex <= 0) return;
        _aiProcessHistoryIndex--;
        ShowAiProcessRun();
    }

    private void AiProcessNext_Click(object sender, RoutedEventArgs e)
    {
        if (_aiProcessHistoryIndex >= _aiProcessHistory.Count - 1) return;
        _aiProcessHistoryIndex++;
        ShowAiProcessRun();
    }

    private void AiProcessToggle_Click(object sender, RoutedEventArgs e)
    {
        var expanded = AiProcessDetails.Visibility != Visibility.Visible;
        AiProcessDetails.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        AiProcessToggle.Content = expanded ? "−" : "+";
        AiProcessToggle.ToolTip = expanded ? "Collapse process details" : "Expand process details";
    }

    private void AiDetailChanged(object sender, RoutedEventArgs e)
    {
        if (AiProcessSection is not null)
            AiProcessSection.Visibility = _aiProcessHasRun || _aiBusy || AiShowDetails.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TraceAi(string message)
    {
        // Application milestones only; never log credentials or raw model responses.
        if (_aiProcessHistory.Count == 0) return;
        var line = $"[{_aiElapsed.Elapsed.TotalSeconds:0.0}s] {message}\n";
        _aiProcessHistory[^1].Log.Append(line);
        if (_aiProcessHistoryIndex != _aiProcessHistory.Count - 1) return;
        AiProcessLog.AppendText(line);
        AiProcessLog.ScrollToEnd();
    }
}
