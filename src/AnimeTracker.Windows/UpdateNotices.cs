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
  var area=SystemParameters.WorkArea;var window=new Window{Title="Новая версия Anime Tracker",Width=Math.Min(360,area.Width-24),Height=Math.Min(160,area.Height-24),ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false,Topmost=true,WindowStartupLocation=WindowStartupLocation.Manual};
  StyleWindow(window);window.Left=Math.Max(area.Left+12,area.Right-window.Width-12);window.Top=Math.Max(area.Top+12,area.Bottom-window.Height-12);
  var body=new DockPanel{Margin=new Thickness(14)};var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};actions.Children.Add(Primary(Btn("Обновить",update)));actions.Children.Add(Btn("Напомнить позже",later));DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);
  var text=new StackPanel();text.Children.Add(Text("Доступна версия "+version,16,Ink));var detail=Text("Обновление с резервной копией",12,Muted);detail.Margin=new Thickness(0,4,0,0);text.Children.Add(detail);body.Children.Add(text);window.Content=body;AttachNoticeTimeout(window);return window;
 }
 void AttachNoticeTimeout(Window window){
  var clock=new System.Diagnostics.Stopwatch();UpdateNoticeLifetime lifetime=null;var timer=new DispatcherTimer();
  Action schedule=delegate{
   timer.Stop();if(lifetime==null)return;bool paused=window.IsMouseOver||(window.IsActive&&window.IsKeyboardFocusWithin);lifetime.SetPaused(paused);if(paused)return;
   var remaining=lifetime.Remaining;if(remaining<=TimeSpan.Zero){window.Close();return;}timer.Interval=remaining;timer.Start();
  };
  window.Loaded+=delegate{clock.Start();lifetime=new UpdateNoticeLifetime(()=>clock.Elapsed);schedule();};
  window.MouseEnter+=delegate{schedule();};window.MouseLeave+=delegate{schedule();};window.IsKeyboardFocusWithinChanged+=delegate{schedule();};timer.Tick+=delegate{schedule();};
  window.Activated+=delegate{schedule();};window.Deactivated+=delegate{schedule();};
  window.Closing+=delegate{timer.Stop();clock.Stop();lifetime=null;};window.Closed+=delegate{timer.Stop();};
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
