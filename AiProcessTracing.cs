using System.Windows;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private void AiDetailChanged(object sender, RoutedEventArgs e)
    {
        if (AiProcessSection is not null)
            AiProcessSection.Visibility = AiShowDetails.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TraceAi(string message)
    {
        // Application milestones only; never log credentials or raw model responses.
        AiProcessLog.AppendText($"[{_aiElapsed.Elapsed.TotalSeconds:0.0}s] {message}\n");
        AiProcessLog.ScrollToEnd();
    }
}
