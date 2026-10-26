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
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        try { return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args); }
        catch(Exception error) { int location=Array.IndexOf(args,"--data-dir");var root=location>=0&&location+1<args.Length?Path.GetFullPath(args[location+1]):AppStore.DefaultRoot(); Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root,"startup-error.txt"),error.ToString()); Console.Error.WriteLine(error); return 1; }
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
    TrayIcon? tray;
    bool updating;
    public bool Quitting { get; private set; }
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            int at = Array.IndexOf(Program.Arguments,"--data-dir");
            string root = at >= 0 && at + 1 < Program.Arguments.Length ? Program.Arguments[at + 1] : AppStore.DefaultRoot();
            Store = new AppStore(root); LibraryWindow = new MainWindow(Store); Widget = new WidgetWindow(Store);
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown; desktop.MainWindow = LibraryWindow;
            CreateMenus();
            if(!Program.Arguments.Contains("--startup"))LibraryWindow.Show();
            if(Store.Widget.Enabled)Widget.Show();
            refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            refresh.Tick += async (_,_) => { refresh.Interval = TimeSpan.FromMinutes(15); if(updating)return;updating=true;try{await Store.RefreshEpisodes(false,lifetime.Token);}catch(OperationCanceledException){}catch(Exception e){LibraryWindow.SetMessage("Обновление каталога недоступно: "+e.Message);}finally{updating=false;} };
            refresh.Start();
            desktop.Exit += (_,_) => { refresh.Stop();lifetime.Cancel();tray?.Dispose(); };
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
        tray = new TrayIcon { ToolTipText = "Anime Tracker", Menu = Menu(), IsVisible = true };
        try { using var stream = AssetLoader.Open(new Uri("avares://AnimeTracker/Assets/app.ico"));tray.Icon = new WindowIcon(stream); } catch(Exception e){LibraryWindow.SetMessage("Значок меню недоступен: "+e.Message);}
        tray.Clicked += (_,_) => OpenLibrary();TrayIcon.SetIcons(this,new TrayIcons { tray });
    }
    public void OpenLibrary(){LibraryWindow.Show();LibraryWindow.WindowState=WindowState.Normal;LibraryWindow.Activate();}
    public void Quit(){Quitting=true;lifetime.Cancel();if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.Shutdown();}
}
