using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AnimeTracker {
public partial class Tracker {
 Window updateNotice;
 string updateNoticeVersion="";
 bool dismissUpdateNotice;
 bool updateNoticeClosing;
 Window BuildUpdateNotice(string version,Action update,Action later){
  var area=SystemParameters.WorkArea;var window=new Window{Title="Новая версия Anime Tracker",Width=Math.Min(420,area.Width-24),Height=Math.Min(230,area.Height-24),ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false,Topmost=true,WindowStartupLocation=WindowStartupLocation.Manual};
  StyleWindow(window);window.Left=Math.Max(area.Left+12,area.Right-window.Width-12);window.Top=Math.Max(area.Top+12,area.Bottom-window.Height-12);
  var body=new DockPanel{Margin=new Thickness(20)};var actions=new WrapPanel{Margin=new Thickness(0,16,0,0)};actions.Children.Add(Primary(Btn("Обновить",update)));actions.Children.Add(Btn("Напомнить позже",later));DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);
  var text=new StackPanel();text.Children.Add(Text("Доступна версия "+version,20,Ink));text.Children.Add(Text("Установим обновление с резервной копией и перезапуском. Можно напомнить через 6 часов.",13,Muted));body.Children.Add(text);window.Content=body;return window;
 }
 void QueueUpdateNotice(){Dispatcher.BeginInvoke(new Action(TryShowUpdateNotice),DispatcherPriority.ApplicationIdle);}
 void UpdateNoticeBlockClosed(object sender,EventArgs e){((Window)sender).Closed-=UpdateNoticeBlockClosed;QueueUpdateNotice();}
 void TryShowUpdateNotice(){
  if(appUpdates==null||quitting||updateNoticeClosing)return;
  if(updateNotice!=null){
   var opened=Windows.Cast<Window>().FirstOrDefault(w=>w!=widget&&w!=full&&w!=updateNotice);
   if(opened!=null){opened.Closed-=UpdateNoticeBlockClosed;opened.Closed+=UpdateNoticeBlockClosed;}
   if(opened!=null||!appUpdates.Options.Automatic||appUpdates.Available==null||appUpdates.Available.Version!=updateNoticeVersion){dismissUpdateNotice=true;updateNotice.Close();}
   return;
  }
  if(!appUpdates.NoticeDue)return;
  var blocker=Windows.Cast<Window>().FirstOrDefault(w=>w!=widget&&w!=full);
  if(blocker!=null){blocker.Closed-=UpdateNoticeBlockClosed;blocker.Closed+=UpdateNoticeBlockClosed;return;}
  var release=appUpdates.BeginNotice();if(release==null)return;
  bool install=false,failed=false;dismissUpdateNotice=false;updateNoticeVersion=release.Version;
  var notice=BuildUpdateNotice(release.Version,delegate{if(appUpdates.Busy)return;install=true;updateNotice.Close();},delegate{updateNotice.Close();});updateNotice=notice;
  notice.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){
   if(quitting||dismissUpdateNotice)return;
   updateNoticeClosing=true;try{appUpdates.RemindLater(release.Version);}catch(Exception error){install=false;e.Cancel=true;ShowMessage(notice,error.Message,"Не удалось отложить напоминание");}finally{updateNoticeClosing=false;}
  };
  notice.Closed+=delegate{
   updateNotice=null;appUpdates.ReleaseNotice(release.Version);if(failed)return;
   if(install&&!quitting&&appUpdates.Available!=null&&appUpdates.Available.Version==release.Version)Dispatcher.BeginInvoke(new Action(delegate{OpenLibrary();OpenUpdates(settingsDraft,true);}),DispatcherPriority.ApplicationIdle);
   else QueueUpdateNotice();
  };
  try{notice.Show();}catch{failed=true;dismissUpdateNotice=true;notice.Close();updateNotice=null;appUpdates.ReleaseNotice(release.Version);}
 }
}
}
