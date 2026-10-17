using System.Windows.Controls;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private bool IsAiModelObject(string key) => _entityCardKeys.Contains(key) || _viewCardKeys.Contains(key) || _materializedViewKeys.Contains(key);

    private HashSet<string> SelectedAiObjectKeys() => _selectedCards
        .Where(card => DiagramCanvasSurface.Children.Contains(card) && card.Tag is string key && _columns.ContainsKey(key) && IsAiModelObject(key))
        .Select(card => (string)card.Tag).ToHashSet();

    private HashSet<string> AiObjectScope()
    {
        var selected = SelectedAiObjectKeys();
        return selected.Count > 0 ? selected : _columns.Keys.Where(k => IsAiModelObject(k) && FindCard(k) is { } card && DiagramCanvasSurface.Children.Contains(card)).ToHashSet();
    }

    private void UpdateAiSelectionCounts()
    {
        if (AiCommentsButton is null || AiTuningButton is null) return;
        var count = SelectedAiObjectKeys().Count;
        var suffix = count > 0 ? $" ({count})" : "";
        AiCommentsCount.Text = suffix;
        AiCommentsButton.ToolTip = "Generate Comments" + suffix;
        AiTuningCount.Text = suffix;
        AiTuningButton.ToolTip = "AI Tuning" + suffix;
    }
}
