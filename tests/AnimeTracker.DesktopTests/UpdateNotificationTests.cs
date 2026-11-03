using System.Reflection;
using AnimeTracker;
using AnimeTracker.Desktop;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using static Program;

internal static class UpdateNotificationTests
{
    static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static Window? Notice(App app)=>(Window?)typeof(App).GetField("updateNotice",Hidden)!.GetValue(app);
    static void Poll(App app)=>typeof(App).GetMethod("TryShowUpdateNotice",Hidden)!.Invoke(app,null);
    static void Click(Window window,string label)=>window.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    public static object Run(string root)
    {
        using var fixture=new UpdateNoticeTests.Fixture(Path.Combine(root,"notification-ui"),"linux-x64");var updates=fixture.Updates;fixture.Check().GetAwaiter().GetResult();
        var store=new AppStore(fixture.Root);store.AddManual("Синтетическая библиотека");var original=File.ReadAllBytes(store.LibraryPath);
        var app=App.CurrentApp;var oldUpdates=app.Updates;var oldStore=app.Store;var oldWindow=app.LibraryWindow;
        typeof(App).GetProperty("Quitting")!.SetValue(app,false);
        var window=new MainWindow(store,systemAppearance:new SystemAppearanceTests.Source()){Opacity=0,ShowInTaskbar=false,ShowActivated=false};window.Show();Dispatcher.UIThread.RunJobs();
        typeof(App).GetProperty("Updates")!.SetValue(app,updates);typeof(App).GetProperty("Store")!.SetValue(app,store);typeof(App).GetProperty("LibraryWindow")!.SetValue(app,window);
        try
        {
            store.ModalDepth=1;Poll(app);Check(Notice(app)==null&&updates.NoticeDue,"Notification interrupted a modal or consumed pending release");store.ModalDepth=0;Poll(app);
            var notice=Notice(app)!;Check(notice!=null&&window.IsEnabled&&!notice.ShowActivated&&!notice.ShowInTaskbar,"Notification is modal/activating or missing");Poll(app);Check(ReferenceEquals(Notice(app),notice),"Duplicate notification");
            Click(notice!,"Напомнить позже");Dispatcher.UIThread.RunJobs();Check(Notice(app)==null&&!updates.NoticeDue,"Later did not dismiss/snooze");
            Check(UpdateOptions.Load(store.Files.PathFor("update-settings.xml"),false).RemindAfter==fixture.UtcNow.AddHours(6),"Later was not persisted");
            fixture.UtcNow=fixture.UtcNow.AddHours(6);Poll(app);notice=Notice(app)!;Check(notice!=null,"Expired reminder was not shown");notice!.Close();Dispatcher.UIThread.RunJobs();Check(Notice(app)==null&&!updates.NoticeDue,"Window close did not mean Later");
            fixture.Version="1.1.2";fixture.Check().GetAwaiter().GetResult();Poll(app);notice=Notice(app)!;Check(notice!=null,"New version hidden by older snooze");
            Click(notice!,"Обновить");PumpUntil(()=>window.OwnedWindows.Any());var progress=window.OwnedWindows.Single();PumpUntil(()=>progress.OwnedWindows.Any());var confirmation=progress.OwnedWindows.Single();
            Check(confirmation.Title=="Установить 1.1.2?"&&store.ModalDepth==1,"Update did not go straight to confirmation/progress");Click(confirmation,"Отмена");Dispatcher.UIThread.RunJobs();
            Check(fixture.Downloads==0&&File.ReadAllBytes(store.LibraryPath).SequenceEqual(original),"Cancel downloaded software or changed library");Click(progress,"Закрыть");PumpUntil(()=>store.ModalDepth==0);
            return new{checks="nonmodal/no activation; no duplicates; modal deferral; Later persisted; X snoozes; new version bypasses old snooze; Update opens install confirmation without Settings; cancellation does not download/install/write library"};
        }
        finally
        {
            typeof(App).GetProperty("Quitting")!.SetValue(app,true);Notice(app)?.Close();foreach(var child in window.OwnedWindows.ToArray()){foreach(var nested in child.OwnedWindows.ToArray())nested.Close();child.Close();}window.Close();
            typeof(App).GetProperty("Updates")!.SetValue(app,oldUpdates);typeof(App).GetProperty("Store")!.SetValue(app,oldStore);typeof(App).GetProperty("LibraryWindow")!.SetValue(app,oldWindow);Dispatcher.UIThread.RunJobs();
        }
    }
}
