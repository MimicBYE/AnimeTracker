using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AnimeTracker {
public partial class Tracker {
 readonly CancellationTokenSource episodeLifetime=new CancellationTokenSource();
 DispatcherTimer episodeTimer;
 bool updatingEpisodes;
 string episodeUpdateError="";
 event Action ScheduleChanged;
 bool CanUpdateEpisodes() { return !quitting && !storageBlocked && !Windows.Cast<Window>().Any(w=>w!=widget && w!=full); }
 void StartEpisodeUpdates() {
  episodeTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(10) };
  episodeTimer.Tick+=async delegate { episodeTimer.Interval=TimeSpan.FromMinutes(15); await RefreshEpisodes(); };
  episodeTimer.Start();
 }
 async Task RefreshEpisodes(bool force=false) {
  if(updatingEpisodes || !CanUpdateEpisodes()) return;
  updatingEpisodes=true;
  episodeUpdateError="";
  try {
   await service.RefreshEpisodes(force,episodeLifetime.Token,CanUpdateEpisodes);
   Refresh();
  } catch(OperationCanceledException) { }
  catch(Exception ex) { episodeUpdateError="Не удалось обновить. Показаны сохранённые данные."; try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","episode-update-error.txt"),DateTime.UtcNow.ToString("o")+" "+ex.Message); } catch {} }
  finally { updatingEpisodes=false; if(ScheduleChanged!=null) ScheduleChanged(); }
 }
 protected override void OnExit(ExitEventArgs e) {
  if(episodeTimer!=null) episodeTimer.Stop(); episodeLifetime.Cancel(); base.OnExit(e);
 }
}
}
