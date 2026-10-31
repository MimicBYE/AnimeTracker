using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace AnimeTracker {
public partial class Tracker : Application {
 TrackerService service;
 Library library {get{return service.Library;}}
 string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "library.xml");
 Window widget, full;
 StackPanel widgetRows, fullRows;
 TextBox search;
 ComboBox filter, sortChoice;
 Button genreFilter;
 ViewPreferences preferences;
 Forms.NotifyIcon tray;
 bool quitting;
 bool storageBlocked {get{return service.ReadOnly;}}
 Action undo;
 Button undoButton;
 Border widgetSurface;
 DockPanel widgetExpanded;
 TextBlock compactTitle, compactProgress;
 Image compactCover;
 int compactCoverId=-1;
 System.Threading.CancellationTokenSource compactCoverRequest;
 Button compactPlus;
 bool expanded, dragging, pointerDown, widgetHidden, recreating;
 EventHandler widgetAnimation;
 System.Windows.Threading.DispatcherTimer widgetAnimationTimer;
 int widgetAnimationFrames;
 Point pointerOrigin;
 double dragLeft, dragTop;
 System.Windows.Threading.DispatcherTimer clickTimer, desktopTimer;
 static System.Threading.Mutex mutex;
 static Brush Ink {get{return ThemeBrush("#47361D");}}
 static Brush Muted {get{return ThemeBrush("#725932");}}
 static Brush Accent {get{return ThemeBrush("#B9862C");}}
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
 void StyleWindow(Window window) {
  using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AnimeTracker.Icon")) {
   var icon=System.Windows.Media.Imaging.BitmapFrame.Create(stream,System.Windows.Media.Imaging.BitmapCreateOptions.None,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
   icon.Freeze(); window.Icon=icon;
  }
  window.SourceInitialized+=delegate {
   ApplyWindowTheme(window);
  };
 }
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
 [STAThread] public static void Main(string[] args) {
  if (args.Contains("--theme-test")) { ThemeTest(args); return; }
  if (args.Contains("--self-test")) { SelfTest(); return; }
  if (args.Contains("--top-export-test")) { VerifyTopExport(); return; }
  if (args.Contains("--import-test")) { try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"import-test.txt"),MalImportTests.Run()); } catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"import-test.txt"),"FAIL "+ex); Environment.ExitCode=1; } return; }
  if (args.Contains("--catalog-test")) { CatalogTest(); return; }
  if (args.Contains("--enable-startup")) {try{AutoStart.Set(true);StartupDiagnostics.Write("autostart-registered");}catch(Exception ex){StartupDiagnostics.Write("registration-failed",ex);Environment.ExitCode=1;}return;}
  StartupDiagnostics.Write(args.Contains("--startup")?"starting startup":"starting manual");
  try {
   bool created;mutex = new System.Threading.Mutex(true,"Local\\AnimeTracker.Desktop",out created);
   if (!created) {StartupDiagnostics.Write("already-running");if(!args.Contains("--startup"))ShowMessage("Трекер уже запущен. Откройте его через значок в трее.","Anime Tracker");return;}
   var app = new Tracker();app.ShutdownMode = ShutdownMode.OnExplicitShutdown;app.Run();GC.KeepAlive(mutex);
  } catch(Exception ex) {StartupDiagnostics.Write("failed",ex);try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-error.txt"),ex.ToString());}catch{}Environment.ExitCode=1;}
 }
 protected override void OnStartup(StartupEventArgs e) {
  if(isolatedThemeTest)return;
  base.OnStartup(e);
  StartupDiagnostics.Write("initializing");
  InitializeThemeResources();
  service=new TrackerService(Path.GetDirectoryName(dataPath));
  service.AppearanceChanged+=ApplyAppearance;ApplyAppearance();
  service.Changed+=ApplyAppearance;
  if(service.Error.Length>0)ShowMessage(service.Error,storageBlocked?"Ошибка загрузки":"Загрузка данных");
  preferences=service.View;
  StartupDiagnostics.Write("library-loaded");

  StartEpisodeUpdates();
  widgetHidden=!widgetOptions.Enabled; BuildWidget(); BuildTray(); if(!widgetHidden){widget.Show(); AttachDesktop();}
  StartupDiagnostics.Write("tray-created");
  desktopTimer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromSeconds(3) };
  desktopTimer.Tick += delegate { EnsureWidget(); };
  desktopTimer.Start();
  if(e.Args.Contains("--library")||(!widgetOptions.Enabled&&!e.Args.Contains("--startup"))) OpenLibrary();
  if(e.Args.Contains("--verify")) Dispatcher.BeginInvoke(new Action(VerifyWidget),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  StartupDiagnostics.Write("running mode="+(e.Args.Contains("--startup")?"startup":"manual")+" library-open="+(full!=null)+" widget-enabled="+widgetOptions.Enabled);
 }
 async void VerifyWidget() {
  try {
   widget.UpdateLayout(); SavePreview("widget-compact.png");
   string compact=DesktopHost.Status(widget);
   SetExpanded(true,false); widget.UpdateLayout(); SavePreview("widget-expanded.png");
   string large=DesktopHost.Status(widget);
   SetExpanded(false,false);
   SetExpanded(true);
   await System.Threading.Tasks.Task.Delay(500);
   int frames=widgetAnimationFrames;
   if(Math.Abs(widget.Height-340)>0.1 || widgetAnimation!=null || frames<2) throw new Exception("Expansion animation did not finish; frames="+frames);
   SetExpanded(false);
   await System.Threading.Tasks.Task.Delay(500);
   if(Math.Abs(widget.Height-CompactSize().Height)>0.1 || widgetAnimation!=null) throw new Exception("Collapse animation did not finish");
   int savedStyle=widgetOptions.Style;
   try {
    for(int style=0;style<3;style++) {widgetOptions.Style=style; ApplyWidgetStyle(); widget.UpdateLayout(); SavePreview("widget-style-"+style+".png"); if(widget.Width!=CompactSize().Width || widget.Height!=CompactSize().Height || !DesktopHost.Status(widget).Contains("attached=True; child=True")) throw new Exception("Widget style geometry/attachment");}
   } finally {widgetOptions.Style=savedStyle;ApplyWidgetStyle();}
   if(!compact.Contains("attached=True; child=True") || !large.Contains("attached=True; child=True")) throw new Exception("Desktop attachment failed: "+compact+" / "+large);
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"desktop-test.txt"),"PASS expansion/collapse animation; frames="+frames+"; desktop parent and WS_CHILD; compact: "+compact+"; expanded: "+large+"; startup="+AutoStart.Enabled);
  } catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"desktop-test.txt"),"FAIL "+ex); }
 }
 void SavePreview(string name) {
  var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)widget.Width,(int)widget.Height,96,96,PixelFormats.Pbgra32); image.Render(widgetSurface);
  var png=new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
  using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name))) png.Save(stream);
 }
 TextBlock Text(string text, double size, Brush brush) { return new TextBlock { Text=text, FontSize=size, Foreground=brush, TextWrapping=TextWrapping.Wrap }; }
 Button Btn(string text, Action action) { var b = new Button { Content=text }; b.Click += delegate { action(); }; return b; }
 Button Primary(Button button) { button.Background=ThemeBrush(Color.FromRgb(221,180,74));button.Foreground=ThemeBrush("#241B0B");button.FontWeight=FontWeights.SemiBold;return button; }
 Button Quiet(Button button) { button.Background=Brushes.Transparent;button.Foreground=Muted;return button; }
 void Persist() { if (storageBlocked) return; try { service.Save(); } catch (Exception ex) { ShowMessage("Не удалось сохранить изменения:\n" + ex.Message,"Ошибка сохранения"); } }
 void BuildWidget() {
  StopWidgetAnimation();
  if(clickTimer!=null) clickTimer.Stop();
  expanded=false;
  widget = new Window { Title="Anime Tracker • виджет", Width=CompactSize().Width, Height=CompactSize().Height, WindowStyle=WindowStyle.None, AllowsTransparency=false, Background=ThemeBrush(Color.FromRgb(249,231,165)), ShowInTaskbar=false, ResizeMode=ResizeMode.NoResize, ShowActivated=false };
  if(compactCoverRequest!=null) { compactCoverRequest.Cancel(); compactCoverRequest.Dispose(); compactCoverRequest=null; } compactCoverId=-1;
  widgetSurface=new Border { Background=Paper(), BorderBrush=ThemeBrush(Color.FromRgb(208,180,111)), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(3,3,16,3), Padding=new Thickness(14,10,12,10) };
  widget.Content=widgetSurface;
  var layout=new Grid(); widgetSurface.Child=layout;
  var small=new StackPanel { Name="Compact" };
  var heading=new DockPanel();
  var quickAdd=Btn("＋",delegate { Edit(null,null); }); quickAdd.ToolTip="Добавить аниме"; quickAdd.Padding=new Thickness(5,0,5,0); quickAdd.Margin=new Thickness(0); quickAdd.Background=Brushes.Transparent; DockPanel.SetDock(quickAdd,Dock.Right); heading.Children.Add(quickAdd);
  var settings=Btn("⚙",delegate {WidgetSettings(null);}); settings.ToolTip="Настройки"; settings.Padding=new Thickness(4,0,4,0); settings.Margin=new Thickness(0); settings.Background=Brushes.Transparent; DockPanel.SetDock(settings,Dock.Right); heading.Children.Add(settings);
  widgetNext=Btn("›",delegate {CycleWidget(1);}); widgetPrevious=Btn("‹",delegate {CycleWidget(-1);});
  foreach(var arrow in new[]{widgetNext,widgetPrevious}) {arrow.Padding=new Thickness(4,0,4,0);arrow.Margin=new Thickness(0);arrow.Background=Brushes.Transparent;arrow.ToolTip="Переключить и закрепить аниме из «Смотрю»";DockPanel.SetDock(arrow,Dock.Right);heading.Children.Add(arrow);}
  var label=Text("Anime Tracker",12,Muted); label.FontFamily=new FontFamily("Georgia"); heading.Children.Add(label); small.Children.Add(heading);
  var summary=new DockPanel { Margin=new Thickness(0,7,0,0) }; widgetSummary=summary;
  compactPlus=Btn("+1",delegate { var a=WidgetAnime(); if(a!=null && !a.Completed) Increment(a); }); compactPlus.Margin=new Thickness(6,0,0,0); compactPlus.VerticalAlignment=VerticalAlignment.Center; DockPanel.SetDock(compactPlus,Dock.Right); summary.Children.Add(compactPlus);
  compactCover=new Image { Width=40,Height=58,Stretch=Stretch.UniformToFill,Margin=new Thickness(0,0,9,0),VerticalAlignment=VerticalAlignment.Top };
  DockPanel.SetDock(compactCover,Dock.Left); summary.Children.Add(compactCover);
  var titles=new StackPanel(); compactTitle=Text("Добавьте первую историю",14,Ink); compactTitle.FontWeight=FontWeights.SemiBold; compactTitle.TextWrapping=TextWrapping.NoWrap; compactTitle.TextTrimming=TextTrimming.CharacterEllipsis; titles.Children.Add(compactTitle); compactProgress=Text("клик — раскрыть",11,Muted); compactProgress.Margin=new Thickness(0,4,0,0); titles.Children.Add(compactProgress); widgetProgressBar=new ProgressBar {Minimum=0,Maximum=1,Height=3,Margin=new Thickness(0,5,0,0),Foreground=Accent,Background=ThemeBrush(Color.FromRgb(232,213,157)),BorderThickness=new Thickness(0)}; titles.Children.Add(widgetProgressBar); summary.Children.Add(titles); small.Children.Add(summary); layout.Children.Add(small);
  widgetExpanded=new DockPanel { Visibility=Visibility.Collapsed }; layout.Children.Add(widgetExpanded);
  var header=new DockPanel { Margin=new Thickness(0,0,0,12) }; var open=Btn("↗",OpenLibrary); open.ToolTip="Открыть библиотеку"; open.Padding=new Thickness(6,2,6,2); DockPanel.SetDock(open,Dock.Right); header.Children.Add(open);
  var title=Text("Anime Tracker",20,Ink); title.FontFamily=new FontFamily("Georgia"); header.Children.Add(title); DockPanel.SetDock(header,Dock.Top); widgetExpanded.Children.Add(header);
  var bottom=new StackPanel { Margin=new Thickness(0,8,0,0) };
  var add=Btn("＋ Добавить аниме",delegate { Edit(null,null); }); add.Margin=new Thickness(0); bottom.Children.Add(add);
  undoButton=Btn("↶ Отменить действие",delegate { if(undo!=null && !storageBlocked) { try {undo();undo=null;Refresh();}catch(Exception ex){ShowMessage("Не удалось отменить действие:\n"+ex.Message,"Ошибка сохранения");} } }); undoButton.FontSize=11; undoButton.Margin=new Thickness(0,6,0,0); bottom.Children.Add(undoButton); DockPanel.SetDock(bottom,Dock.Bottom); widgetExpanded.Children.Add(bottom);
  widgetRows=new StackPanel(); widgetExpanded.Children.Add(new SmoothScrollViewer { Content=widgetRows, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
  var area=SystemParameters.WorkArea;
  widget.Left=library.Left < 0 ? area.Right-widget.Width-28 : Math.Max(area.Left,Math.Min(library.Left,area.Right-widget.Width));
  widget.Top=library.Top < 0 ? area.Top+70 : Math.Max(area.Top,Math.Min(library.Top,area.Bottom-widget.Height));
  library.Left=widget.Left; library.Top=widget.Top;
  clickTimer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
  clickTimer.Tick += delegate { clickTimer.Stop(); SetExpanded(!expanded); };
  widgetSurface.ToolTip="Клик — свернуть / раскрыть · двойной клик — библиотека · потяните за бумагу, чтобы переместить";
  widgetSurface.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs ev) {
   if(IsButtonChild(ev.OriginalSource as DependencyObject) || IsRowChild(ev.OriginalSource as DependencyObject)) return;
   if(ev.ClickCount==2) { clickTimer.Stop(); pointerDown=false; widgetSurface.ReleaseMouseCapture(); OpenLibrary(); ev.Handled=true; return; }
   if(widgetAnimation!=null) SetExpanded(expanded,false);
   pointerDown=true; dragging=false; pointerOrigin=DesktopHost.Cursor(); dragLeft=library.Left; dragTop=library.Top; widgetSurface.CaptureMouse(); ev.Handled=true;
  };
  widgetSurface.MouseMove += delegate {
   if(!pointerDown) return; var pos=DesktopHost.Cursor(); var delta=pos-pointerOrigin;
   if(!dragging && (Math.Abs(delta.X)>SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y)>SystemParameters.MinimumVerticalDragDistance)) { dragging=true; clickTimer.Stop(); }
   if(dragging) { library.Left=dragLeft+delta.X/DesktopHost.Scale(widget); library.Top=dragTop+delta.Y/DesktopHost.Scale(widget); ClampPosition(); }
  };
  widgetSurface.MouseLeftButtonUp += delegate(object s,MouseButtonEventArgs ev) { if(!pointerDown) return; pointerDown=false; widgetSurface.ReleaseMouseCapture(); if(dragging) { ClampPosition(); Persist(); } else { clickTimer.Stop(); clickTimer.Start(); } ev.Handled=true; };
  widgetSurface.LostMouseCapture += delegate { bool interrupted=pointerDown && dragging; pointerDown=false; if(interrupted) { dragging=false; ClampPosition(); Persist(); } };
  widget.Closing += delegate(object s,System.ComponentModel.CancelEventArgs ev) { if(!quitting && !recreating && DesktopHost.IsWindow(new WindowInteropHelper(widget).Handle)) ev.Cancel=true; };
  ApplyWidgetStyle(); Refresh();
 }
 Brush Paper() {
  var drawing=new DrawingGroup(); drawing.Children.Add(new GeometryDrawing(ThemeBrush(Color.FromRgb(249,231,165)),null,new RectangleGeometry(new Rect(0,0,80,24))));
  drawing.Children.Add(new GeometryDrawing(null,new Pen(ThemeBrush(Color.FromArgb(28,155,119,50)),0.6),new LineGeometry(new Point(0,23.5),new Point(80,23.5))));
  var random=new Random(42); for(int i=0;i<30;i++) drawing.Children.Add(new GeometryDrawing(ThemeBrush(Color.FromArgb(15,115,88,37)),null,new EllipseGeometry(new Point(random.Next(80),random.Next(23)),0.45,0.45)));
  return new DrawingBrush(drawing) { TileMode=TileMode.Tile, ViewportUnits=BrushMappingMode.Absolute, Viewport=new Rect(0,0,80,24), Stretch=Stretch.None };
 }
 bool IsRowChild(DependencyObject obj) { while(obj!=null) { var fe=obj as FrameworkElement; if(fe!=null && fe.Tag as string=="row") return true; obj=VisualTreeHelper.GetParent(obj); } return false; }
 static Point ClampWidgetPoint(double left,double top,double width,double height,Rect area) {
  return new Point(Math.Max(area.Left,Math.Min(left,area.Right-width)),Math.Max(area.Top,Math.Min(top,area.Bottom-height)));
 }
 void ClampPosition() { var point=ClampWidgetPoint(library.Left,library.Top,widget.Width,widget.Height,SystemParameters.WorkArea); library.Left=point.X; library.Top=point.Y; DesktopHost.Place(widget,library.Left,library.Top); }
 void StopWidgetAnimation() { if(widgetAnimationTimer!=null) { widgetAnimationTimer.Stop(); widgetAnimationTimer.Tick-=widgetAnimation; widgetAnimationTimer=null; } widgetAnimation=null; }
 void SetExpanded(bool value,bool animate=true) {
  StopWidgetAnimation(); widgetAnimationFrames=0; expanded=value;
  var grid=(Grid)widgetSurface.Child; grid.ClipToBounds=true;
  grid.Children[0].Visibility=value?Visibility.Collapsed:Visibility.Visible; widgetExpanded.Visibility=value?Visibility.Visible:Visibility.Collapsed;
  double fromWidth=widget.Width,fromHeight=widget.Height,toWidth=value?280:CompactSize().Width,toHeight=value?340:CompactSize().Height;
  double fromLeft=library.Left,fromTop=library.Top; var area=SystemParameters.WorkArea;
  double toLeft=Math.Max(area.Left,Math.Min(fromLeft,area.Right-toWidth)),toTop=Math.Max(area.Top,Math.Min(fromTop,area.Bottom-toHeight));
  Action<double> frame=delegate(double progress) {
   widget.Width=fromWidth+(toWidth-fromWidth)*progress; widget.Height=fromHeight+(toHeight-fromHeight)*progress;
   library.Left=fromLeft+(toLeft-fromLeft)*progress; library.Top=fromTop+(toTop-fromTop)*progress;
   DesktopHost.Place(widget,library.Left,library.Top);
  };
  if(!animate || !widget.IsVisible) { frame(1); return; }
  var clock=System.Diagnostics.Stopwatch.StartNew();
  widgetAnimation=delegate { widgetAnimationFrames++; double t=Math.Min(1,clock.Elapsed.TotalMilliseconds/200); frame(1-Math.Pow(1-t,3)); if(t>=1) StopWidgetAnimation(); };
  widgetAnimationTimer=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render) { Interval=TimeSpan.FromMilliseconds(16) };
  widgetAnimationTimer.Tick+=widgetAnimation; widgetAnimationTimer.Start();
 }
 void AttachDesktop() { if(DesktopHost.Attach(widget)) DesktopHost.Place(widget,library.Left,library.Top); }
 void EnsureWidget() {
  if(quitting || pointerDown || widgetAnimation!=null || widgetHidden) return;
  if(!DesktopHost.IsWindow(new WindowInteropHelper(widget).Handle)) {
   recreating=true; try { BuildWidget(); } finally { recreating=false; }
  }
  if(widgetHidden) return;
  if(!widget.IsVisible) widget.Show();
  if(widget.WindowState!=WindowState.Normal) widget.WindowState=WindowState.Normal;
  ClampPosition(); AttachDesktop();
 }
 void RestoreWidget() {
  if(!SaveWidgetOptions(widgetOptions.Pinned,widgetOptions.Style,true))return; EnsureWidget();
  var area=SystemParameters.WorkArea; library.Left=area.Right-widget.Width-28; library.Top=area.Top+70;
  ClampPosition(); AttachDesktop(); Persist();
 }
 static Anime LatestAnime(Library source) { return source.Items.OrderByDescending(a=>a.Updated).FirstOrDefault(); }
 void Increment(Anime a) {
  if(storageBlocked)return;
  try {var before=a.Copy();if(!service.Increment(a))return;undo=delegate{service.RestoreProgress(a,before);};Refresh();AnimateWidgetProgress(a);}
  catch(Exception ex){ShowMessage("Не удалось сохранить серию:\n"+ex.Message,"Ошибка сохранения");}
 }
 bool IsButtonChild(DependencyObject obj) { while (obj!=null) { if (obj is System.Windows.Controls.Primitives.ButtonBase || obj is System.Windows.Controls.Primitives.ScrollBar) return true; obj=VisualTreeHelper.GetParent(obj); } return false; }
 void BuildTray() {
  tray=new Forms.NotifyIcon { Text="Anime Tracker", Icon=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location), Visible=true };
  var menu=new Forms.ContextMenuStrip();
  menu.Items.Add("Открыть библиотеку",null,delegate { Dispatcher.Invoke(new Action(OpenLibrary)); });
  menu.Items.Add("Настройки…",null,delegate { Dispatcher.Invoke(new Action(OpenSettings)); });
  menu.Items.Add("Импорт из MyAnimeList…",null,delegate { Dispatcher.Invoke(new Action(delegate { OpenLibrary(); ImportMal(); })); });
  menu.Items.Add("Показать / скрыть виджет",null,delegate { Dispatcher.Invoke(new Action(delegate { SaveWidgetOptions(widgetOptions.Pinned,widgetOptions.Style,!widgetOptions.Enabled); })); });
  menu.Items.Add("Вернуть виджет на экран",null,delegate { Dispatcher.Invoke(new Action(RestoreWidget)); });
  menu.Items.Add("Добавить аниме",null,delegate { Dispatcher.Invoke(new Action(delegate { Edit(null,null); })); });
  var startup=new Forms.ToolStripMenuItem("Запускать вместе с Windows") { Checked=AutoStart.Enabled };
  startup.Click += delegate { try { AutoStart.Set(!AutoStart.Enabled); startup.Checked=AutoStart.Enabled; } catch(Exception ex) { ShowMessage("Не удалось изменить автозапуск: "+ex.Message); } }; menu.Items.Add(startup);
  menu.Opening += delegate { startup.Checked=AutoStart.Enabled; };
  menu.Items.Add(new Forms.ToolStripSeparator());
  menu.Items.Add("Выход",null,delegate { Dispatcher.Invoke(new Action(delegate { Persist(); quitting=true; tray.Dispose(); Shutdown(); })); });
  tray.ContextMenuStrip=menu; ApplyTrayTheme(); tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(OpenLibrary)); };
 }
 void OpenLibrary() {
  if (full!=null) { full.Show(); full.WindowState=WindowState.Normal; full.Activate(); return; }
  full=new Window { Title="Anime Tracker — библиотека", Width=900, Height=720, MinWidth=780, MinHeight=400, WindowStartupLocation=WindowStartupLocation.CenterScreen };
  StyleWindow(full);
  var panel=new DockPanel { Margin=new Thickness(28) };
  var tabs=new TabControl { FocusVisualStyle=null,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0) };
  libraryTabs=tabs;
  tabs.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TabControl'><Grid KeyboardNavigation.TabNavigation='Local'><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions><DockPanel x:Name='HeaderPanel' IsItemsHost='True' LastChildFill='False' Margin='0,0,8,0' KeyboardNavigation.TabIndex='1'/><ContentPresenter x:Name='PART_SelectedContentHost' Grid.Row='1' ContentSource='SelectedContent' Margin='{TemplateBinding Padding}'/></Grid></ControlTemplate>");
  tabs.Items.Add(new TabItem { Header="Библиотека",Content=panel }); tabs.Items.Add(BuildEveningTab(full)); tabs.Items.Add(BuildCalendarTab(full)); tabs.Items.Add(BuildSequelsTab(full)); tabs.Items.Add(BuildTopTab(false,full)); tabs.Items.Add(BuildTopTab(true,full)); settingsTab=BuildSettingsTab(full);tabs.Items.Add(settingsTab);full.Content=tabs; AnimateTabs(tabs);
  var head=new StackPanel(); head.Children.Add(Text("МОЯ КОЛЛЕКЦИЯ",11,Accent)); head.Children.Add(Text("Библиотека аниме",30,Ink));
  var toolbar=new DockPanel { Margin=new Thickness(0,22,0,20) };
  var add=Primary(Btn("＋ Добавить",delegate { Edit(null,full); })); DockPanel.SetDock(add,Dock.Right); toolbar.Children.Add(add);
  var restore=Btn("Показать виджет",RestoreWidget); DockPanel.SetDock(restore,Dock.Right); toolbar.Children.Add(restore);
  filter=new ComboBox { Width=145, Margin=new Thickness(0,0,12,0), VerticalContentAlignment=VerticalAlignment.Center, ItemsSource=new [] { "Все аниме", "Смотрю", "Просмотрено", "В планах", "Отложено", "Брошено" }, SelectedIndex=Array.IndexOf(MalImport.StatusKeys,preferences.Status)+1 }; DockPanel.SetDock(filter,Dock.Right); toolbar.Children.Add(filter);
  search=new TextBox { Text=preferences.Search, ToolTip="Поиск по названию", Margin=new Thickness(0,0,12,0) }; toolbar.Children.Add(search); head.Children.Add(Text("Поиск по названию",12,Muted)); head.Children.Add(toolbar);
  var options=new DockPanel { Margin=new Thickness(0,0,0,16) };
  var sortPanel=new StackPanel { Width=275 }; sortPanel.Children.Add(Text("Сортировка",12,Muted));
  sortChoice=new ComboBox { ItemsSource=LibraryView.SortLabels,SelectedIndex=preferences.Sort,Padding=new Thickness(6),Margin=new Thickness(0,4,0,0) }; sortPanel.Children.Add(sortChoice); DockPanel.SetDock(sortPanel,Dock.Right); options.Children.Add(sortPanel);
  var genrePanel=new StackPanel { Margin=new Thickness(0,0,14,0) }; genrePanel.Children.Add(Text("Жанры · можно выбрать несколько",12,Muted));
  genreFilter=Btn("Все жанры ▾",OpenGenreMenu); genreFilter.Margin=new Thickness(0,4,0,0); genreFilter.HorizontalContentAlignment=HorizontalAlignment.Left; genrePanel.Children.Add(genreFilter); options.Children.Add(genrePanel); head.Children.Add(options);
  DockPanel.SetDock(head,Dock.Top); panel.Children.Add(head);
  fullRows=new StackPanel(); panel.Children.Add(new SmoothScrollViewer { Content=fullRows, VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
  search.TextChanged += delegate { SaveView(); Refresh(); }; filter.SelectionChanged += delegate { SaveView(); Refresh(); };
  sortChoice.SelectionChanged += delegate { SaveView(); Refresh(); };
  full.Closed += delegate { full=null; fullRows=null; search=null; filter=null; sortChoice=null; genreFilter=null; libraryTabs=null;settingsTab=null; };
  Refresh(); full.Show();
 }
 void SaveView() {
  if(search==null || filter==null || sortChoice==null) return;
  preferences.Search=search.Text; preferences.Sort=Math.Max(0,sortChoice.SelectedIndex); preferences.Status=filter.SelectedIndex<=0?"":MalImport.StatusKeys[filter.SelectedIndex-1];
  try { service.SaveView(); } catch(Exception ex) { ShowMessage("Не удалось сохранить настройки вида:\n"+ex.Message); }
 }
 void OpenGenreMenu() {
  var menu=new ContextMenu { MaxHeight=450, PlacementTarget=genreFilter, Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom };
  var mode=new MenuItem { Header="Требовать все выбранные жанры", IsCheckable=true, IsChecked=preferences.MatchAll, StaysOpenOnClick=true };
  mode.ToolTip="Включено: все выбранные жанры одновременно. Выключено: хотя бы один из них.";
  mode.Click+=delegate { preferences.MatchAll=mode.IsChecked; SaveView(); Refresh(); }; menu.Items.Add(mode);
  var reset=new MenuItem { Header="Снять все отметки" }; reset.Click+=delegate { preferences.Genres.Clear(); SaveView(); Refresh(); }; menu.Items.Add(reset); menu.Items.Add(new Separator());
  var choices=new[]{"Без жанров"}.Concat(library.Items.SelectMany(a=>LibraryView.Genres(a)).Concat(preferences.Genres).Where(g=>g!="Без жанров").Distinct().OrderBy(g=>g,StringComparer.CurrentCultureIgnoreCase));
  foreach(string genre in choices) {
   string value=genre; var option=new MenuItem { Header=value, IsCheckable=true, IsChecked=preferences.Genres.Contains(value), StaysOpenOnClick=true };
   option.Click+=delegate { if(option.IsChecked) { if(!preferences.Genres.Contains(value)) preferences.Genres.Add(value); } else preferences.Genres.Remove(value); SaveView(); Refresh(); }; menu.Items.Add(option);
  }
  genreFilter.ContextMenu=menu; menu.IsOpen=true;
 }
 Border Row(Anime a, bool compact) {
  var border=new Border { Tag="row", Background=ThemeBrush(Color.FromArgb(compact?(byte)70:(byte)255,255,249,225)), BorderBrush=ThemeBrush(Color.FromArgb(80,190,159,87)), BorderThickness=new Thickness(0,0,0,1), CornerRadius=new CornerRadius(3), Padding=new Thickness(compact?7:16), Margin=new Thickness(0,0,0,compact?8:12) };
  var dock=new DockPanel(); border.Child=dock;
  var buttons=new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center };
  var plus=Btn("+1",delegate { Increment(a); });
  plus.FontSize=18; plus.Foreground=Accent; plus.IsEnabled=!a.Completed; plus.ToolTip="Отметить следующую серию"; buttons.Children.Add(plus);
  if (!compact) { Primary(plus);plus.Foreground=ThemeBrush("#241B0B");buttons.Children.Add(Quiet(Btn(a.Notes.Length>0?"Карточка •":"Карточка",delegate { Edit(a,full); }))); }
  DockPanel.SetDock(buttons,Dock.Right); dock.Children.Add(buttons);
  if(a.CatalogId>0) { var cover=new Image { Width=compact?32:64, Height=compact?46:90, Stretch=Stretch.Uniform, Margin=new Thickness(0,0,compact?7:16,0) }; DockPanel.SetDock(cover,Dock.Left); dock.Children.Add(cover); LoadCover(cover,a.CatalogId,a.CoverUrl,System.Threading.CancellationToken.None); }
  var labels=new StackPanel { Margin=new Thickness(0,0,10,0), Cursor=Cursors.Hand }; labels.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { Edit(a,compact?null:full); e.Handled=true; };
  labels.Children.Add(Text(a.Title,compact?14:18,Ink));
  if(!compact) { var names=String.Join(" · ",new[]{a.English,a.Japanese,a.Romaji}.Where(s=>!String.IsNullOrWhiteSpace(s) && s!=a.Title).Distinct()); if(names.Length>0) {var alternative=Text(names,12,Muted);alternative.Margin=new Thickness(0,4,0,0);labels.Children.Add(alternative);} }
  var progress=Text((MalImport.StatusLabel(a.Status)+" · ") + a.Watched + (a.Total>0?" / " + a.Total:" / ?") + " серий" + (a.AiringStatus=="ongoing"?" · вышло "+a.EpisodesAired:""),compact?12:13,compact?Muted:Ink);progress.Margin=new Thickness(0,compact?0:10,0,0);labels.Children.Add(progress);
  if(a.Rewatching) labels.Children.Add(Text("Пересмотр · серия "+a.RewatchEpisode,11,Muted));
  if(a.Total>0) labels.Children.Add(new ProgressBar { Minimum=0, Maximum=a.Total, Value=a.Watched, Height=compact?3:4, Margin=new Thickness(0,8,0,compact?0:8), Foreground=Accent, Background=ThemeBrush(Color.FromRgb(232,213,157)), BorderThickness=new Thickness(0) });
  if(!compact) {var details=new WrapPanel();var score=Text(a.Score>0?"★ "+a.Score+" / 10":"☆ Без оценки",12,Ink);score.Margin=new Thickness(0,0,16,4);details.Children.Add(score);if(a.Genres.Length>0)details.Children.Add(Text(String.Join(" · ",LibraryView.Genres(a)),12,Muted));labels.Children.Add(details);}
  dock.Children.Add(labels); return border;
 }
 void Refresh() {
  if (widgetRows==null) return;
  widgetRows.Children.Clear(); var active=library.Items.OrderByDescending(a=>a.Updated).ToList();
  var shown=WidgetAnime();
  compactTitle.Text=shown==null?"Следующая история…":shown.Title;
  compactProgress.Foreground=Muted;
  compactProgress.Text=shown==null?"клик — раскрыть список":shown.Watched+" / "+(shown.Total>0?shown.Total.ToString():"?")+" · "+MalImport.StatusLabel(shown.Status);
  compactTitle.ToolTip=shown==null?"Добавьте аниме":shown.Title+(widgetOptions.Pinned==shown.Id?" · закреплено":" · автоматически");
  UpdateCompactCover(shown);
  compactPlus.IsEnabled=shown!=null && !shown.Completed && (shown.Total==0 || shown.Watched<shown.Total); compactPlus.Visibility=shown==null?Visibility.Collapsed:Visibility.Visible;
  widgetProgressBar.Maximum=Math.Max(1,shown==null?1:shown.Total>0?shown.Total:shown.Watched); widgetProgressBar.Value=shown==null?0:shown.Watched;
  widgetPrevious.IsEnabled=widgetNext.IsEnabled=library.Items.Count(a=>a.Status=="watching" || a==shown)>1;
  if (active.Count==0) { var empty=new StackPanel { Margin=new Thickness(0,18,0,18) }; var t=Text("Здесь начнётся\nновая история",21,Ink); t.FontFamily=new FontFamily("Georgia"); empty.Children.Add(t); var hint=Text("Добавьте аниме, а затем отмечайте серии кнопкой +1.",12,Muted); hint.Margin=new Thickness(0,14,0,0); empty.Children.Add(hint); widgetRows.Children.Add(empty); }
  foreach(var a in active.Take(10)) widgetRows.Children.Add(Row(a,true));
  if(active.Count>10) widgetRows.Children.Add(Btn("Вся библиотека →",OpenLibrary));
  undoButton.Visibility=undo==null?Visibility.Collapsed:Visibility.Visible;
  if (fullRows==null) return; fullRows.Children.Clear();
  genreFilter.Content=preferences.Genres.Count==0?"Все жанры ▾":"Выбрано: "+preferences.Genres.Count+(preferences.MatchAll?" · все одновременно ▾":" · любой из них ▾");
  genreFilter.ToolTip=String.Join(", ",preferences.Genres);
  var items=LibraryView.Select(library.Items,search.Text,filter.SelectedIndex<=0?"":MalImport.StatusKeys[filter.SelectedIndex-1],preferences.Genres,preferences.MatchAll,sortChoice.SelectedIndex);
  fullRows.Children.Add(Text(items.Count + " аниме · " + items.Sum(a=>a.Watched) + " просмотренных серий",12,Muted));
  foreach(var a in items) fullRows.Children.Add(Row(a,false));
  if (items.Count==0) fullRows.Children.Add(Text("Пока ничего нет. Добавьте аниме или измените поиск.",16,Muted));
 }
 void Edit(Anime existing, Window owner) {
  if(existing!=null) { OpenAnimeCard(existing,owner); return; }
  clickTimer.Stop();
  var dialog=new Window { Title=existing==null?"Добавить аниме":"Изменить аниме", Width=610, Height=Math.Min(850,SystemParameters.WorkArea.Height-50), ResizeMode=ResizeMode.NoResize, Owner=owner, WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner, ShowInTaskbar=owner==null };
  StyleWindow(dialog);
  var p=new StackPanel { Margin=new Thickness(24) }; dialog.Content=new ScrollViewer { Content=p, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
  p.Children.Add(Text(existing==null?"Новая история":"Редактирование",24,Ink));
  var lifetime=new System.Threading.CancellationTokenSource();
  System.Threading.CancellationTokenSource searchRequest=null;
  int generation=0; bool closed=false;
  CatalogEntry selected=existing!=null && existing.CatalogId>0?new CatalogEntry { Id=existing.CatalogId, English=existing.English, Japanese=existing.Japanese, Romaji=existing.Romaji, CoverUrl=existing.CoverUrl, Genres=existing.Genres, MalId=existing.MalId }:null;
  p.Children.Add(Text("Поиск в Shikimori · русский / English / 日本語",12,Muted));
  var queryRow=new DockPanel { Margin=new Thickness(0,6,0,6) }; var find=new Button { Content="Найти", Margin=new Thickness(8,0,0,0) }; DockPanel.SetDock(find,Dock.Right); queryRow.Children.Add(find);
  var query=new TextBox { MaxLength=150, ToolTip="Название на русском, английском, японском или латиницей" }; queryRow.Children.Add(query); p.Children.Add(queryRow);
  var searchStatus=Text("Введите от двух символов — варианты появятся автоматически.",11,Muted); p.Children.Add(searchStatus);
  var results=new ListBox { MaxHeight=190, Visibility=Visibility.Collapsed, Background=ThemeBrush(Color.FromRgb(255,248,223)), BorderThickness=new Thickness(0), Margin=new Thickness(0,8,0,0), HorizontalContentAlignment=HorizontalAlignment.Stretch };
  ScrollViewer.SetHorizontalScrollBarVisibility(results,ScrollBarVisibility.Disabled); p.Children.Add(results);
  var selectionPanel=new DockPanel { Margin=new Thickness(0,12,0,0), Visibility=selected==null?Visibility.Collapsed:Visibility.Visible };
  var selectedCover=new Image { Width=55, Height=78, Stretch=Stretch.Uniform, Margin=new Thickness(0,0,12,0) }; DockPanel.SetDock(selectedCover,Dock.Left); selectionPanel.Children.Add(selectedCover);
  var selectedNames=Text(selected==null?"":selected.Names,12,Muted); selectionPanel.Children.Add(selectedNames); p.Children.Add(selectionPanel);
  if(selected!=null) LoadCover(selectedCover,selected.Id,selected.CoverUrl,lifetime.Token);
  p.Children.Add(new Border { Height=18 }); p.Children.Add(Text("Название аниме",13,Muted));
  var name=new TextBox { Text=existing==null?"":existing.Title, MaxLength=200, Margin=new Thickness(0,6,0,16) }; p.Children.Add(name);
  p.Children.Add(Text("Просмотрено серий",13,Muted)); var watched=new TextBox { Text=existing==null?"0":existing.Watched.ToString(), MaxLength=6, Margin=new Thickness(0,6,0,16) }; p.Children.Add(watched);
  p.Children.Add(Text("Всего серий · оставьте пустым, если неизвестно",12,Muted)); var total=new TextBox { Text=existing==null || existing.Total==0?"":existing.Total.ToString(), MaxLength=6, Margin=new Thickness(0,6,0,16) }; p.Children.Add(total);
  var suggestionTimer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) };
  Func<System.Threading.Tasks.Task> runSearch=async delegate {
   suggestionTimer.Stop();
   string term=query.Text.Trim(); if(term.Length<2) { searchStatus.Text="Введите хотя бы два символа."; return; }
   if(searchRequest!=null) { searchRequest.Cancel(); searchRequest.Dispose(); } searchRequest=System.Threading.CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
   var request=searchRequest; int version=++generation; find.IsEnabled=false; results.Items.Clear(); results.Visibility=Visibility.Collapsed; searchStatus.Text="Ищу в Shikimori…";
   try {
    var entries=await Catalog.Search(term,request.Token);
    if(closed || version!=generation) return;
    foreach(var entry in entries) {
     var row=new DockPanel { Margin=new Thickness(4,5,4,5) }; var poster=new Image { Width=38, Height=55, Stretch=Stretch.Uniform, Margin=new Thickness(0,0,10,0) }; DockPanel.SetDock(poster,Dock.Left); row.Children.Add(poster);
     var texts=new StackPanel(); texts.Children.Add(Text(entry.Title,14,Ink)); texts.Children.Add(Text(entry.Names,10,Muted)); texts.Children.Add(Text(entry.Info,10,Muted)); row.Children.Add(texts);
     results.Items.Add(new ListBoxItem { Tag=entry, Content=row, Padding=new Thickness(2), HorizontalContentAlignment=HorizontalAlignment.Stretch });
     LoadCover(poster,entry.Id,entry.CoverUrl,request.Token);
    }
    results.Visibility=entries.Count==0?Visibility.Collapsed:Visibility.Visible;
    searchStatus.Text=entries.Count==0?"Ничего не найдено. Попробуйте другое название или добавьте вручную.":"Выберите нужный сезон — данные заполнятся автоматически.";
   } catch(OperationCanceledException) { if(!closed && version==generation) searchStatus.Text="Поиск прерван или каталог не ответил. Повторите попытку."; }
   catch(Exception ex) { if(!closed && version==generation) searchStatus.Text=ex is InvalidOperationException?ex.Message:"Каталог недоступен. Повторите поиск или добавьте аниме вручную."; }
   finally { if(!closed && version==generation) find.IsEnabled=true; }
  };
  suggestionTimer.Tick += async delegate { suggestionTimer.Stop(); if(!closed) await runSearch(); };
  find.Click += async delegate { await runSearch(); };
  Action<CatalogEntry> chooseResult=delegate(CatalogEntry entry) {
   if(entry==null) return;
   suggestionTimer.Stop(); generation++; if(searchRequest!=null) searchRequest.Cancel(); find.IsEnabled=true;
   selected=entry; name.Text=selected.Title; total.Text=selected.Episodes>0?selected.Episodes.ToString():"";
   selectedNames.Text=selected.Names+"\n"+selected.Info; selectionPanel.Visibility=Visibility.Visible; selectedCover.Source=null;
   var chosen=selected; var img=selectedCover;
   LoadSelectedCover(img,chosen,lifetime.Token,()=>selected==chosen && !closed);
   searchStatus.Text="Выбрано: "+selected.Title; results.Visibility=Visibility.Collapsed; query.Focus();
  };
  query.PreviewKeyDown += async delegate(object s,KeyEventArgs ev) {
   if(ev.Key==Key.Enter) { ev.Handled=true; await runSearch(); }
   else if(ev.Key==Key.Down && results.Visibility==Visibility.Visible && results.Items.Count>0) { ev.Handled=true; results.SelectedIndex=0; var first=results.Items[0] as ListBoxItem; if(first!=null) first.Focus(); }
   else if(ev.Key==Key.Escape) { ev.Handled=true; suggestionTimer.Stop(); generation++; if(searchRequest!=null) searchRequest.Cancel(); find.IsEnabled=true; results.Visibility=Visibility.Collapsed; }
  };
  query.TextChanged += delegate { suggestionTimer.Stop(); generation++; if(searchRequest!=null) { try { searchRequest.Cancel(); } catch(ObjectDisposedException) {} } find.IsEnabled=true; results.Items.Clear(); results.Visibility=Visibility.Collapsed; if(query.Text.Trim().Length>=2) { searchStatus.Text="Подбираю варианты…"; suggestionTimer.Start(); } else searchStatus.Text="Введите от двух символов — варианты появятся автоматически."; };
  ListBoxItem pressedResult=null;
  results.PreviewMouseLeftButtonDown += delegate(object s,MouseButtonEventArgs ev) { pressedResult=ItemsControl.ContainerFromElement(results,ev.OriginalSource as DependencyObject) as ListBoxItem; };
  results.PreviewMouseLeftButtonUp += delegate(object s,MouseButtonEventArgs ev) { var released=ItemsControl.ContainerFromElement(results,ev.OriginalSource as DependencyObject) as ListBoxItem; var pressed=pressedResult; pressedResult=null; if(pressed!=null && pressed==released && results.Items.Contains(pressed)) { ev.Handled=true; chooseResult((CatalogEntry)pressed.Tag); } };
  results.PreviewKeyDown += delegate(object s,KeyEventArgs ev) { if(ev.Key==Key.Enter) { ev.Handled=true; var item=results.SelectedItem as ListBoxItem; if(item!=null) chooseResult((CatalogEntry)item.Tag); } else if(ev.Key==Key.Escape) { ev.Handled=true; results.Visibility=Visibility.Collapsed; query.Focus(); } };
  dialog.Closed += delegate { closed=true; suggestionTimer.Stop(); lifetime.Cancel(); if(searchRequest!=null) { searchRequest.Cancel(); searchRequest.Dispose(); } lifetime.Dispose(); };
  p.Children.Add(Text("Статус",12,Muted)); var statusChoice=new ComboBox { ItemsSource=MalImport.StatusLabels, SelectedIndex=existing==null?0:Array.IndexOf(MalImport.StatusKeys,existing.Status), Margin=new Thickness(0,5,0,14), Padding=new Thickness(6) }; p.Children.Add(statusChoice);
  Action syncCompleted=delegate { int count; bool known=statusChoice.SelectedIndex==1 && Int32.TryParse(total.Text,out count) && count>0; watched.IsReadOnly=known; if(known) watched.Text=total.Text.Trim(); };
  statusChoice.SelectionChanged+=delegate { syncCompleted(); }; total.TextChanged+=delegate { syncCompleted(); }; syncCompleted();
  p.Children.Add(Text("Моя оценка",12,Muted)); var rating=new ComboBox { ItemsSource=Ratings.Labels, SelectedIndex=existing==null?0:existing.Score, Padding=new Thickness(6), Margin=new Thickness(0,5,0,14) }; p.Children.Add(rating);
  var error=Text("",12,ThemeBrush("#FA8072")); p.Children.Add(error);
  var actions=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,12,0,0) };
  var save=Btn("Сохранить",delegate {
   int w,t=0; string title=name.Text.Trim();
   if(title.Length==0) { error.Text="Введите название аниме."; return; }
   if(!Int32.TryParse(watched.Text,out w) || w<0 || (!String.IsNullOrWhiteSpace(total.Text) && (!Int32.TryParse(total.Text,out t) || t<1)) || (t>0 && w>t)) { error.Text="Проверьте числа: просмотрено от 0 до общего количества."; return; }
   if(library.Items.Any(item=>item!=existing && ((selected!=null && item.CatalogId==selected.Id) || String.Equals(item.Title,title,StringComparison.CurrentCultureIgnoreCase)))) { error.Text="Такое аниме уже есть. Для другого сезона выберите другую карточку."; return; }
   var a=existing==null?new Anime():existing.Copy(); a.Title=title; a.Score=Math.Max(0,rating.SelectedIndex); a.SetProgress(w,t,MalImport.StatusKeys[Math.Max(0,statusChoice.SelectedIndex)]); a.Updated=DateTime.UtcNow;
   if(selected!=null) { a.CatalogId=selected.Id; if(selected.AiringStatus!="") { a.AiringStatus=selected.AiringStatus; a.EpisodesAired=selected.EpisodesAired; a.EpisodesChecked=DateTime.UtcNow; } a.English=selected.English; a.Japanese=selected.Japanese; a.Romaji=selected.Romaji; a.CoverUrl=selected.CoverUrl; a.Genres=selected.Genres; if(selected.MalId>0) a.MalId=selected.MalId; }
   if(storageBlocked){error.Text="Запись библиотеки отключена.";return;}
   try{service.SaveDraft(existing,a);undo=null;Refresh();dialog.Close();}catch(Exception ex){error.Text="Не удалось сохранить: "+ex.Message;}
  }); save.IsDefault=true; save.Background=Accent; save.Foreground=Brushes.Black; actions.Children.Add(save);
  var cancel=Btn("Отмена",delegate { dialog.Close(); }); cancel.IsCancel=true; actions.Children.Add(cancel);
  if(existing!=null) actions.Children.Add(Btn("Удалить",delegate {if(storageBlocked)return;try{int index=library.Items.IndexOf(existing);service.Remove(existing);undo=delegate{service.Insert(existing,index);};Refresh();dialog.Close();}catch(Exception ex){error.Text="Не удалось удалить: "+ex.Message;} }));
  p.Children.Add(actions); dialog.Loaded += delegate { if(existing==null) query.Focus(); else { name.Focus(); name.SelectAll(); } }; dialog.ShowDialog();
 }
 async void LoadCover(Image target,int id,string url,System.Threading.CancellationToken token) { try { var bitmap=await Catalog.Cover(id,url,token); if(!token.IsCancellationRequested) target.Source=bitmap; } catch(OperationCanceledException) {} catch { target.ToolTip="Обложка недоступна"; } }
 async void UpdateCompactCover(Anime anime) {
  int id=anime==null?0:anime.CatalogId;
  if(id==compactCoverId) return;
  if(compactCoverRequest!=null) { compactCoverRequest.Cancel(); compactCoverRequest.Dispose(); }
  compactCoverId=id; compactCover.Source=null; compactCover.Visibility=id>0 && widgetOptions.Style!=1?Visibility.Visible:Visibility.Collapsed;
  compactCoverRequest=new System.Threading.CancellationTokenSource(); var token=compactCoverRequest.Token; var target=compactCover;
  if(id<=0) return;
  try { var bitmap=await Catalog.Cover(id,anime.CoverUrl,token); if(!token.IsCancellationRequested && target==compactCover && id==compactCoverId) { target.Source=bitmap; if(bitmap==null) target.Visibility=Visibility.Collapsed; } }
  catch(OperationCanceledException) {} catch { if(!token.IsCancellationRequested) target.Visibility=Visibility.Collapsed; }
 }
 async void LoadSelectedCover(Image target,CatalogEntry entry,System.Threading.CancellationToken token,Func<bool> current) { try { var bitmap=await Catalog.Cover(entry.Id,entry.CoverUrl,token); if(!token.IsCancellationRequested && current()) { target.Source=bitmap; target.ToolTip=bitmap==null?"Обложка недоступна":null; } } catch(OperationCanceledException) {} catch { if(current()) target.ToolTip="Обложка недоступна"; } }
 static void SelfTest() {
  string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-"+Guid.NewGuid()); string path=Path.Combine(dir,"library.xml");
  try {
   LibraryView.Test();
   foreach(var size in new[]{new Size(260,124),new Size(280,340),new Size(320,100),new Size(260,270)}) {
    var area=new Rect(0,30,1920,1010);
    foreach(double x in new[]{-5000.0,0,1800,5000}) foreach(double y in new[]{-5000.0,30,1000,5000}) {
     var point=ClampWidgetPoint(x,y,size.Width,size.Height,area);
     if(!area.Contains(new Rect(point,size))) throw new Exception("Widget drag escaped working area");
    }
    var inside=ClampWidgetPoint(150,200,size.Width,size.Height,area); if(inside!=new Point(150,200)) throw new Exception("Widget moved inside bounds");
   }
   EpisodeUpdates.Test(); Ratings.Test(dir); SmoothScrollViewer.Test(); SequelSuggestions.Test(); CalendarNotes.Test(dir); CardEdits.Test(dir); EveningChoice.Test(dir); PersonalOrder.Test(dir); ArcData.Test(dir); ArcCatalog.Test(dir); WatchChoices.Test(dir);
   var recent=new Anime { Title="Recent completed",Status="completed",Updated=DateTime.UtcNow };
   var older=new Anime { Title="Older watching",Updated=recent.Updated.AddDays(-1) };
   var widgetTest=new Library(); widgetTest.Items.Add(older); widgetTest.Items.Add(recent);
   if(LatestAnime(widgetTest)!=recent) throw new Exception("Widget must show latest user change regardless of status");
   older.Updated=recent.Updated.AddMinutes(1); if(LatestAnime(widgetTest)!=older) throw new Exception("Widget follows user edits");
   ViewPreferences.Test(dir);AppearanceTests.Run(dir);SettingsBackupTest(dir);LibraryStorageTests.Run(dir);AutoStart.Test(dir);StartupDiagnostics.Test(dir);TrackerServiceTests.Run(dir).GetAwaiter().GetResult();
   var completion=new Anime { Watched=3,Total=12 };
   completion.SetProgress(3,12,"completed"); if(completion.Watched!=12 || completion.Status!="completed") throw new Exception("Completed fills known total");
   completion.SetProgress(5,0,"completed"); if(completion.Watched!=5) throw new Exception("Unknown total preserves progress");
   completion.SetProgress(4,12,"watching"); if(completion.Watched!=4 || completion.Status!="watching") throw new Exception("Other statuses preserve progress");
   var a=new Anime { Title="Тест & <аниме>", AiringStatus="ongoing", EpisodesAired=2, EpisodesChecked=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc), Total=2, CatalogId=20, English="Naruto", Japanese="ナルト", Romaji="Naruto", CoverUrl="https://shikimori.io/test.jpg" }; a.Increment(); if(a.Watched!=1 || a.Completed) throw new Exception("Increment"); a.Increment(); a.Increment(); if(a.Watched!=2 || !a.Completed) throw new Exception("Completion boundary");
   var b=new Anime { Title="Unknown" }; b.Increment(); if(b.Completed || b.Watched!=1) throw new Exception("Unknown total");
   var lib=new Library(); lib.Items.Add(a); lib.Items.Add(b); lib.Save(path); b.Increment(); lib.Save(path); var read=Library.Load(path);
   if(read.Items.Count!=2 || read.Items[0].Title!=a.Title || read.Items[1].Watched!=2 || !read.Items[0].Completed || !File.Exists(path+".bak")) throw new Exception("Persistence");
   if(read.Items[0].AiringStatus!="ongoing" || read.Items[0].EpisodesAired!=2 || read.Items[0].EpisodesChecked!=a.EpisodesChecked) throw new Exception("Episode metadata persistence");
   if(read.Items[0].CatalogId!=20 || read.Items[0].Japanese!="ナルト" || !Catalog.Matches(read.Items[0],"naruto") || !Catalog.Matches(read.Items[0],"ナルト")) throw new Exception("Multilingual persistence/search");
   var legacy=XDocument.Load(path); foreach(var element in legacy.Root.Elements("anime")) foreach(string key in new[]{"catalogId","english","japanese","romaji","coverUrl"}) element.Attribute(key).Remove(); legacy.Save(path);
   var migrated=Library.Load(path); if(migrated.Items[0].CatalogId!=0 || migrated.Items[0].Watched!=2) throw new Exception("Legacy migration");
   var parsed=Catalog.Parse("{\"data\":{\"animes\":[{\"id\":\"1\",\"name\":\"Test\",\"russian\":null,\"episodes\":0,\"poster\":null}]}}"); if(parsed.Count!=1 || parsed[0].Title!="Test" || parsed[0].Episodes!=0 || parsed[0].CoverUrl!="") throw new Exception("Nullable catalog fields");
   bool rejected=false; try { Catalog.Parse("{\"errors\":[{\"message\":\"test\"}]}"); } catch(InvalidDataException) { rejected=true; } if(!rejected) throw new Exception("GraphQL errors");
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"PASS: automatic episode refresh preserves all 5 user statuses/progress/rewatch, exact IDs, unknown totals, refresh interval, final release; multi-genre ALL/ANY, sorting, filter combinations, view settings save/reload/update/backup/defaults, completed fills known total, unknown total preserves progress, increment, XML round-trip, backup, multilingual search, legacy migration, catalog parsing.");
  } catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"FAIL: "+ex); Environment.ExitCode=1; }
  finally { try { if(File.Exists(dir+".tmp")) File.Delete(dir+".tmp"); if(Directory.Exists(dir)) Directory.Delete(dir,true); } catch(Exception ex) { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"\nFAIL: test cleanup: "+ex.Message); Environment.ExitCode=1; } }
 }
 static void CatalogTest() {
  var report=new List<string>();
  try {
   foreach(string query in new[]{"Наруто","Naruto","ナルト"}) {
    var entries=Catalog.Search(query,System.Threading.CancellationToken.None).GetAwaiter().GetResult(); var entry=entries.FirstOrDefault(x=>x.Id==20);
    if(entry==null || entry.Russian!="Наруто" || entry.English!="Naruto" || entry.Japanese!="ナルト" || entry.Episodes!=220) throw new Exception("Unexpected catalog result for "+query);
    report.Add("PASS "+query+": "+entry.Id+", "+entry.Russian+", "+entry.English+", "+entry.Japanese+", episodes="+entry.Episodes);
    if(query=="Наруто") { var cover=Catalog.Cover(entry.Id,entry.CoverUrl,System.Threading.CancellationToken.None).GetAwaiter().GetResult(); if(cover==null || !File.Exists(Catalog.CoverPath(entry.Id))) throw new Exception("Cover unavailable"); report.Add("PASS cover decoded and cached: "+cover.PixelWidth+"x"+cover.PixelHeight); }
   }
  } catch(Exception ex) { report.Add("FAIL "+ex); Environment.ExitCode=1; }
  File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog-test.txt"),report);
 }
}
}
