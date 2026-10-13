using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ErwinStudioSample;

public partial class MainWindow
{
    private string? _aiImageData;
    private void AiAttach_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy) return;
        var picker = new OpenFileDialog { Title = "Open schema description or diagram", Filter = "Supported files|*.txt;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.xlsx;*.xls;*.xlsb|Excel workbook|*.xlsx;*.xls;*.xlsb|Text description|*.txt|Diagram image|*.png;*.jpg;*.jpeg;*.gif;*.bmp", Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var file = new FileInfo(picker.FileName);
            if (new[] { ".xlsx", ".xls", ".xlsb" }.Contains(file.Extension.ToLowerInvariant()))
            {
                var workbook = ExcelSchemaImport.Read(file.FullName);
                ClearAiAttachment();
                AiSchemaDescription.Text = workbook.Text;
                AiStatus.Text = $"Loaded {workbook.Sheets} worksheets from {file.Name}. Review the extracted text, then Generate.";
                return;
            }
            if (file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                if (file.Length > 200_000) throw new InvalidOperationException("Choose a text file smaller than 200 KB.");
                var text = File.ReadAllText(file.FullName);
                AiSchemaDescription.Text += (string.IsNullOrWhiteSpace(AiSchemaDescription.Text) ? "" : "\n\n") + text;
                AiStatus.Text = $"Loaded text from {file.Name}. You can edit it before generating.";
                return;
            }
            if (file.Length > 20_000_000) throw new InvalidOperationException("Choose an image smaller than 20 MB.");
            using var stream = File.OpenRead(file.FullName);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(frame);
            using var output = new MemoryStream(); encoder.Save(output);
            if (output.Length > 20_000_000) throw new InvalidOperationException("The decoded image is too large. Resize it and try again.");
            _aiImageData = "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
            frame.Freeze(); AiAttachmentPreview.Source = frame;
            AiAttachmentName.Text = file.Name;
            AiAttachmentPanel.Visibility = Visibility.Visible;
            AiStatus.Text = "Image attached. Add optional instructions, then Generate. Requires an AI deployment that supports images.";
        }
        catch (Exception ex) when (ex is ExcelDataReader.Exceptions.ExcelReaderException or IOException or UnauthorizedAccessException or NotSupportedException or System.IO.FileFormatException or InvalidOperationException or ArgumentException)
        { AiStatus.Text = "Could not open attachment: " + ex.Message; }
    }

    private void AiRemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (_aiBusy) return;
        ClearAiAttachment();
    }

    private void ClearAiAttachment()
    {
        _aiImageData = null; AiAttachmentPreview.Source = null;
        AiAttachmentPanel.Visibility = Visibility.Collapsed;
        AiAttachmentName.Text = "";
    }
}
