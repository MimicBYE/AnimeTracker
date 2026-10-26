using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using AnimeTracker;
using AnimeTracker.Desktop;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

internal static class Program {
 static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void PumpUntil(Func<bool> done){using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));var poll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};poll.Tick+=(_,_)=>{if(done())stop.Cancel();};poll.Start();try{Dispatcher.UIThread.MainLoop(stop.Token);}finally{poll.Stop();}Check(done(),"UI timeout");}
 [STAThread] static int Main(string[] args) {
  string root=Path.Combine(Path.GetTempPath(),"anime-tracker-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try {
   AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
   var results=new List<object>();
   foreach(int size in new[]{1000,5000}) {
    var path=Path.Combine(root,size.ToString());var store=new AppStore(path);
    store.Library.Items.AddRange(Enumerable.Range(1,size).Select(i=>new Anime{Title="Тайтл "+i.ToString("D5"),English="Title "+i,Total=12,Status="planned"}));
    var watch=Stopwatch.StartNew();var window=new MainWindow(store);watch.Stop();double initial=watch.Elapsed.TotalMilliseconds;
    var search=Field<TextBox>(window,"search");var list=Field<ListBox>(window,"libraryRows");
    var timings=new List<double>();int immediateSaves=0;
    foreach(string text in new[]{"Т","Та","Тай","Тайт","Тайтл"}) {
     watch.Restart();search.Text=text;Dispatcher.UIThread.RunJobs();watch.Stop();timings.Add(watch.Elapsed.TotalMilliseconds);
     if(ViewPreferences.Load(store.FilePath("view-settings.xml")).Search==text)immediateSaves++;
    }
    Check(immediateSaves==0,"Typing wrote preferences synchronously");
    var viewPath=store.FilePath("view-settings.xml");
    PumpUntil(()=>File.Exists(viewPath));Check(ViewPreferences.Load(viewPath).Search=="Тайтл"&&!File.Exists(viewPath+".bak"),"Burst was not coalesced into one save");
    window.FlushPendingFilters();Check(!File.Exists(viewPath+".bak"),"Unchanged filters were written again");
    // The transparent native window exercises a finite viewport without covering the user's desktop.
    window.Opacity=0;window.ShowInTaskbar=false;window.ShowActivated=false;window.Show();Dispatcher.UIThread.RunJobs();window.UpdateLayout();
    int realized=list.GetRealizedContainers().Count();Check(realized>0&&realized<100,"Library list is not virtualized in a bounded viewport");
    if(args.Length==2&&args[0]=="--screenshots") {Directory.CreateDirectory(args[1]);((Panel)window.Content!).Background=window.Background;using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1040,800),new Vector(96,96));bitmap.Render((Control)window.Content!);bitmap.Save(Path.Combine(args[1],"library-"+size+".png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);}
    list.ScrollIntoView(size-1);Dispatcher.UIThread.RunJobs();window.UpdateLayout();Check(list.GetRealizedContainers().Count()<100,"Scrolling materialized the full library");
    search.Text="00001";Dispatcher.UIThread.RunJobs();window.FlushPendingFilters();Check(list.Items.Count==1&&((Anime)list.Items[0]!).Title=="Тайтл 00001","Search results incorrect");
    var status=Field<ComboBox>(window,"status");status.SelectedIndex=1;Dispatcher.UIThread.RunJobs();Check(list.Items.Count==0,"Status filter was not applied immediately");
    status.SelectedIndex=3;Dispatcher.UIThread.RunJobs();Check(list.Items.Count==1,"Planned status did not restore result");
    search.Text="Последний запрос"; // Close before queued TextChanged/debounce ticks.
    typeof(App).GetProperty("Quitting")!.SetValue(App.CurrentApp,true);window.Close();
    Check(ViewPreferences.Load(viewPath).Search=="Последний запрос","Fast close lost latest search");
    results.Add(new{entries=size,initialControlConstructionMs=initial,inputHandlersMs=timings,immediateSaves,realizedContainers=realized,checks="debounce, single write, finite viewport/scroll, search/status, flush on close"});
   }
   Console.WriteLine(JsonSerializer.Serialize(new{os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,framework=Environment.Version.ToString(),mode="input timing on unshown Avalonia controls, then transparent native-window checks; synthetic data, no covers/network; not frame-time measurements",results},new JsonSerializerOptions{WriteIndented=true}));
   return 0;
  } catch(Exception e){Console.Error.WriteLine(e);return 1;}
  finally{Directory.Delete(root,true);}
 }
}
