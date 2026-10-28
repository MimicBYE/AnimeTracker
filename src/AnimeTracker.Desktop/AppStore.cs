using AnimeTracker;

namespace AnimeTracker.Desktop;

// Avalonia supplies its UI lifetime and OS-specific default location only.
public sealed class AppStore : TrackerService
{
    public int ModalDepth { get; set; }
    public AppStore(string root) : base(root) { }
    public static string DefaultRoot() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "AnimeTracker")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnimeTracker.Port");
    public Task RefreshEpisodes(bool force, CancellationToken token) => base.RefreshEpisodes(force, token, () => ModalDepth == 0);
}