using System.IO.Compression;
using System.Xml.Linq;
using AnimeTracker;

namespace AnimeTracker.Desktop;

public sealed class AppStore
{
    public string Root { get; }
    public string LibraryPath => Path.Combine(Root, "library.xml");
    public Library Library { get; private set; } = new();
    public WatchChoices Choices { get; private set; } = new();
    public ViewPreferences View { get; private set; } = new();
    public WidgetOptions Widget { get; private set; } = new();
    public PersonalOrder Order { get; private set; } = new();
    public List<AnimeArc> Arcs { get; private set; } = new();
    public Dictionary<int, CatalogEntry> Metadata { get; } = new();
    public Dictionary<int, ScheduleEntry> Schedule { get; private set; } = new();
    public string Error { get; private set; } = "";
    public bool ReadOnly { get; private set; }
    public bool ChoicesBlocked { get; private set; }
    public bool ArcsBlocked { get; private set; }
    public int ModalDepth { get; set; }
    public event Action? Changed;

    public AppStore(string root) { Root = Path.GetFullPath(root); Reload(); }
    public string FilePath(string name) => Path.Combine(Root, name);
    public static string DefaultRoot() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "AnimeTracker")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnimeTracker.Port");
    public void Reload()
    {
        Directory.CreateDirectory(Root); Error = ""; ReadOnly = ChoicesBlocked = ArcsBlocked = false;
        var opened=LibraryStorage.Open(LibraryPath); Library=opened.Library; ReadOnly=opened.ReadOnly; Error=opened.Message;
        try { Choices = WatchChoices.Load(FilePath("watch-choices.xml")); } catch (Exception e) { Choices = new(); ChoicesBlocked = true; Error += " " + e.Message; }
        try { Arcs = ArcData.Load(FilePath("arcs.xml")); } catch (Exception e) { Arcs = new(); ArcsBlocked = true; Error += " Не удалось прочитать арки: " + e.Message; }
        View = ViewPreferences.Load(FilePath("view-settings.xml")); Widget = WidgetOptions.Load(FilePath("widget-settings.xml"));
        Order = PersonalOrder.Load(FilePath("personal-order.xml")); Schedule = CalendarNotes.Load(FilePath("schedule.xml"));
        Metadata.Clear();
        try { if (File.Exists(FilePath("evening-cache.xml"))) foreach (var x in XElement.Load(FilePath("evening-cache.xml")).Elements("anime")) { var id = (int)x.Attribute("id")!; Metadata[id] = new CatalogEntry { Id = id, Kind = (string?)x.Attribute("kind") ?? "", Duration = (int?)x.Attribute("duration") ?? 0, Episodes = (int?)x.Attribute("episodes") ?? 0, EpisodesAired = (int?)x.Attribute("aired") ?? 0, AiringStatus = (string?)x.Attribute("status") ?? "", Genres = (string?)x.Attribute("genres") ?? "" }; } } catch { Metadata.Clear(); }
        Notify();
    }
    void Writable() { if (ReadOnly) throw new InvalidOperationException(Error); }
    public void Notify() => Changed?.Invoke();
    public void SaveView() => View.Save(FilePath("view-settings.xml"));
    public void SetWidget(string pin, int style, bool? enabled=null) { var next = new WidgetOptions { Pinned = pin, Style = style, Enabled=enabled??Widget.Enabled }; next.Save(FilePath("widget-settings.xml")); Widget = next; Notify(); }
    public void ChangeChoices(Action<WatchChoices> edit) { if (ChoicesBlocked) throw new InvalidOperationException("Списки очереди и скрытия недоступны для записи."); var next = Choices.Copy(); edit(next); next.Save(FilePath("watch-choices.xml")); Choices = next; Notify(); }
    public Anime Add(CatalogEntry entry) { Writable(); var anime = Ratings.AddPlanned(Library, entry, LibraryPath); Metadata[entry.Id] = entry; Notify(); return anime; }
    public Anime AddManual(string title)
    {
        Writable(); title = title.Trim(); if (title.Length == 0) throw new ArgumentException("Введите название.");
        if (Library.Items.Any(a => string.Equals(a.Title, title, StringComparison.CurrentCultureIgnoreCase))) throw new ArgumentException("Такое название уже есть.");
        var anime = new Anime { Title = title, Status = "planned" }; var next = new Library { Left = Library.Left, Top = Library.Top, Items = Library.Items.Concat(new[] { anime }).ToList() }; next.Save(LibraryPath); Library.Items.Add(anime); Notify(); return anime;
    }
    public void Increment(Anime anime)
    {
        Writable(); if(LibraryStorage.Increment(Library,anime,LibraryPath))Notify();
    }
    public void Update(Anime anime, string title, int watched, int total, string status, int score, string notes) { Writable(); CardEdits.Save(Library, anime, title, watched, total, status, score, notes, LibraryPath); Notify(); }
    public void Status(Anime anime, string status) { Writable(); Ratings.UpdatePersonal(Library, anime, status, null, LibraryPath); Notify(); }
    public void Remove(Anime anime) { Writable(); var next = new Library { Left = Library.Left, Top = Library.Top, Items = Library.Items.Where(a => a != anime).ToList() }; next.Save(LibraryPath); Library.Items.Remove(anime); Notify(); }
    public void SaveArcs(List<AnimeArc> next) { if (ArcsBlocked) throw new InvalidOperationException("Файл арок недоступен для записи."); ArcData.Save(next, FilePath("arcs.xml")); Arcs = next; Notify(); }
    public int ImportMal(string source, bool importedStatuses)
    {
        Writable(); var incoming = MalImport.Read(source); var rows = MalImport.Preview(Library, incoming); var next = MalImport.Merge(Library, rows, importedStatuses); MalImport.Commit(Library, next, source, LibraryPath); Library = next; Notify(); return incoming.Count;
    }
    public async Task RefreshMetadata(IEnumerable<int> source, CancellationToken token)
    {
        var ids = source.Where(id => id > 0).Distinct().ToArray();
        for (int offset = 0; offset < ids.Length; offset += 50) { foreach (var entry in await Catalog.ByIds(ids.Skip(offset).Take(50), token)) Metadata[entry.Id] = entry; }
        new XElement("evening", Metadata.Values.Select(e => new XElement("anime", new XAttribute("id", e.Id), new XAttribute("kind", e.Kind), new XAttribute("duration", e.Duration), new XAttribute("episodes", e.Episodes), new XAttribute("aired", e.EpisodesAired), new XAttribute("status", e.AiringStatus), new XAttribute("genres", e.Genres)))).Save(FilePath("evening-cache.xml.tmp"));
        AtomicMove(FilePath("evening-cache.xml.tmp"), FilePath("evening-cache.xml"));
    }
    public async Task RefreshEpisodes(bool force, CancellationToken token)
    {
        if (ReadOnly || ModalDepth > 0) return;
        var ids = Library.Items.Where(a => a.CatalogId > 0 && (EpisodeUpdates.Due(a, DateTime.UtcNow) || ((a.Status == "watching" || a.Status == "planned") && (force || !Schedule.ContainsKey(a.CatalogId) || DateTime.UtcNow - Schedule[a.CatalogId].Checked >= TimeSpan.FromHours(6))))).Select(a => a.CatalogId).Distinct().ToArray();
        for (int offset = 0; offset < ids.Length; offset += 50)
        {
            var entries = await Catalog.ByIds(ids.Skip(offset).Take(50), token); if (ModalDepth > 0) return;
            var next = new Library { Left = Library.Left, Top = Library.Top, Items = Library.Items.Select(a => a.Copy()).ToList() }; bool changed = false;
            foreach (var anime in next.Items) { var entry = entries.FirstOrDefault(e => e.Id == anime.CatalogId); if (entry != null) changed = EpisodeUpdates.Apply(anime, entry, DateTime.UtcNow) || changed; }
            foreach (var entry in entries) { Schedule[entry.Id] = new ScheduleEntry { Id = entry.Id, Next = entry.NextEpisodeAt, Status = entry.AiringStatus, Checked = DateTime.UtcNow }; Metadata[entry.Id] = entry; }
            CalendarNotes.Save(Schedule, FilePath("schedule.xml"));
            if (changed) { next.Save(LibraryPath); foreach (var original in Library.Items) { var fresh = next.Items.First(a => a.Id == original.Id); original.Total = fresh.Total; original.EpisodesAired = fresh.EpisodesAired; original.AiringStatus = fresh.AiringStatus; original.EpisodesChecked = fresh.EpisodesChecked; } }
        }
        Notify();
    }
    static void AtomicMove(string temporary, string target) { if (File.Exists(target)) File.Replace(temporary, target, target + ".bak"); else File.Move(temporary, target); }
    public string ImportWindowsFolder(string source)
    {
        source = Path.GetFullPath(source); if (source.TrimEnd(Path.DirectorySeparatorChar) == Root.TrimEnd(Path.DirectorySeparatorChar)) throw new ArgumentException("Выберите другую папку данных.");
        AnimeTracker.Library.Load(Path.Combine(source, "library.xml")); if (!File.Exists(Path.Combine(source, "library.xml"))) throw new ArgumentException("В папке нет library.xml.");
        if (File.Exists(Path.Combine(source, "watch-choices.xml"))) WatchChoices.Load(Path.Combine(source, "watch-choices.xml")); if (File.Exists(Path.Combine(source, "arcs.xml"))) ArcData.Load(Path.Combine(source, "arcs.xml"));
        var stage = Root + ".import-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(stage);
        foreach (var name in new[] { "library.xml", "watch-choices.xml", "view-settings.xml", "widget-settings.xml", "personal-order.xml", "arcs.xml", "schedule.xml", "evening-cache.xml", "top-kinds.xml" }) { var path = Path.Combine(source, name); if (File.Exists(path)) File.Copy(path, Path.Combine(stage, name)); }
        foreach (var folder in new[] { "covers", "episode-flags" }) { var directory = Path.Combine(source, folder); if (!Directory.Exists(directory)) continue; Directory.CreateDirectory(Path.Combine(stage, folder)); foreach (var file in Directory.GetFiles(directory)) { var name = Path.GetFileName(file); if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d+\.(png|xml)$") && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.Copy(file, Path.Combine(stage, folder, name)); } }
        var backup = Root + ".before-import-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        Directory.Move(Root, backup); try { Directory.Move(stage, Root); } catch { Directory.Move(backup, Root); throw; }
        Reload(); return backup;
    }
    public void ExportBackup(string target) { if (Path.GetFullPath(target).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Сохраните архив за пределами папки данных."); ZipFile.CreateFromDirectory(Root, target, CompressionLevel.Optimal, false); }
}
