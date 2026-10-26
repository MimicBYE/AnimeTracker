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
 string schedulePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","schedule.xml"); } }
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
   var schedule=CalendarNotes.Load(schedulePath);
   var ids=library.Items.Where(a=>a.CatalogId>0 && (EpisodeUpdates.Due(a,DateTime.UtcNow) || ((a.Status=="watching" || a.Status=="planned") && (force || !schedule.ContainsKey(a.CatalogId) || DateTime.UtcNow-schedule[a.CatalogId].Checked>=TimeSpan.FromHours(6))))).Select(a=>a.CatalogId).Distinct().ToArray();
   for(int offset=0;offset<ids.Length;offset+=50) {
    var entries=await Catalog.ByIds(ids.Skip(offset).Take(50),episodeLifetime.Token);
    if(!CanUpdateEpisodes()) return;
    // Snapshot after the network request: manual changes made while waiting survive.
    var snapshot=new Library { Left=library.Left,Top=library.Top,Items=library.Items.Select(a=>a.Copy()).ToList() };
    bool changed=false;
    foreach(var a in snapshot.Items) {
     var entry=entries.FirstOrDefault(e=>e.Id==a.CatalogId);
     if(entry!=null) changed=EpisodeUpdates.Apply(a,entry,DateTime.UtcNow)||changed;
    }
    foreach(var entry in entries) schedule[entry.Id]=new ScheduleEntry { Id=entry.Id,Next=entry.NextEpisodeAt,Status=entry.AiringStatus,Checked=DateTime.UtcNow };
    CalendarNotes.Save(schedule,schedulePath);
    if(!changed) continue;
    snapshot.Save(dataPath);
    for(int i=0;i<library.Items.Count;i++) {
     var a=library.Items[i]; var fresh=snapshot.Items[i];
     a.Total=fresh.Total; a.EpisodesAired=fresh.EpisodesAired; a.AiringStatus=fresh.AiringStatus; a.EpisodesChecked=fresh.EpisodesChecked;
    }
    Refresh();
   }
  } catch(OperationCanceledException) { }
  catch(Exception ex) { episodeUpdateError="Не удалось обновить. Показаны сохранённые данные."; try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","episode-update-error.txt"),DateTime.UtcNow.ToString("o")+" "+ex.Message); } catch {} }
  finally { updatingEpisodes=false; if(ScheduleChanged!=null) ScheduleChanged(); }
 }
 protected override void OnExit(ExitEventArgs e) {
  if(episodeTimer!=null) episodeTimer.Stop(); episodeLifetime.Cancel(); base.OnExit(e);
 }
}
}
