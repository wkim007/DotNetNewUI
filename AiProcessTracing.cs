using System.Windows;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private bool _aiProcessHasRun;

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
        AiProcessLog.AppendText($"[{_aiElapsed.Elapsed.TotalSeconds:0.0}s] {message}\n");
        AiProcessLog.ScrollToEnd();
    }
}
