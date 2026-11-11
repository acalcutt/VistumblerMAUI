using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VistumblerMAUI.ViewModels;

/// <summary>
/// One-off messages (saved and cleared, upload results, errors) for the notice line under the title bar. The
/// counts and GPS state are always in the title bar, so the routine "Scanning…" / "N active / M total" status no
/// longer needs a line of its own; the notice line shows only while there's something to say.
/// </summary>
public partial class ScanViewModel
{
    private static readonly TimeSpan NoticeShownFor = TimeSpan.FromSeconds(10);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = string.Empty;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    private int _noticeGeneration;

    /// <summary>Shows <paramref name="text"/> in the notice line. UI thread only.</summary>
    /// <param name="sticky">Errors stay until dismissed (or replaced); everything else goes after 10 seconds.</param>
    private void ShowNotice(string text, bool sticky = false)
    {
        int generation = ++_noticeGeneration;
        Notice = text;
        if (sticky) return;
        _ = Task.Delay(NoticeShownFor).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (generation == _noticeGeneration) Notice = string.Empty;
        }));
    }

    [RelayCommand]
    private void DismissNotice()
    {
        _noticeGeneration++;
        Notice = string.Empty;
    }
}
