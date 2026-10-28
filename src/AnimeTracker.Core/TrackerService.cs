using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AnimeTracker {
public sealed class MalImportResult {
 public int Count,Added,Matched;
 public string Backup="";
}
// One instance owns a folder on the host's serialized application context.
// UI, timers, native integration and default OS paths stay in the host.
public partial class TrackerService {
 readonly ICatalogSource catalog;
 readonly Func<DateTime> utcNow;
 readonly bool defaultWidgetEnabled;
 int generation;
 readonly SemaphoreSlim catalogGate=new SemaphoreSlim(1,1);
 public TrackerFiles Files {get;private set;}
 public string Root {get{return Files.Root;}}
 public string LibraryPath {get{return Files.Library;}}
 public Library Library {get;private set;}
 public WatchChoices Choices {get;private set;}
 public ViewPreferences View {get;private set;}
 public WidgetOptions Widget {get;private set;}
 public PersonalOrder Order {get;private set;}
 public List<AnimeArc> Arcs {get;private set;}
 public Dictionary<int,CatalogEntry> Metadata {get;private set;}
 public Dictionary<int,ScheduleEntry> Schedule {get;private set;}
 public string Error {get;private set;}
 public bool ReadOnly {get;private set;}
 public bool ChoicesBlocked {get;private set;}
 public bool ArcsBlocked {get;private set;}
 public event Action Changed;
 public TrackerService(string root) : this(new TrackerFiles(root),new ShikimoriCatalogSource(),()=>DateTime.UtcNow) {}
 public TrackerService(TrackerFiles files,ICatalogSource catalog,Func<DateTime> utcNow,bool defaultWidgetEnabled=true) {
  if(files==null || catalog==null || utcNow==null)throw new ArgumentNullException();
  Files=files;this.catalog=catalog;this.utcNow=utcNow;this.defaultWidgetEnabled=defaultWidgetEnabled;Reload();
 }
 public string FilePath(string name) {return Files.PathFor(name);}
 public void Notify() {if(Changed!=null)Changed();}
 public void Reload() {
  generation++;foreach(var root in Files.Roots)Directory.CreateDirectory(root);Error="";ReadOnly=ChoicesBlocked=ArcsBlocked=false;
  var opened=LibraryStorage.Open(LibraryPath);Library=opened.Library;ReadOnly=opened.ReadOnly;Error=opened.Message;
  try{Choices=WatchChoices.Load(Files.Choices);}catch(Exception e){Choices=new WatchChoices();ChoicesBlocked=true;Error+=" "+e.Message;}
  try{Arcs=ArcData.Load(Files.Arcs);}catch(Exception e){Arcs=new List<AnimeArc>();ArcsBlocked=true;Error+=" Не удалось прочитать арки: "+e.Message;}
  View=ViewPreferences.Load(Files.View);Widget=WidgetOptions.Load(Files.Widget);if(!File.Exists(Files.Widget)&&!File.Exists(Files.Widget+".bak"))Widget.Enabled=defaultWidgetEnabled;
  Order=PersonalOrder.Load(Files.Order);Schedule=CalendarNotes.Load(Files.Schedule);Metadata=ReadMetadata(Files.Metadata);Notify();
 }
 void Writable() {if(ReadOnly)throw new InvalidOperationException(Error);}
 public void SaveView() {View.Save(Files.View);}
 public void SetWidget(string pin,int style,bool? enabled=null) {var next=new WidgetOptions{Pinned=pin,Style=style,Enabled=enabled??Widget.Enabled};next.Save(Files.Widget);Widget=next;Notify();}
 public void ChangeChoices(Action<WatchChoices> edit) {
  if(ChoicesBlocked)throw new InvalidOperationException("Списки очереди и скрытия недоступны для записи.");
  var next=Choices.Copy();edit(next);next.Save(Files.Choices);Choices=next;Notify();
 }
 public Anime Add(CatalogEntry entry) {Writable();var anime=Ratings.AddPlanned(Library,entry,LibraryPath);Metadata[entry.Id]=entry;Notify();return anime;}
 public Anime AddManual(string title) {
  Writable();title=title.Trim();if(title.Length==0)throw new ArgumentException("Введите название.");
  if(Library.Items.Any(a=>String.Equals(a.Title,title,StringComparison.CurrentCultureIgnoreCase)))throw new ArgumentException("Такое название уже есть.");
  var anime=new Anime{Title=title,Status="planned"};LibraryStorage.SaveItem(Library,null,anime,LibraryPath);Notify();return anime;
 }
 public bool Increment(Anime anime) {Writable();var changed=LibraryStorage.Increment(Library,anime,LibraryPath);if(changed)Notify();return changed;}
 public void RestoreProgress(Anime anime,Anime before) {Writable();LibraryStorage.RestoreProgress(Library,anime,before,LibraryPath);Notify();}
 public void Update(Anime anime,string title,int watched,int total,string status,int score,string notes) {Writable();CardEdits.Save(Library,anime,title,watched,total,status,score,notes,LibraryPath);Notify();}
 public void Status(Anime anime,string status) {Personal(anime,status,null);}
 public void Personal(Anime anime,string status,int? score) {Writable();Ratings.UpdatePersonal(Library,anime,status,score,LibraryPath);Notify();}
 public void Notes(Anime anime,string notes) {Writable();CalendarNotes.SaveNotes(Library,anime,notes,LibraryPath);Notify();}
 public void SaveDraft(Anime current,Anime draft) {
  Writable();if(String.IsNullOrWhiteSpace(draft.Title)||draft.Score<0||draft.Score>10)throw new ArgumentException("Проверьте название и оценку.");
  draft=draft.Copy();draft.Title=draft.Title.Trim();draft.SetProgress(draft.Watched,draft.Total,draft.Status);
  if(Library.Items.Any(a=>a!=current&&((draft.CatalogId>0&&a.CatalogId==draft.CatalogId)||String.Equals(a.Title,draft.Title,StringComparison.CurrentCultureIgnoreCase))))throw new ArgumentException("Такое аниме уже есть в библиотеке.");
  LibraryStorage.SaveItem(Library,current,draft,LibraryPath);Notify();
 }
 public void Remove(Anime anime) {Writable();LibraryStorage.Remove(Library,anime,LibraryPath);Notify();}
 public void Insert(Anime anime,int index) {Writable();LibraryStorage.Insert(Library,anime,index,LibraryPath);Notify();}
 public void Save() {Writable();Library.Save(LibraryPath);}
 public void SaveArcs(List<AnimeArc> next) {if(ArcsBlocked)throw new InvalidOperationException("Файл арок недоступен для записи.");ArcData.Save(next,Files.Arcs);Arcs=next;Notify();}
 public MalImportResult CommitMal(string source,List<Anime> incoming,bool importedStatuses) {
  Writable();var rows=MalImport.Preview(Library,incoming);var next=MalImport.Merge(Library,rows,importedStatuses);
  var result=new MalImportResult{Count=incoming.Count,Added=rows.Count(r=>r.Existing==null),Matched=rows.Count(r=>r.Existing!=null)};
  result.Backup=MalImport.Commit(Library,next,source,LibraryPath);Library=next;generation++;Notify();return result;
 }
 public int ImportMal(string source,bool importedStatuses) {return CommitMal(source,MalImport.Read(source),importedStatuses).Count;}
}
}
