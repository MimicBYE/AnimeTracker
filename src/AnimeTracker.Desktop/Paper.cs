using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AnimeTracker.Desktop;

internal static class Paper
{
    public static readonly IBrush Ink=Brush.Parse("#47361D"),Muted=Brush.Parse("#896F3E"),Accent=Brush.Parse("#B9862C"),Sheet=Brush.Parse("#FFF2BA");
    public static TextBlock Text(string text,double size=13,IBrush? brush=null)=>new(){Text=text,FontSize=size,Foreground=brush??Ink,TextWrapping=TextWrapping.Wrap};
    public static StackPanel Stack(params Control[] children){var panel=new StackPanel{Spacing=9};foreach(var child in children)panel.Children.Add(child);return panel;}
    public static WrapPanel Actions(params Control[] children){var panel=new WrapPanel{Orientation=Orientation.Horizontal};foreach(var child in children){child.Margin=new Thickness(0,0,7,7);panel.Children.Add(child);}return panel;}
    public static Button Button(string title,Action action){var button=new Button{Content=title};button.Click+=(_,_)=>action();return button;}
    public static Button AsyncButton(string title,Func<Task> action){var button=new Button{Content=title};button.Click+=async(_,_)=>{button.IsEnabled=false;try{await action();}catch(Exception e){if(TopLevel.GetTopLevel(button) is Window owner)await Message(owner,e.Message,"Ошибка");}finally{button.IsEnabled=true;}};return button;}
    public static Border Card(Control content)=>new(){Background=Sheet,CornerRadius=new CornerRadius(10),Padding=new Thickness(18),Margin=new Thickness(0,0,0,12),Child=content};
    public static ComboBox Choice(IEnumerable<string> items,int index=0,double width=180)=>new(){ItemsSource=items.ToArray(),SelectedIndex=index,MinWidth=width,HorizontalAlignment=HorizontalAlignment.Stretch};
    public static StackPanel Field(string title,Control input)=>Stack(Text(title,11,Muted),input);
    public static PaperScroll Scroll(Control content)=>new(){Content=content,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    public static Window Dialog(Window owner,string title,Control content,double width=570,double height=430)=>new(){Title=title,Icon=owner.Icon,Width=width,Height=height,MinWidth=400,MinHeight=280,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new Border{Padding=new Thickness(22),Child=content},ShowInTaskbar=false};
    public static async Task<bool> Message(Window owner,string message,string title="Anime Tracker",bool confirm=false)
    {
        var dialog=Dialog(owner,title,new TextBlock());var panel=new DockPanel();var actions=Actions(Button(confirm?"Продолжить":"Закрыть",()=>dialog.Close(true)));if(confirm)actions.Children.Add(Button("Отмена",()=>dialog.Close(false)));DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);panel.Children.Add(Scroll(Text(message,14)));dialog.Content=new Border{Padding=new Thickness(24),Child=panel};return await dialog.ShowDialog<bool>(owner);
    }
    public static async Task<string?> OpenFile(Window owner,string title,params string[] patterns)
    {
        var files=await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=title,AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("Файлы"){Patterns=patterns}]});return files.FirstOrDefault()?.TryGetLocalPath();
    }
    public static async Task<string?> SaveFile(Window owner,string title,string name,string extension)
    {
        var file=await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title=title,SuggestedFileName=name,DefaultExtension=extension,ShowOverwritePrompt=true,FileTypeChoices=[new FilePickerFileType(extension.ToUpperInvariant()){Patterns=["*."+extension]}]});return file?.TryGetLocalPath();
    }
}
public sealed class PaperListBox:ListBox
{
    protected override Type StyleKeyOverride=>typeof(ListBox);
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    ScrollViewer? scroll;
    double target;
    public PaperListBox(){
        // Handle the wheel before the internal viewer consumes it. Keep its bounded viewport.
        AddHandler(PointerWheelChangedEvent,(_,e)=>{
            if(e.Handled||scroll==null)return;double max=Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height);if(max<=0)return;
            if(!timer.IsEnabled)target=scroll.Offset.Y;target=Math.Clamp(target-e.Delta.Y*55,0,max);e.Handled=true;timer.Start();
        },Avalonia.Interactivity.RoutingStrategies.Tunnel);
        timer.Tick+=(_,_)=>{if(scroll==null){timer.Stop();return;}target=Math.Clamp(target,0,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height));double delta=target-scroll.Offset.Y;if(Math.Abs(delta)<0.5){scroll.Offset=new Vector(scroll.Offset.X,target);timer.Stop();}else scroll.Offset=new Vector(scroll.Offset.X,scroll.Offset.Y+delta*0.28);};
        DetachedFromVisualTree+=(_,_)=>timer.Stop();
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e){timer.Stop();base.OnApplyTemplate(e);scroll=e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change){base.OnPropertyChanged(change);if(change.Property==ItemsSourceProperty)timer.Stop();}
}
public sealed class PaperScroll:ScrollViewer
{
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    double target;
    public PaperScroll(){timer.Tick+=(_,_)=>{double delta=target-Offset.Y;if(Math.Abs(delta)<0.5){Offset=new Vector(Offset.X,target);timer.Stop();}else Offset=new Vector(Offset.X,Offset.Y+delta*0.28);};DetachedFromVisualTree+=(_,_)=>timer.Stop();}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if(e.Handled)return;double max=Math.Max(0,Extent.Height-Viewport.Height);if(max<=0){base.OnPointerWheelChanged(e);return;}
        if(!timer.IsEnabled)target=Offset.Y;target=Math.Clamp(target-e.Delta.Y*55,0,max);e.Handled=true;timer.Start();
    }
}
public sealed class Covers
{
    readonly AppStore store;
    readonly Dictionary<int,Bitmap> images=[];
    readonly CoverSource source;
    readonly SemaphoreSlim gate=new(3);
    public Covers(AppStore store){this.store=store;source=new CoverSource(async(id,token)=>(await AnimeTracker.Catalog.ByIds(new[]{id},token)).FirstOrDefault(e=>e.Id==id)?.CoverUrl??"");}
    public Image Image(AnimeTracker.Anime anime,CancellationToken token,double width=64,double height=90){var image=new Image{Width=width,Height=height,Stretch=Stretch.Uniform};_=Load(image,anime.CatalogId,anime.CoverUrl,token);return image;}
    public Task Fill(Image image,AnimeTracker.Anime anime,CancellationToken token)=>Load(image,anime.CatalogId,anime.CoverUrl,token);
    async Task Load(Image image,int id,string url,CancellationToken token)
    {
        if(id<=0)return;try{if(images.TryGetValue(id,out var known)){image.Source=known;return;}await gate.WaitAsync(token);try{
            if(images.TryGetValue(id,out known)){image.Source=known;return;}
            string path=store.FilePath(Path.Combine("covers",id+".png"));byte[]? bytes=null;
            if(File.Exists(path))try{bytes=await File.ReadAllBytesAsync(path,token);using var file=new MemoryStream(bytes);known=Bitmap.DecodeToWidth(file,240);}catch{bytes=null;known=null;}
            if(known==null){bytes=await source.Fetch(id,url,token);using var input=new MemoryStream(bytes);known=Bitmap.DecodeToWidth(input,480);Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllBytesAsync(temporary,bytes,token);File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            token.ThrowIfCancellationRequested();images[id]=known;image.Source=known;
        }finally{gate.Release();}}catch(OperationCanceledException){}catch{}
    }
}
