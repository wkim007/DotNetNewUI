using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private static string AiSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErwinStudioSample", "ai-settings.dat");

    private void SaveAiSettings(AiConfiguration configuration)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configuration));
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(AiSettingsPath)!);
            var temporary = AiSettingsPath + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, AiSettingsPath, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private void LoadAiSettings()
    {
        if (!File.Exists(AiSettingsPath)) return;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(AiSettingsPath), null, DataProtectionScope.CurrentUser);
            try
            {
                var configuration = JsonSerializer.Deserialize<AiConfiguration>(bytes);
                if (configuration is null || string.IsNullOrWhiteSpace(configuration.Key) || string.IsNullOrWhiteSpace(configuration.Endpoint) ||
                    string.IsNullOrWhiteSpace(configuration.Version) || string.IsNullOrWhiteSpace(configuration.Deployment))
                    throw new InvalidDataException();
                _aiConfiguration = configuration;
                SetAiValidation(true);
                AiStatus.Text = "Loaded previously validated AI settings.";
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            SetAiValidation(false);
            AiStatus.Text = "Could not load saved AI settings. Open Settings and validate again.";
        }
    }

    private readonly DispatcherTimer _aiProgressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Diagnostics.Stopwatch _aiElapsed = new();

    private void InitializeAiFeatures()
    {
        LoadAiSettings();
        _aiProgressTimer.Tick += (_, _) => AiProgressText.Text = $"AI is processing… ({(int)_aiElapsed.Elapsed.TotalSeconds}s)";
        Closed += (_, _) => _aiProgressTimer.Stop();
    }

    private void SetAiProgress(bool running, string action = "")
    {
        _aiBusy = running;
        AiActions.IsEnabled = _aiValidated && !running;
        AiSchemaDescription.IsEnabled = !running;
        AiProgressPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        AiProgressBar.IsIndeterminate = running;
        AiGenerateButton.Content = running && action == "Generate" ? "Generating…" : "✧  Generate";
        if (running)
        {
            _aiElapsed.Restart(); AiProgressText.Text = "AI is processing… (0s)"; _aiProgressTimer.Start();
        }
        else { _aiProgressTimer.Stop(); _aiElapsed.Stop(); }
    }
}
