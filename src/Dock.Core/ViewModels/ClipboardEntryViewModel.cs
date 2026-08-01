using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Core.Models;
using Dock.Core.Services;

namespace Dock.Core.ViewModels;

public partial class ClipboardEntryViewModel : ObservableObject
{
    private readonly IClipboardWriter _writer;

    public ClipboardEntry Entry { get; }
    public string Text => Entry.Text;
    public string Preview => BuildPreview(Entry.Text);

    public ClipboardEntryViewModel(ClipboardEntry entry, IClipboardWriter writer)
    {
        Entry = entry;
        _writer = writer;
    }

    private static string BuildPreview(string text)
    {
        var collapsed = string.Join(" ", text.Split(
            ['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

        if (string.IsNullOrEmpty(collapsed))
            collapsed = text.Trim();

        const int maxLength = 140;
        return collapsed.Length > maxLength ? collapsed[..maxLength] + "…" : collapsed;
    }

    [RelayCommand]
    private void Copy() => _writer.SetText(Entry.Text);
}
