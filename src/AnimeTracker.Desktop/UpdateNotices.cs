using AnimeTracker;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace AnimeTracker.Desktop;

public sealed partial class App
{
    Window? updateNotice;
    string updateNoticeVersion="";
    bool dismissUpdateNotice;
    bool updateNoticeClosing;
    internal void PauseUpdateNotice(){if(updateNotice!=null&&!updateNoticeClosing){dismissUpdateNotice=true;updateNotice.Close();}}
    internal void QueueUpdateNotice()=>Dispatcher.UIThread.Post(TryShowUpdateNotice,DispatcherPriority.ApplicationIdle);
    internal static Window BuildUpdateNotice(string version,Action update,Action later)
    {
        var body=Paper.Stack(Paper.Text("Доступна версия "+version,20),Paper.Text("Установим обновление с резервной копией и перезапуском. Можно напомнить через 6 часов.",13,Paper.Muted),Paper.Actions(Paper.Primary(Paper.Button("Обновить",update)),Paper.Button("Напомнить позже",later)));
        return new Window{Title="Новая версия Anime Tracker",Width=420,Height=230,CanResize=false,ShowActivated=false,ShowInTaskbar=false,Topmost=true,Background=Paper.Color("#F8EECC"),Content=new Border{Padding=new Thickness(20),Child=body}};
    }
    internal void TryShowUpdateNotice()
    {
        var updates=Updates;if(updates==null||Quitting||updateNoticeClosing)return;
        if(updateNotice!=null){if(!updates.Options.Automatic||updates.Available==null||updates.Available.Version!=updateNoticeVersion){dismissUpdateNotice=true;updateNotice.Close();}return;}
        if(Store.ModalDepth!=0||!updates.NoticeDue)return;
        var release=updates.BeginNotice();if(release==null)return;
        bool install=false,failed=false;dismissUpdateNotice=false;updateNoticeVersion=release.Version;
        var notice=BuildUpdateNotice(release.Version,()=>{if(updates.Busy)return;install=true;updateNotice!.Close();},()=>updateNotice!.Close());updateNotice=notice;notice.Icon=LibraryWindow.Icon;
        notice.Opened+=(_,_)=>{
            var screen=LibraryWindow.Screens.ScreenFromWindow(LibraryWindow)??notice.Screens.Primary;if(screen==null)return;
            var area=screen.WorkingArea;notice.Width=Math.Min(420,(area.Width-24)/notice.DesktopScaling);notice.Height=Math.Min(230,(area.Height-24)/notice.DesktopScaling);
            notice.Position=new PixelPoint(Math.Max(area.X+12,area.Right-(int)(notice.Width*notice.DesktopScaling)-12),Math.Max(area.Y+12,area.Bottom-(int)(notice.Height*notice.DesktopScaling)-12));
        };
        notice.Closing+=async(_,e)=>{
            if(Quitting||dismissUpdateNotice)return;
            updateNoticeClosing=true;try{updates.RemindLater(release.Version);}catch(Exception error){install=false;e.Cancel=true;await Paper.Message(notice,error.Message,"Не удалось отложить напоминание");}finally{updateNoticeClosing=false;}
        };
        notice.Closed+=(_,_)=>{
            updateNotice=null;updates.ReleaseNotice(release.Version);if(failed)return;
            if(install&&!Quitting&&updates.Available?.Version==release.Version)Dispatcher.UIThread.Post(async()=>{OpenLibrary();await LibraryWindow.UpdatesDialog(LibraryWindow,()=>false,true);},DispatcherPriority.ApplicationIdle);
            else QueueUpdateNotice();
        };
        try{notice.Show();}catch{failed=true;dismissUpdateNotice=true;notice.Close();updateNotice=null;updates.ReleaseNotice(release.Version);}
    }
}
