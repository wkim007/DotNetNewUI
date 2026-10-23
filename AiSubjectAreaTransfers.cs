using System.Text.Json;
using System.Text.Json.Nodes;
using static ErwinStudioSample.ModelJsonDocument;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private bool TryApplySubjectAreaTransfer(string description)
    {
        if (!ModelSubjectAreaTransfer.IsTransferRequest(description)) return false;
        try
        {
            TraceAi("Resolving the existing object and destination subject area.");
            var command = ModelSubjectAreaTransfer.Parse(description);
            var destination = _subjectAreas.FirstOrDefault(a => a.Name.Equals(command.DestinationArea, StringComparison.OrdinalIgnoreCase));
            var preferred = destination?.LastDiagram is { } last && _diagrams.Contains(last) ? last.Id : null;
            var original = ExportModelJson();
            var inputSnapshot = CaptureAiModelSnapshot();
            var transfer = ModelSubjectAreaTransfer.Apply(original, command, preferred);
            TraceAi($"Validated {command.Action}: {transfer.ObjectName} (ID {transfer.ObjectId}) → {transfer.DestinationArea} / {transfer.DestinationDiagram}.");
            // Reuse the validated, transactional importer so placements and relationships
            // are reconstructed from the existing model rather than generated schema.
            var validated = ModelJsonDocument.Parse(transfer.Model.ToJsonString());
            TraceAi("Preserving object identity, attributes, descriptions, indexes, and existing relationship definitions.");
            ImportModelJson(validated);
            var action = command.Action == "move" ? "Moved" : "Copied";
            var message = transfer.Changed
                ? $"{action} '{transfer.ObjectName}' to Subject Area '{transfer.DestinationArea}', diagram '{transfer.DestinationDiagram}'."
                : $"'{transfer.ObjectName}' is already in Subject Area '{transfer.DestinationArea}', diagram '{transfer.DestinationDiagram}'.";
            _latestAiRequestJson = JsonSerializer.Serialize(new
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                Engine = "Local model operation",
                Note = "This command applies the user's explicit copy/move request to existing workspace objects. No schema-generation API request was needed.",
                Request = new { description, command.Action, ObjectId = transfer.ObjectId, transfer.ObjectName, transfer.SourceArea, transfer.SourceDiagram, transfer.DestinationArea, transfer.DestinationDiagram },
                Response = new { transfer.Changed, Message = message },
                Status = "Applied to model",
                InputModelSnapshot = inputSnapshot,
                AppliedModelSnapshot = CaptureAiModelSnapshot()
            }, new JsonSerializerOptions { WriteIndented = true });
            UpdateAiControlState();
            AiSchemaDescription.Clear();
            ClearAiAttachment();
            AiStatus.Text = StatusText.Text = message;
            TraceAi(message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or ArgumentException)
        {
            AiStatus.Text = "Could not copy or move the object: " + ex.Message;
            TraceAi(AiStatus.Text);
        }
        return true;
    }
}
