using System.Xml.Linq;
using AnimeTracker;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AnimeTracker.Desktop;

public sealed class WidgetWindow:Window
{
    readonly AppStore store;
    readonly Covers covers;
    readonly CancellationTokenSource lifetime=new();
    readonly DispatcherTimer clicks=new(){Interval=TimeSpan.FromMilliseconds(350)};
    readonly Border surface=new(){Background=Brush.Parse("#F5E5A6"),BorderBrush=Brush.Parse("#CFB06F"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6,6,16,6),Padding=new Thickness(13,10)};
    bool expanded,pressed,dragged;
    PixelPoint initialPointer,initialPosition;
    public WidgetWindow(AppStore store)
    {
        this.store=store;covers=new Covers(store);Title="Anime Tracker · виджет";WindowDecorations=Avalonia.Controls.WindowDecorations.None;CanResize=false;ShowInTaskbar=false;ShowActivated=false;Background=Brush.Parse("#F5E5A6");Content=surface;
        store.Changed+=ApplyPreference;Render();clicks.Tick+=(_,_)=>{clicks.Stop();expanded=!expanded;Render();};
        Opened+=(_,_)=>{RestorePosition();MacIntegration.AttachToDesktop(this);};
        Closing+=(_,e)=>{if(!App.CurrentApp.Quitting){e.Cancel=true;Hide();}};
        Closed+=(_,_)=>{clicks.Stop();store.Changed-=ApplyPreference;lifetime.Cancel();};
        surface.PointerPressed+=(_,e)=>{
            if(e.Source is Control control&&(control is Button||control.FindAncestorOfType<Button>()!=null||control.FindAncestorOfType<ScrollViewer>()!=null))return;
            if(e.ClickCount==2){clicks.Stop();pressed=false;e.Pointer.Capture(null);App.CurrentApp.OpenLibrary();return;}
            if(!e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed)return;pressed=true;dragged=false;initialPointer=this.PointToScreen(e.GetPosition(this));initialPosition=Position;e.Pointer.Capture(surface);e.Handled=true;
        };
        surface.PointerMoved+=(_,e)=>{if(!pressed)return;var point=this.PointToScreen(e.GetPosition(this));int x=point.X-initialPointer.X,y=point.Y-initialPointer.Y;if(Math.Abs(x)>4||Math.Abs(y)>4){dragged=true;clicks.Stop();}if(dragged){Position=new PixelPoint(initialPosition.X+x,initialPosition.Y+y);ClampPosition();}};
        surface.PointerReleased+=(_,e)=>{if(!pressed)return;pressed=false;e.Pointer.Capture(null);if(dragged)SavePosition();else{clicks.Stop();clicks.Start();}e.Handled=true;};
        surface.PointerCaptureLost+=(_,_)=>pressed=false;
    }
    void ApplyPreference(){Render();if(store.Widget.Enabled){if(!IsVisible){Show();MacIntegration.AttachToDesktop(this);}}else Hide();}
    void ClampPosition(){var screen=Screens.ScreenFromWindow(this)??Screens.Primary;if(screen==null)return;var area=screen.WorkingArea;int width=(int)Math.Ceiling(Width*RenderScaling),height=(int)Math.Ceiling(Height*RenderScaling);Position=new PixelPoint(Math.Clamp(Position.X,area.X,Math.Max(area.X,area.Right-width)),Math.Clamp(Position.Y,area.Y,Math.Max(area.Y,area.Bottom-height)));}
    void RestorePosition(){try{var x=XElement.Load(store.FilePath("mac-widget-position.xml"));Position=new PixelPoint((int)x.Attribute("x")!,(int)x.Attribute("y")!);}catch{var screen=Screens.Primary;if(screen!=null)Position=new PixelPoint(screen.WorkingArea.Right-(int)(Width*RenderScaling)-30,screen.WorkingArea.Y+70);}ClampPosition();}
    void SavePosition(){try{new XElement("position",new XAttribute("x",Position.X),new XAttribute("y",Position.Y)).Save(store.FilePath("mac-widget-position.xml.tmp"));File.Move(store.FilePath("mac-widget-position.xml.tmp"),store.FilePath("mac-widget-position.xml"),true);}catch{}}
    async Task Safe(Action action){try{action();}catch(Exception e){App.CurrentApp.OpenLibrary();await Paper.Message(App.CurrentApp.LibraryWindow,e.Message);}}
    void Render()
    {
        int style=store.Widget.Style;var size=WidgetOptions.Size(style);Width=expanded?280:size.Width;Height=expanded?340:size.Height;
        var panel=new StackPanel{Spacing=7};surface.Child=panel;var heading=new DockPanel();
        var add=Paper.AsyncButton("＋",()=>App.CurrentApp.LibraryWindow.AddFromWidget());add.Padding=new Thickness(4,0);DockPanel.SetDock(add,Dock.Right);heading.Children.Add(add);
        var settings=Paper.AsyncButton("⚙",async()=>{App.CurrentApp.OpenLibrary();await App.CurrentApp.LibraryWindow.Settings();});settings.Padding=new Thickness(4,0);DockPanel.SetDock(settings,Dock.Right);heading.Children.Add(settings);heading.Children.Add(Paper.Text("Anime Tracker",12,Paper.Muted));panel.Children.Add(heading);
        var anime=WidgetOptions.Selected(store.Library,store.Widget.Pinned);if(anime==null){panel.Children.Add(Paper.Text("Следующая история…",18));panel.Children.Add(Paper.Text("＋ Добавить первое аниме",11,Paper.Muted));ClampPosition();return;}
        var summary=new DockPanel();if(style!=1){var image=covers.Image(anime,lifetime.Token,style==2&&!expanded?230:40,style==2&&!expanded?125:58);image.Margin=new Thickness(0,0,8,0);DockPanel.SetDock(image,style==2&&!expanded?Dock.Top:Dock.Left);summary.Children.Add(image);}
        var plus=Paper.AsyncButton("+1",()=>Safe(()=>store.Increment(anime)));plus.IsEnabled=!store.ReadOnly&&!anime.Completed&&(anime.Total<=0||anime.Watched<anime.Total);plus.FontSize=18;plus.Padding=new Thickness(8,5);DockPanel.SetDock(plus,Dock.Right);summary.Children.Add(plus);
        var details=Paper.Stack(new TextBlock{Text=anime.Title,FontSize=15,TextWrapping=TextWrapping.Wrap,MaxHeight=style==1&&!expanded?22:42,Foreground=Paper.Ink},Paper.Text(anime.Watched+" / "+(anime.Total>0?anime.Total.ToString():"?")+" · "+MalImport.StatusLabel(anime.Status),11,Paper.Muted));details.Spacing=3;summary.Children.Add(details);panel.Children.Add(summary);
        if(style==2&&!expanded)panel.Children.Add(new ProgressBar{Minimum=0,Maximum=Math.Max(1,anime.Total>0?anime.Total:anime.Watched),Value=anime.Watched,Height=4,Foreground=Paper.Accent});
        if(expanded){var rows=new StackPanel{Spacing=5};foreach(var item in store.Library.Items.OrderByDescending(a=>a.Updated).Take(10))rows.Children.Add(Paper.AsyncButton(item.Title+" · "+item.Watched+" / "+(item.Total>0?item.Total.ToString():"?"),()=>Safe(()=>store.SetWidget(item.Id,store.Widget.Style))));if(anime.Completed){var next=store.Choices.Active(store.Library).FirstOrDefault();if(next!=null)rows.Children.Insert(0,Paper.AsyncButton("Дальше из очереди: "+next.Title,()=>Safe(()=>store.SetWidget(next.Id,store.Widget.Style))));}var scroll=Paper.Scroll(rows);scroll.Height=155;panel.Children.Add(scroll);}
        if(expanded)panel.Children.Add(Paper.Text("Клик — свернуть · двойной клик — библиотека",10,Paper.Muted));ClampPosition();
    }
}
