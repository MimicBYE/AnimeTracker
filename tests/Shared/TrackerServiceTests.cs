using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AnimeTracker {
// The same contract suite runs under .NET Framework (Windows self-test) and .NET.
public static class TrackerServiceTests {
 sealed class FakeCatalog : ICatalogSource {
  public readonly List<int[]> Requests=new List<int[]>();
  public Func<int[],Task<List<CatalogEntry>>> Reply=ids=>Task.FromResult(Entries(ids));
  public Task<List<CatalogEntry>> ByIds(IEnumerable<int> ids,CancellationToken token) {var batch=ids.ToArray();Requests.Add(batch);return Reply(batch);}
 }
 static readonly DateTime Now=new DateTime(2026,10,28,10,0,0,DateTimeKind.Utc);
 static List<CatalogEntry> Entries(int[] ids) {return ids.Select(id=>new CatalogEntry {Id=id,MalId=id,Russian="Каталог "+id,Episodes=24,EpisodesAired=15,AiringStatus="ongoing",NextEpisodeAt=new DateTimeOffset(Now.AddDays(1)),Kind="tv",Duration=24}).ToList();}
 static void Check(bool condition,string message) {if(!condition)throw new Exception("TrackerService: "+message);}
 static void Same(Anime before,Anime after,bool personalOnly) {
  foreach(var field in typeof(Anime).GetFields())if(!personalOnly || !new[]{"Total","EpisodesAired","AiringStatus","EpisodesChecked"}.Contains(field.Name))Check(Object.Equals(field.GetValue(before),field.GetValue(after)),"changed "+field.Name);
 }
 static void RejectWrite(Action action) {bool failed=false;try{action();}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}Check(failed,"write failure was not propagated");}
 static async Task Cancelled(Task operation) {bool cancelled=false;try{await operation;}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancelled response was accepted");}
 static TrackerService Open(string root,string name) {return Open(root,name,new FakeCatalog(),()=>Now);}
 static TrackerService Open(string root,string name,FakeCatalog catalog) {return Open(root,name,catalog,()=>Now);}
 static TrackerService Open(string root,string name,FakeCatalog catalog,Func<DateTime> clock) {return new TrackerService(new TrackerFiles(Path.Combine(root,name)),catalog,clock);}
 static Anime Seed(TrackerService service,int id=21,string status="watching") {
  var anime=new Anime {CatalogId=id,MalId=id,Title="Название 日本語 "+id,English="Title "+id,Japanese="日本語",Romaji="Romaji",CoverUrl="https://example.invalid/cover",Genres="Драма|Комедия",Watched=4,Total=12,Score=8,Notes="Личные заметки\nстрока 2",Status=status,Rewatching=true,RewatchEpisode=2,MalXml="<anime><my_score>8</my_score></anime>",Updated=Now.AddDays(-2),AiringStatus="ongoing"};
  service.Library.Items.Add(anime);service.Library.Save(service.LibraryPath);return anime;
 }
 static string MalFile(string root) {
  string path=Path.Combine(root,"mal.xml");new XDocument(new XElement("myanimelist",new XElement("anime",new XElement("series_animedb_id",21),new XElement("series_title","Title 21"),new XElement("series_episodes",12),new XElement("my_watched_episodes",2),new XElement("my_status","Completed"),new XElement("my_score",9)))).Save(path);return path;
 }
 public static async Task Run(string root) {
  root=Path.Combine(root,"svc");Directory.CreateDirectory(root);
  var files=new TrackerFiles(root);bool escaped=false;try{files.PathFor(Path.Combine("..","outside.xml"));}catch(ArgumentException){escaped=true;}Check(escaped,"data path escaped root");
  var nested=Open(root,"nested");bool rejected=false;try{nested.ImportWindowsFolder(root);}catch(ArgumentException){rejected=true;}Check(rejected&&Directory.Exists(nested.Root),"ancestor folder migration accepted");
  foreach(var status in MalImport.StatusKeys) {
   var service=Open(root,status);var anime=Seed(service,21,status);var before=anime.Copy();
   int events=0;service.Changed+=delegate{events++;var saved=Library.Load(service.LibraryPath).Items.FirstOrDefault(a=>a.Id==anime.Id);if(saved!=null)Same(anime,saved,false);};
   bool incremented=service.Increment(anime);Check(incremented==(status!="completed"),"increment status policy");
   if(incremented){Check(events==1&&anime.Watched==5,"increment not published after persistence");service.RestoreProgress(anime,before);Same(before,anime,false);}
   service.Personal(anime,"completed",9);Check(anime.Watched==(status=="completed"?before.Watched:12)&&anime.Score==9,"completion/score or preservation of imported progress");
   service.Notes(anime,"Заметка & 日本語");Check(anime.Notes=="Заметка & 日本語"&&anime.MalXml==before.MalXml,"notes preservation");
   service.Remove(anime);service.Insert(anime,0);Check(service.Library.Items[0]==anime,"remove/restore identity");
   var restart=Open(root,status).Library.Items.Single();Same(anime,restart,false);
  }
  var failed=Open(root,"write");var original=Seed(failed);var snapshot=original.Copy();string bytes=File.ReadAllText(failed.LibraryPath);int notices=0;failed.Changed+=()=>notices++;
  Directory.CreateDirectory(failed.LibraryPath+".tmp");
  string mal=MalFile(root);var incoming=MalImport.Read(mal);
  foreach(var operation in new Action[]{()=>failed.Increment(original),()=>failed.RestoreProgress(original,new Anime {Watched=1,Status="planned"}),()=>failed.Update(original,"Изменено",1,20,"planned",1,"lost"),()=>failed.Personal(original,"completed",10),()=>failed.Notes(original,"lost"),()=>failed.Remove(original),()=>failed.Insert(new Anime{Title="Другой"},0),()=>failed.AddManual("Ручное"),()=>failed.Add(Entries(new[]{22})[0]),()=>failed.CommitMal(mal,incoming,true)}) {
   RejectWrite(operation);Same(snapshot,original,false);Check(failed.Library.Items.Count==1&&File.ReadAllText(failed.LibraryPath)==bytes&&notices==0,"failed command changed live/disk state or notified");
  }
  Directory.Delete(failed.LibraryPath+".tmp");
  foreach(string name in new[]{failed.Files.Choices,failed.Files.Widget,failed.Files.Arcs})Directory.CreateDirectory(name+".tmp");
  var oldChoices=failed.Choices;var oldWidget=failed.Widget;var oldArcs=failed.Arcs;
  RejectWrite(()=>failed.ChangeChoices(next=>{next.Add(original);next.Hide(original,null);}));RejectWrite(()=>failed.SetWidget(original.Id,2,false));RejectWrite(()=>failed.SaveArcs(new List<AnimeArc>{new AnimeArc{AnimeId=original.Id,Name="Арка",From=1,To=2,Type="canon"}}));
  Check(failed.Choices==oldChoices&&failed.Widget==oldWidget&&failed.Arcs==oldArcs&&notices==0,"failed auxiliary save published state");
  foreach(string name in new[]{failed.Files.Choices,failed.Files.Widget,failed.Files.Arcs})Directory.Delete(name+".tmp");
  failed.ChangeChoices(next=>{next.Add(original);next.Hide(original,null);});failed.SetWidget(original.Id,2,false);
  var reloaded=Open(root,"write");Check(reloaded.Choices.IsHidden(reloaded.Library.Items[0])&&reloaded.Choices.Active(reloaded.Library).Length==1&&!reloaded.Widget.Enabled&&reloaded.Widget.Style==2,"choices/widget restart");
  failed.Increment(original);int watched=original.Watched;var imported=failed.CommitMal(mal,incoming,false);var merged=failed.Library.Items.Single();
  Check(merged.Watched==watched&&merged.Status==original.Status&&merged.Notes==original.Notes&&imported.Matched==1&&imported.Added==0&&File.Exists(Path.Combine(imported.Backup,"library-before.xml"))&&File.Exists(Path.Combine(imported.Backup,"source.xml")),"MAL commit/backup/current progress");
  File.WriteAllText(Path.Combine(root,"broken.xml"),"<wrong/>");var brokenRoot=Path.Combine(root,"broken");Directory.CreateDirectory(brokenRoot);File.Copy(Path.Combine(root,"broken.xml"),Path.Combine(brokenRoot,"library.xml"));var broken=Open(root,"broken");
  Check(broken.ReadOnly,"corruption not read-only");bool blocked=false;try{broken.AddManual("Forbidden");}catch(InvalidOperationException){blocked=true;}Check(blocked,"read-only write allowed");

  await RefreshContracts(root);
  foreach(int size in new[]{1000,5000}) {
   var large=Open(root,"large-"+size);large.Library.Items=Enumerable.Range(1,size).Select(i=>new Anime {Id="fixture-"+i,Title="Тайтл "+i,Status=MalImport.StatusKeys[i%5],Watched=3,Total=12,Score=i%11,Notes="Сохранить 日本語"}).ToList();large.Save();
   var selected=large.Library.Items.First(a=>a.Status=="watching");large.Increment(selected);var restart=Open(root,"large-"+size);Check(restart.Library.Items.Count==size,"large library count");
   for(int i=0;i<size;i++)Same(large.Library.Items[i],restart.Library.Items[i],false);
  }
 }
 static async Task RefreshContracts(string root) {
  var catalog=new FakeCatalog();var service=Open(root,"refresh",catalog);var first=Seed(service);var removed=Seed(service,22,"planned");
  for(int id=23;id<=123;id++)Seed(service,id,MalImport.StatusKeys[id%5]);
  var waiting=new TaskCompletionSource<List<CatalogEntry>>();catalog.Reply=ids=>catalog.Requests.Count==1?waiting.Task:Task.FromResult(Entries(ids));
  var refresh=service.RefreshEpisodes(false,CancellationToken.None);Check(catalog.Requests.Count==1,"first batch not requested");await service.RefreshEpisodes(true,CancellationToken.None);Check(catalog.Requests.Count==1,"overlapping episode refresh");
  service.Increment(first);service.Personal(first,"on_hold",9);service.Notes(first,"Изменено во время сети");service.Remove(removed);var manual=service.AddManual("Пока ждём");
  var personal=service.Library.Items.ToDictionary(a=>a.Id,a=>a.Copy());waiting.SetResult(Entries(catalog.Requests[0]));await refresh;
  Check(catalog.Requests.Count==3&&catalog.Requests.All(ids=>ids.Length<=50),"batch limit");
  foreach(var anime in service.Library.Items){Same(personal[anime.Id],anime,true);if(anime.CatalogId>0)Check(anime.Total==24&&anime.EpisodesChecked==Now,"episode update missing");}
  Check(!service.Library.Items.Contains(removed)&&service.Library.Items.Contains(manual),"deleted anime resurrected/new anime lost");Same(first,Library.Load(service.LibraryPath).Items.Single(a=>a.Id==first.Id),false);
  await service.RefreshEpisodes(false,CancellationToken.None);Check(catalog.Requests.Count==3,"six-hour cache ignored");

  foreach(string mode in new[]{"cancel","modal","reload","import"}) {
   var fake=new FakeCatalog();var current=Open(root,mode,fake);var anime=Seed(current);var before=anime.Copy();string disk=File.ReadAllText(current.LibraryPath);int events=0;current.Changed+=()=>events++;
   var pending=new TaskCompletionSource<List<CatalogEntry>>();fake.Reply=ids=>pending.Task;bool canApply=true;var cancel=new CancellationTokenSource();var operation=current.RefreshEpisodes(true,cancel.Token,()=>canApply);
   if(mode=="cancel")cancel.Cancel();if(mode=="modal")canApply=false;if(mode=="reload")current.Reload();if(mode=="import")current.CommitMal(MalFile(root),MalImport.Read(MalFile(root)),false);
   int beforeResponse=events;pending.SetResult(Entries(fake.Requests[0]));if(mode=="cancel")await Cancelled(operation);else await operation;
   Check(events==beforeResponse&&current.Schedule.Count==0&&current.Metadata.Count==0,"stale response published: "+mode);
   if(mode!="import"){Same(before,current.Library.Items[0],false);Check(File.ReadAllText(current.LibraryPath)==disk,"stale response saved: "+mode);}
   fake.Reply=ids=>Task.FromResult(Entries(ids));await current.RefreshEpisodes(true,CancellationToken.None);Check(current.Library.Items[0].Total==24,"refresh gate leaked: "+mode);cancel.Dispose();
  }
  foreach(bool blockSchedule in new[]{true,false}) {
   var fake=new FakeCatalog();var current=Open(root,blockSchedule?"schedule-fail":"episode-fail",fake);var anime=Seed(current);var before=anime.Copy();int events=0;current.Changed+=()=>events++;string disk=File.ReadAllText(current.LibraryPath);
   string block=(blockSchedule?current.Files.Schedule:current.LibraryPath)+".tmp";Directory.CreateDirectory(block);bool failed=false;
   try{await current.RefreshEpisodes(true,CancellationToken.None);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
   Check(failed&&events==0&&current.Schedule.Count==0&&current.Metadata.Count==0&&File.ReadAllText(current.LibraryPath)==disk,"failed refresh published data");Same(before,anime,false);Directory.Delete(block);
   await current.RefreshEpisodes(true,CancellationToken.None);Check(anime.Total==24&&events==1,"refresh not retryable");
  }
  var metaCatalog=new FakeCatalog();var meta=Open(root,"meta",metaCatalog);Directory.CreateDirectory(meta.Files.Metadata+".tmp");bool metaFailed=false;
  try{await meta.RefreshMetadata(new[]{21},CancellationToken.None);}catch(IOException){metaFailed=true;}catch(UnauthorizedAccessException){metaFailed=true;}
  Check(metaFailed&&meta.Metadata.Count==0,"failed metadata cache published");Directory.Delete(meta.Files.Metadata+".tmp");await meta.RefreshMetadata(new[]{21,21,0,-1},CancellationToken.None);
  Check(metaCatalog.Requests.Last().SequenceEqual(new[]{21})&&Open(root,"meta").Metadata[21].Duration==24,"metadata cache restart/deduplication");
  var clockCatalog=new FakeCatalog();DateTime now=Now;var clock=Open(root,"clock",clockCatalog,()=>now);Seed(clock);await clock.RefreshEpisodes(false,CancellationToken.None);now=now.AddHours(6);await clock.RefreshEpisodes(false,CancellationToken.None);Check(clockCatalog.Requests.Count==2,"injected clock/refresh interval");
  var serialCatalog=new FakeCatalog();var serial=Open(root,"serial",serialCatalog);Seed(serial);var hold=new TaskCompletionSource<List<CatalogEntry>>();serialCatalog.Reply=ids=>hold.Task;
  var running=serial.RefreshEpisodes(true,CancellationToken.None);var queuedCancel=new CancellationTokenSource();var queued=serial.RefreshMetadata(new[]{22},queuedCancel.Token);Check(serialCatalog.Requests.Count==1,"metadata overlapped refresh");queuedCancel.Cancel();await Cancelled(queued);hold.SetResult(Entries(serialCatalog.Requests[0]));await running;
  serialCatalog.Reply=ids=>Task.FromResult(Entries(ids));await serial.RefreshMetadata(new[]{22},CancellationToken.None);Check(serialCatalog.Requests.Count==2&&serial.Metadata.ContainsKey(22),"catalog gate not released after cancelled wait");queuedCancel.Dispose();
 }
}
}
