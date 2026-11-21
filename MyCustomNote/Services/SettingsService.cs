using System.IO;
using MyCustomNote.Core.Settings;

namespace MyCustomNote.Services;

/// <summary>The current <see cref="AppSettings"/>: every change is saved at once, and listeners are told.</summary>
internal sealed class SettingsService
{
    private readonly SettingsStore _store;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        Current = store.Load();
    }

    public AppSettings Current { get; private set; }

    /// <summary>Raised after a change made with <c>notify: true</c> (theme, font, colours, sort).</summary>
    public event EventHandler? Changed;

    /// <param name="change">Gets the current settings and returns the new ones.</param>
    /// <param name="notify">False for changes nobody needs to react to (window position and size).</param>
    public void Update(Func<AppSettings, AppSettings> change, bool notify = true)
    {
        Current = change(Current);

        try
        {
            _store.Save(Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference that cannot be saved is not worth interrupting the user; it still applies until the app closes.
        }

        if (notify)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
