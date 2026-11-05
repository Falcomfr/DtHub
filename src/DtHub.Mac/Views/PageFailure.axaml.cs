using Avalonia.Controls;

namespace DtHub.Mac.Views;

/// <summary>
/// The failed page's place: WebView2 shows its own error page, WebKit an
/// empty one. Said in a sentence, the engine's reason under it, and a
/// button to try again. The web view is a native view drawn above
/// everything, so its window hides it while this shows.
/// </summary>
internal sealed partial class PageFailure : UserControl
{
    public PageFailure()
    {
        InitializeComponent();

        RetryButton.Click += (_, _) => Retry?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The reader asked to load the page again.</summary>
    public event EventHandler? Retry;

    /// <summary>The engine's reason, as WebKit words it.</summary>
    public string? Reason
    {
        get => Detail.Text;
        set => Detail.Text = value;
    }
}
