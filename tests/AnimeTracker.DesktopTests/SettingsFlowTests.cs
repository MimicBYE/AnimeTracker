using AnimeTracker;
using AnimeTracker.Desktop;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static Program;

internal static class SettingsFlowTests
{
    static Button Button(Window window,string title)=>window.GetLogicalDescendants().OfType<Button>().Distinct().Single(b=>b.Content as string==title);
    static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    static void OnScreen(Control control,Window window,string message)
    {
        var point=control.TranslatePoint(default,window);
        Check(point.HasValue&&point.Value.X>=-1&&point.Value.Y>=-1&&point.Value.X+control.Bounds.Width<=window.ClientSize.Width+1&&point.Value.Y+control.Bounds.Height<=window.ClientSize.Height+1,message);
    }
    static void Capture(Window window,string? directory,string name,double scale)
    {
        if(directory==null)return;Directory.CreateDirectory(directory);var content=(Control)window.Content!;
        if(content is Panel panel)panel.Background=window.Background;
        if(content is Border border)border.Background=window.Background;
        using var image=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Math.Ceiling(content.Bounds.Width*scale),(int)Math.Ceiling(content.Bounds.Height*scale)),new Vector(96*scale,96*scale));
        image.Render(content);image.Save(Path.Combine(directory,name+".png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
    public static object Run(string root,string? screenshots)
    {
        var store=new AppStore(Path.Combine(root,"settings"));var existing=store.AddManual("Существующая запись с длинным названием — проверка переноса строк");existing.English="A long alternative title for the library layout";existing.Japanese="長いアニメタイトルの表示を確認する";existing.Genres="Приключения|Драма|Фэнтези";existing.Total=25;store.Library.Save(store.LibraryPath);store.SetWidget("",0,true);
        var widgetPath=store.FilePath("widget-settings.xml");var originalWidget=File.ReadAllText(widgetPath);var originalLibrary=File.ReadAllText(store.LibraryPath);
        var fixture=Path.Combine(root,"settings-import.xml");File.WriteAllText(fixture,"<myanimelist><anime><series_animedb_id>20</series_animedb_id><series_title>Import fixture</series_title><series_episodes>12</series_episodes><my_watched_episodes>3</my_watched_episodes><my_status>Watching</my_status><my_score>7</my_score></anime></myanimelist>");
        var pending=new TaskCompletionSource<string?>();Window? pickerOwner=null;int calls=0;
        var window=new MainWindow(store,owner=>{pickerOwner=owner;calls++;return calls==1?pending.Task:Task.FromResult<string?>(fixture);}){Opacity=0,ShowInTaskbar=false,ShowActivated=false};window.Show();Dispatcher.UIThread.RunJobs();
        var settingsTask=window.Settings();var dialog=window.OwnedWindows.Single();dialog.Opacity=0;dialog.ShowInTaskbar=false;Dispatcher.UIThread.RunJobs();dialog.UpdateLayout();
        try
        {
            var controls=dialog.GetLogicalDescendants().ToArray();var style=controls.OfType<ComboBox>().Single(c=>Avalonia.Automation.AutomationProperties.GetName(c)=="Оформление виджета");var enabled=controls.OfType<CheckBox>().Single(c=>c.Content as string=="Показывать виджет");var startup=controls.OfType<CheckBox>().Single(c=>c.Content as string=="Запускать при входе в систему");bool? initialStartup=startup.IsChecked;
            var theme=controls.OfType<ComboBox>().Single(c=>Avalonia.Automation.AutomationProperties.GetName(c)=="Тема приложения");theme.SelectedIndex=1;
            style.SelectedIndex=2;enabled.IsChecked=false;startup.IsChecked=initialStartup!=true;
            var import=Button(dialog,"Импорт XML из MyAnimeList");
            void DraftIntact()
            {
                Check(dialog.IsVisible&&!settingsTask.IsCompleted,"Import dismissed Settings");
                Check(theme.SelectedIndex==1&&!store.Appearance.Dark&&style.SelectedIndex==2&&enabled.IsChecked==false&&startup.IsChecked==(initialStartup!=true),"Import lost the settings draft");
                Check(store.Widget.Style==0&&store.Widget.Enabled&&File.ReadAllText(widgetPath)==originalWidget,"Import applied widget settings implicitly");
                var startupProperty=typeof(MainWindow).Assembly.GetType("AnimeTracker.Desktop.MacIntegration")!.GetProperty("AutoStartEnabled")!;
                Check((bool)startupProperty.GetValue(null)! == (initialStartup==true),"Import changed OS autostart");
            }
            Click(import);Check(calls==1&&ReferenceEquals(pickerOwner,dialog),"File picker owner is not Settings");DraftIntact();Check(!import.IsEnabled,"Import can be started twice");
            pending.SetResult(null);PumpUntil(()=>import.IsEnabled);DraftIntact();Check(store.ModalDepth==1&&File.ReadAllText(store.LibraryPath)==originalLibrary,"Cancelling file picker changed library or modal depth");

            Click(import);var preview=dialog.OwnedWindows.Single();preview.Opacity=0;Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(preview.Owner,dialog)&&store.ModalDepth==2,"Preview ownership/modal depth incorrect");DraftIntact();Click(Button(preview,"Отмена"));PumpUntil(()=>import.IsEnabled);DraftIntact();Check(store.ModalDepth==1&&File.ReadAllText(store.LibraryPath)==originalLibrary,"Cancelling preview changed library");

            Click(import);preview=dialog.OwnedWindows.Single();preview.Opacity=0;Dispatcher.UIThread.RunJobs();Click(Button(preview,"Импортировать"));PumpUntil(()=>import.IsEnabled);DraftIntact();
            var imported=store.Library.Items.Single(a=>a.MalId==20);Check(store.Library.Items.Count==2&&imported.Status=="watching"&&imported.Watched==3&&imported.Score==7&&store.ModalDepth==1,"Explicit import did not preserve MAL fields");

            var importedLibrary=File.ReadAllText(store.LibraryPath);File.WriteAllText(fixture,"<wrong/>");Click(import);var error=dialog.OwnedWindows.Single();error.Opacity=0;Dispatcher.UIThread.RunJobs();Check(error.Title=="Ошибка"&&ReferenceEquals(error.Owner,dialog),"Import error is not owned by Settings");Click(Button(error,"Закрыть"));PumpUntil(()=>import.IsEnabled);DraftIntact();Check(File.ReadAllText(store.LibraryPath)==importedLibrary,"Invalid import changed library");

            var scales=new List<object>();
            foreach(double scale in new[]{1.5,2.0})
            {
                // Logical viewports corresponding to a 1920x1080 surface at 150/200%; no OS DPI changes.
                window.Width=1920/scale;window.Height=1080/scale;dialog.Width=640;dialog.Height=500;Dispatcher.UIThread.RunJobs();window.UpdateLayout();dialog.UpdateLayout();
                OnScreen(Button(window,"＋ Добавить"),window,"Library add button clipped at constrained viewport");
                OnScreen(window.GetLogicalDescendants().OfType<TextBox>().First(),window,"Library search clipped");
                OnScreen(Button(dialog,"Применить"),dialog,"Settings Apply clipped");OnScreen(Button(dialog,"Закрыть"),dialog,"Settings Close clipped");
                var scroll=dialog.GetVisualDescendants().OfType<ScrollViewer>().Single(v=>v is PaperScroll);Check(scroll.Viewport.Height>0&&scroll.Extent.Height>scroll.Viewport.Height,"Settings has no working scroll template/viewport");
                scroll.Offset=default;var wheel=new PointerWheelEventArgs(scroll,new Pointer(2,PointerType.Mouse,true),dialog,new Point(50,50),0,new PointerPointProperties(),KeyModifiers.None,new Vector(0,-1));scroll.RaiseEvent(wheel);Check(wheel.Handled&&scroll.Offset.Y==0,"Settings wheel is not smoothly handled");PumpUntil(()=>scroll.Offset.Y>0);Check(scroll.Offset.Y<55,"Settings wheel jumped");PumpUntil(()=>Math.Abs(scroll.Offset.Y-55)<0.001);
                scroll.Offset=new Vector(0,scroll.Extent.Height-scroll.Viewport.Height);Dispatcher.UIThread.RunJobs();dialog.UpdateLayout();Check(scroll.Offset.Y>0,"Settings did not scroll");OnScreen(import,dialog,"Import unreachable after scrolling");OnScreen(Button(dialog,"Резервная копия ZIP"),dialog,"Backup unreachable after scrolling");
                Capture(window,screenshots,"library-scale-"+(int)(scale*100),scale);Capture(dialog,screenshots,"settings-scale-"+(int)(scale*100),scale);
                scales.Add(new{scale,libraryClient=window.ClientSize.ToString(),settingsClient=dialog.ClientSize.ToString(),scrollOffset=scroll.Offset.Y,scrollExtent=scroll.Extent.Height,scrollViewport=scroll.Viewport.Height,checks="search/add in bounds, fixed footer in bounds, import reachable by scroll; logical-viewport and bitmap DPI emulation, not native OS scaling"});
            }
            Click(Button(dialog,"Закрыть"));PumpUntil(()=>settingsTask.IsCompleted);Check(settingsTask.IsCompletedSuccessfully&&store.ModalDepth==0,"Settings modal depth leaked");Check(File.ReadAllText(widgetPath)==originalWidget&&!store.Appearance.Dark,"Closing Settings applied draft");
            return new{checks="file cancellation, preview cancellation, explicit import, invalid import, owned dialogs, draft retained, widget/autostart not applied, close discards draft",scales};
        }
        finally{foreach(var child in dialog.OwnedWindows.ToArray())child.Close();dialog.Close();typeof(App).GetProperty("Quitting")!.SetValue(App.CurrentApp,true);window.Close();}
    }
}
