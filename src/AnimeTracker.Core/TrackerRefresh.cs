using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AnimeTracker {
public partial class TrackerService {
 public async Task RefreshMetadata(IEnumerable<int> source,CancellationToken token) {
  await catalogGate.WaitAsync(token);int version=generation;
  try {
   var ids=source.Where(id=>id>0).Distinct().ToArray();
   for(int offset=0;offset<ids.Length;offset+=50){token.ThrowIfCancellationRequested();var batch=ids.Skip(offset).Take(50).ToArray();var entries=await catalog.ByIds(batch,token);token.ThrowIfCancellationRequested();if(version!=generation)return;
    var next=new Dictionary<int,CatalogEntry>(Metadata);foreach(var entry in entries.Where(e=>batch.Contains(e.Id)))next[entry.Id]=entry;SaveMetadata(next,Files.Metadata);Metadata=next;
   }
  } finally {catalogGate.Release();}
 }
 public async Task RefreshEpisodes(bool force,CancellationToken token,Func<bool> canApply=null) {
  if(ReadOnly||(canApply!=null&&!canApply()))return;
  if(!await catalogGate.WaitAsync(0,token))return;int version=generation;
  try {
   var now=utcNow();var ids=Library.Items.Where(a=>a.CatalogId>0&&(EpisodeUpdates.Due(a,now)||((a.Status=="watching"||a.Status=="planned")&&(force||!Schedule.ContainsKey(a.CatalogId)||now-Schedule[a.CatalogId].Checked>=TimeSpan.FromHours(6))))).Select(a=>a.CatalogId).Distinct().ToArray();
   for(int offset=0;offset<ids.Length;offset+=50){token.ThrowIfCancellationRequested();var batch=ids.Skip(offset).Take(50).ToArray();var entries=await catalog.ByIds(batch,token);token.ThrowIfCancellationRequested();
    if(version!=generation||ReadOnly||(canApply!=null&&!canApply()))return;ApplyEpisodes(entries.Where(e=>batch.Contains(e.Id)).ToList(),utcNow());
   }
  } finally {catalogGate.Release();}
 }
 void ApplyEpisodes(List<CatalogEntry> entries,DateTime now) {
  // Snapshot after await; keep live objects so open rows/cards stay valid.
  var next=new Library{Left=Library.Left,Top=Library.Top,Items=Library.Items.Select(a=>a.Copy()).ToList()};bool changed=false;
  foreach(var anime in next.Items){var entry=entries.FirstOrDefault(e=>e.Id==anime.CatalogId);if(entry!=null)changed=EpisodeUpdates.Apply(anime,entry,now)||changed;}
  var schedule=new Dictionary<int,ScheduleEntry>(Schedule);foreach(var entry in entries)schedule[entry.Id]=new ScheduleEntry{Id=entry.Id,Next=entry.NextEpisodeAt,Status=entry.AiringStatus,Checked=now};
  // Schedule is a rebuildable cache, not a transaction with the personal library.
  CalendarNotes.Save(schedule,Files.Schedule);if(changed)next.Save(LibraryPath);
  if(changed)for(int i=0;i<Library.Items.Count;i++){var original=Library.Items[i];var fresh=next.Items[i];original.Total=fresh.Total;original.EpisodesAired=fresh.EpisodesAired;original.AiringStatus=fresh.AiringStatus;original.EpisodesChecked=fresh.EpisodesChecked;}
  Schedule=schedule;foreach(var entry in entries)Metadata[entry.Id]=entry;Notify();
 }
 static Dictionary<int,CatalogEntry> ReadMetadata(string path) {
  var result=new Dictionary<int,CatalogEntry>();try{if(File.Exists(path))foreach(var x in XElement.Load(path).Elements("anime")){int id=(int)x.Attribute("id");result[id]=new CatalogEntry{Id=id,Kind=(string)x.Attribute("kind")??"",Duration=(int?)x.Attribute("duration")??0,Episodes=(int?)x.Attribute("episodes")??0,EpisodesAired=(int?)x.Attribute("aired")??0,AiringStatus=(string)x.Attribute("status")??"",Genres=(string)x.Attribute("genres")??""};}}catch{result.Clear();}return result;
 }
 static void SaveMetadata(Dictionary<int,CatalogEntry> entries,string path) {
  new XElement("evening",entries.Values.Select(e=>new XElement("anime",new XAttribute("id",e.Id),new XAttribute("kind",e.Kind),new XAttribute("duration",e.Duration),new XAttribute("episodes",e.Episodes),new XAttribute("aired",e.EpisodesAired),new XAttribute("status",e.AiringStatus),new XAttribute("genres",e.Genres)))).Save(path+".tmp");
  if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);
 }
}
}
