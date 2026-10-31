using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AnimeTracker.Desktop;

internal static class Program
{
    internal static string[] Arguments = [];
    internal static AnimeTracker.TrackerFiles? DataFiles;
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        try
        {
            int at=Array.IndexOf(args,"--data-dir");if(at>=0&&at+1==args.Length)throw new ArgumentException("После --data-dir укажите папку.");
            DataFiles=at>=0?new AnimeTracker.TrackerFiles(args[at+1]):OperatingSystem.IsLinux()?LinuxPaths.Default():new AnimeTracker.TrackerFiles(AppStore.DefaultRoot());
            bool smoke=args.Contains("--linux-smoke-test");
            if(smoke&&(!OperatingSystem.IsLinux()||at<0||!Path.GetFileName(DataFiles.Root).StartsWith("anime-tracker-linux-smoke-",StringComparison.Ordinal)))throw new ArgumentException("Smoke requires a separate anime-tracker-linux-smoke-* folder on Linux.");
            if(args.Contains("--install-menu")){DesktopIntegration.InstallMenu();return 0;}
            if(OperatingSystem.IsLinux())LinuxPaths.Prepare(DataFiles);
            using var lease=OperatingSystem.IsLinux()?DataFolderLock.TryAcquire(DataFiles.Root):null;
            if(OperatingSystem.IsLinux()&&lease==null){if(!args.Contains("--startup"))DataFolderLock.RequestOpen(DataFiles.Root);return 0;}
            if(OperatingSystem.IsLinux()&&at<0)LinuxPaths.MigrateLegacy(DataFiles,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AnimeTracker.Port"));
            return AppBuilder.Configure<App>().UsePlatformDetect().With(new X11PlatformOptions{WmClass="AnimeTracker"}).LogToTrace().StartWithClassicDesktopLifetime(args);
        }
        catch(Exception error)
        {
            try{var root=OperatingSystem.IsLinux()?LinuxPaths.StateHome:DataFiles?.Root??AppStore.DefaultRoot();Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"startup-error.txt"),error.ToString());}catch{}
            Console.Error.WriteLine(error);return 1;
        }
    }
}
public sealed partial class App : Application
{
    public static App CurrentApp => (App)Current!;
    public AppStore Store { get; private set; } = null!;
    public MainWindow LibraryWindow { get; private set; } = null!;
    public WidgetWindow Widget { get; private set; } = null!;
    readonly CancellationTokenSource lifetime = new();
    DispatcherTimer? refresh;
    DispatcherTimer? openRequests;
    TrayIcon? tray;
    bool updating;
    public bool Quitting { get; private set; }
    public override void Initialize() {AvaloniaXamlLoader.Load(this);DesktopTheme.Apply(false);}
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Store = new AppStore(Program.DataFiles??new AnimeTracker.TrackerFiles(AppStore.DefaultRoot())); LibraryWindow = new MainWindow(Store); Widget = new WidgetWindow(Store);
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown; desktop.MainWindow = LibraryWindow;
            CreateMenus();
            if(OperatingSystem.IsLinux()||!Program.Arguments.Contains("--startup")||tray==null)LibraryWindow.Show();
            if(Store.Widget.Enabled)Widget.Show();
            refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            refresh.Tick += async (_,_) => { refresh.Interval = TimeSpan.FromMinutes(15); if(updating)return;updating=true;try{await Store.RefreshEpisodes(false,lifetime.Token);}catch(OperationCanceledException){}catch(Exception e){LibraryWindow.SetMessage("Обновление каталога недоступно: "+e.Message);}finally{updating=false;} };
            if(!Program.Arguments.Contains("--linux-smoke-test"))refresh.Start();
            if(OperatingSystem.IsLinux())
            {
                openRequests=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
                openRequests.Tick+=(_,_)=>{var path=Store.FilePath(".open-request");if(File.Exists(path)){try{File.Delete(path);}catch(IOException){return;}OpenLibrary();}};openRequests.Start();
            }
            desktop.Exit += (_,_) => { LibraryWindow.FlushPendingFilters();refresh.Stop();openRequests?.Stop();lifetime.Cancel();tray?.Dispose(); };
            if(Program.Arguments.Contains("--linux-smoke-test"))Dispatcher.UIThread.Post(RunLinuxSmoke,DispatcherPriority.ApplicationIdle);
        }
        base.OnFrameworkInitializationCompleted();
    }
    void CreateMenus()
    {
        NativeMenu Menu()
        {
            var menu = new NativeMenu();
            var open = new NativeMenuItem("Открыть библиотеку");open.Click += (_,_) => OpenLibrary();menu.Items.Add(open);
            var widget = new NativeMenuItem("Показать / скрыть виджет");widget.Click += (_,_) => {Store.SetWidget(Store.Widget.Pinned,Store.Widget.Style,!Store.Widget.Enabled);};menu.Items.Add(widget);
            var settings = new NativeMenuItem("Настройки");settings.Click += async (_,_) => {OpenLibrary();await LibraryWindow.Settings();};menu.Items.Add(settings);
            menu.Items.Add(new NativeMenuItemSeparator());
            var quit = new NativeMenuItem("Выход");quit.Click += (_,_) => Quit();menu.Items.Add(quit);return menu;
        }
        NativeMenu.SetMenu(this,Menu());
        if(Program.Arguments.Contains("--no-tray"))return;
        try
        {
            tray = new TrayIcon { ToolTipText = "Anime Tracker", Menu = Menu(), IsVisible = true };
            using var stream = AssetLoader.Open(new Uri("avares://AnimeTracker/Assets/app.ico"));tray.Icon = new WindowIcon(stream);
            tray.Clicked += (_,_) => OpenLibrary();TrayIcon.SetIcons(this,new TrayIcons { tray });
        }catch(Exception e){tray?.Dispose();tray=null;LibraryWindow.SetMessage("Трей недоступен: "+e.Message);}
    }
    public void OpenLibrary(){LibraryWindow.Show();LibraryWindow.WindowState=WindowState.Normal;LibraryWindow.Activate();}
    public void Quit(){LibraryWindow.FlushPendingFilters();Quitting=true;lifetime.Cancel();if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.Shutdown();}
}
