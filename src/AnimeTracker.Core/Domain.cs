using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace AnimeTracker {
public struct WidgetDimensions { public double Width,Height; public WidgetDimensions(double width,double height){Width=width;Height=height;} }

public class Anime {
 public string Id = Guid.NewGuid().ToString();
 public string Title = "";
 public int CatalogId;
 public string Genres="";
 public string English="", Japanese="", Romaji="", CoverUrl="";
 public int Watched, Total;
 public int Score;
 public string Notes="";
 public int EpisodesAired;
 public string AiringStatus="";
 public DateTime EpisodesChecked=DateTime.MinValue;
 public string Status="watching";
 public bool Completed { get { return Status=="completed"; } set { Status=value?"completed":"watching"; } }
 public int MalId;
 public string MalXml="";
 public bool Rewatching;
 public int RewatchEpisode;
 public Anime Copy() { return (Anime)MemberwiseClone(); }
 public void CopyFrom(Anime a) {
  Id=a.Id; Title=a.Title; CatalogId=a.CatalogId; Genres=a.Genres; English=a.English; Japanese=a.Japanese; Romaji=a.Romaji; CoverUrl=a.CoverUrl;
  Watched=a.Watched; Total=a.Total; Score=a.Score; Notes=a.Notes; EpisodesAired=a.EpisodesAired; AiringStatus=a.AiringStatus; EpisodesChecked=a.EpisodesChecked;
  Status=a.Status; MalId=a.MalId; MalXml=a.MalXml; Rewatching=a.Rewatching; RewatchEpisode=a.RewatchEpisode; Updated=a.Updated;
 }
 public void SetProgress(int watched,int total,string status) {
  if(watched<0 || total<0 || !MalImport.StatusKeys.Contains(status)) throw new ArgumentException("Некорректный прогресс или статус.");
  if(status=="completed" && total>0) watched=total;
  if(total>0 && watched>total) throw new ArgumentException("Просмотрено больше общего количества серий.");
  Watched=watched; Total=total; Status=status;
 }
 public DateTime Updated = DateTime.UtcNow;
 public void Increment() { if (Total > 0 && Watched >= Total) return; Watched++; Completed = Total > 0 && Watched >= Total; Updated = DateTime.UtcNow; }
}
public class Library {
 public List<Anime> Items = new List<Anime>();
 public double Left = -1, Top = -1;
 public void Save(string path) {
  Directory.CreateDirectory(Path.GetDirectoryName(path));
  var doc = new XDocument(new XElement("library", new XAttribute("left", Left), new XAttribute("top", Top), Items.Select(a => new XElement("anime", new XAttribute("id", a.Id), new XAttribute("title", a.Title), new XAttribute("catalogId", a.CatalogId), new XAttribute("genres", a.Genres), new XAttribute("airingStatus",a.AiringStatus), new XAttribute("episodesAired",a.EpisodesAired), new XAttribute("episodesChecked",a.EpisodesChecked.ToString("o")), new XAttribute("english", a.English), new XAttribute("japanese", a.Japanese), new XAttribute("romaji", a.Romaji), new XAttribute("coverUrl", a.CoverUrl), new XAttribute("watched", a.Watched), new XAttribute("score",a.Score), new XAttribute("total", a.Total), new XAttribute("completed", a.Completed), new XAttribute("status", a.Status), new XAttribute("malId", a.MalId), new XAttribute("rewatching", a.Rewatching), new XAttribute("rewatchEpisode", a.RewatchEpisode), new XAttribute("malXml", a.MalXml), new XAttribute("updated", a.Updated.ToString("o")), new XElement("notes",a.Notes)))));
  string temp = path + ".tmp"; doc.Save(temp);
  if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
 }
 public static Library Load(string path) {
  var lib = new Library(); if(Directory.Exists(path)) throw new IOException("Вместо файла библиотеки обнаружена папка."); if (!File.Exists(path)) return lib;
  XElement root;
  var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=128L*1024*1024 };
  using(var reader=XmlReader.Create(path,settings)) root=XDocument.Load(reader).Root;
  if(root==null || root.Name!="library" || root.Elements().Any(e=>e.Name!="anime") || root.Nodes().OfType<XText>().Any(t=>!String.IsNullOrWhiteSpace(t.Value))) throw new InvalidDataException("Ожидался корневой элемент library со списком anime.");
  lib.Left = (double?)root.Attribute("left") ?? -1; lib.Top = (double?)root.Attribute("top") ?? -1;
  if(Double.IsNaN(lib.Left)||Double.IsInfinity(lib.Left)||Double.IsNaN(lib.Top)||Double.IsInfinity(lib.Top)) throw new InvalidDataException("Некорректные координаты виджета.");
  var ids=new HashSet<string>(StringComparer.Ordinal);
  foreach (var e in root.Elements("anime")) {
   if(e.Elements().Any(n=>n.Name!="notes") || e.Elements("notes").Count()>1 || e.Elements("notes").Any(n=>n.HasElements) || e.Nodes().OfType<XText>().Any(t=>!String.IsNullOrWhiteSpace(t.Value))) throw new InvalidDataException("Некорректная структура записи anime.");
   var a = new Anime { Id=(string)e.Attribute("id"), Title=(string)e.Attribute("title"), CatalogId=(int?)e.Attribute("catalogId") ?? 0, Genres=(string)e.Attribute("genres") ?? "", English=(string)e.Attribute("english") ?? "", Japanese=(string)e.Attribute("japanese") ?? "", Romaji=(string)e.Attribute("romaji") ?? "", CoverUrl=(string)e.Attribute("coverUrl") ?? "", Watched=(int)e.Attribute("watched"), Total=(int)e.Attribute("total"), Completed=(bool)e.Attribute("completed"), Updated=(DateTime)e.Attribute("updated") };
   a.AiringStatus=(string)e.Attribute("airingStatus") ?? ""; a.EpisodesAired=(int?)e.Attribute("episodesAired") ?? 0; a.EpisodesChecked=(DateTime?)e.Attribute("episodesChecked") ?? DateTime.MinValue;
   a.Status=(string)e.Attribute("status") ?? (a.Completed?"completed":"watching"); a.MalId=(int?)e.Attribute("malId") ?? 0; a.Rewatching=(bool?)e.Attribute("rewatching") ?? false; a.RewatchEpisode=(int?)e.Attribute("rewatchEpisode") ?? 0; a.MalXml=(string)e.Attribute("malXml") ?? "";
   a.Notes=(string)e.Element("notes") ?? "";
   a.Score=(int?)e.Attribute("score") ?? Ratings.FromMal(a.MalXml); if(a.Score<0 || a.Score>10) throw new InvalidDataException("Invalid score");
   if (String.IsNullOrWhiteSpace(a.Id) || !ids.Add(a.Id) || String.IsNullOrWhiteSpace(a.Title) || a.Watched < 0 || a.Total < 0 || a.CatalogId<0 || a.MalId<0 || a.EpisodesAired<0 || a.RewatchEpisode<0 || !MalImport.StatusKeys.Contains(a.Status)) throw new InvalidDataException("Некорректные данные anime: проверьте уникальный id, название, числа и статус.");
   lib.Items.Add(a);
  } return lib;
 }
}


public class ImportRow {
 public Anime Incoming, Existing;
 public string Title { get { return Incoming.Title; } }
 public string MalStatus { get { return MalImport.StatusLabel(Incoming.Status); } }
 public string Progress { get { return Incoming.Watched+" / "+(Incoming.Total==0?"?":Incoming.Total.ToString()); } }
 public string Local { get { return Existing==null?"Новая запись":MalImport.StatusLabel(Existing.Status)+" · "+Existing.Watched+" серий"; } }
 public string Note { get; set; }
}
public static class MalImport {
 public static readonly string[] StatusKeys={"watching","completed","planned","on_hold","dropped"};
 public static readonly string[] StatusLabels={"Смотрю","Просмотрено","В планах","Отложено","Брошено"};
 public static string StatusLabel(string key) { int i=Array.IndexOf(StatusKeys,key); return i<0?key:StatusLabels[i]; }
 static string Value(XElement e,string name) { return ((string)e.Element(name)??"").Trim(); }
 static int Number(XElement e,string key,bool required) {
  string s=Value(e,key); int n;
  if(s=="" && !required) return 0;
  if(!Int32.TryParse(s,out n) || n<0 || n>1000000) throw new InvalidDataException("Некорректное поле "+key+" у «"+Value(e,"series_title")+"».");
  return n;
 }
 public static string ParseStatus(string status) {
  switch(status.Trim().ToLowerInvariant().Replace("-","").Replace("_","").Replace(" ","")) {
   case "1": case "watching": return "watching";
   case "2": case "completed": return "completed";
   case "3": case "onhold": return "on_hold";
   case "4": case "dropped": return "dropped";
   case "6": case "plantowatch": return "planned";
   default: throw new InvalidDataException("Неизвестный статус MAL: «"+status+"». Импорт остановлен без изменений.");
  }
 }
 public static List<Anime> Read(string path) {
  using(var file=File.OpenRead(path)) {
   bool gzip=file.ReadByte()==0x1f && file.ReadByte()==0x8b; file.Position=0;
   using(Stream input=gzip?(Stream)new GZipStream(file,CompressionMode.Decompress):file) {
    var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit, XmlResolver=null, MaxCharactersInDocument=32*1024*1024 };
    using(var reader=XmlReader.Create(input,settings)) return Parse(XDocument.Load(reader));
   }
  }
 }
 public static List<Anime> Parse(XDocument document) {
  if(document.Root==null || document.Root.Name!="myanimelist" || document.Root.Element("manga")!=null) throw new InvalidDataException("Выберите экспорт Anime List из MyAnimeList, а не список манги.");
  var items=new List<Anime>(); var ids=new HashSet<int>();
  foreach(var e in document.Root.Elements("anime")) {
   int id=Number(e,"series_animedb_id",true); string title=Value(e,"series_title");
   if(id==0 || title.Length==0) throw new InvalidDataException("В записи нет названия или MAL ID. Импорт остановлен без изменений.");
   if(!ids.Add(id)) throw new InvalidDataException("Файл содержит повторяющийся MAL ID "+id+". Проверьте экспорт: записи не объединялись автоматически.");
   int rewatch=Number(e,"my_rewatching",false); if(rewatch>1) throw new InvalidDataException("Некорректный признак пересмотра у «"+title+"».");
   int score=Number(e,"my_score",false); if(score>10) throw new InvalidDataException("Оценка MAL должна быть от 0 до 10.");
   items.Add(new Anime { Score=score, MalId=id, Title=title, English=title, Status=ParseStatus(Value(e,"my_status")), Watched=Number(e,"my_watched_episodes",true), Total=Number(e,"series_episodes",false), Rewatching=rewatch==1, RewatchEpisode=Number(e,"my_rewatching_ep",false), MalXml=e.ToString(SaveOptions.DisableFormatting) });
  }
  if(items.Count==0) throw new InvalidDataException("В файле не найдено ни одного аниме.");
  return items;
 }
 public static List<ImportRow> Preview(Library library,List<Anime> incoming) {
  var result=new List<ImportRow>(); var used=new HashSet<string>();
  foreach(var a in incoming) {
   var exact=library.Items.Where(x=>x.MalId==a.MalId).ToList();
   if(exact.Count>1) throw new InvalidDataException("В библиотеке несколько записей с MAL ID "+a.MalId+". Сначала устраните дубликаты.");
   Anime match=exact.FirstOrDefault(); string note="";
   if(match==null) {
    // Only one unlinked exact title/alias is a safe candidate; never use fuzzy matching.
    var candidates=library.Items.Where(x=>x.MalId==0 && new[]{x.Title,x.English,x.Romaji,x.Japanese}.Any(t=>String.Equals(t,a.Title,StringComparison.OrdinalIgnoreCase))).ToList();
    if(candidates.Count==1 && !used.Contains(candidates[0].Id)) { match=candidates[0]; note="Совпадение названия"; }
    else if(candidates.Count>0) note="Неоднозначное название: добавится отдельно";
   } else note="Совпадение MAL ID";
   if(match!=null) used.Add(match.Id);
   if(a.Total>0 && a.Watched>a.Total) note+=" · прогресс больше общего числа, сохранится как в XML";
   if(a.Rewatching) note+=" · пересмотр, серия "+a.RewatchEpisode;
   result.Add(new ImportRow { Incoming=a, Existing=match, Note=note.Trim(' ','·') });
  } return result;
 }
 public static Library Merge(Library current,List<ImportRow> rows,bool importedStatuses) {
  var next=new Library { Left=current.Left,Top=current.Top,Items=current.Items.Select(x=>x.Copy()).ToList() };
  foreach(var row in rows) {
   var source=row.Incoming;
   if(row.Existing==null) { next.Items.Add(source.Copy()); continue; }
   var target=next.Items.Single(x=>x.Id==row.Existing.Id);
   if(target.Score==0) target.Score=source.Score;
   target.MalId=source.MalId; target.Watched=Math.Max(target.Watched,source.Watched);
   target.Total=Math.Max(target.Total,source.Total);
   if(importedStatuses) target.Status=source.Status;
   // Keep original fields even when local values win; raw XML also retains scores/dates/tags.
   target.MalXml=source.MalXml; target.Rewatching=importedStatuses?source.Rewatching:target.Rewatching||source.Rewatching; target.RewatchEpisode=Math.Max(target.RewatchEpisode,source.RewatchEpisode);
   target.Updated=DateTime.UtcNow;
  } return next;
 }
 public static string Commit(Library before,Library after,string sourcePath,string libraryPath) {
  string folder=Path.Combine(Path.GetDirectoryName(libraryPath),"imports",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
  Directory.CreateDirectory(folder);
  before.Save(Path.Combine(folder,"library-before.xml"));
  File.Copy(sourcePath,Path.Combine(folder,"source"+(sourcePath.EndsWith(".gz",StringComparison.OrdinalIgnoreCase)?".xml.gz":".xml")));
  after.Save(libraryPath);
  return folder;
 }
}


public static class EpisodeUpdates {
 public static bool Due(Anime a,DateTime now) {
  return a.CatalogId>0 && a.AiringStatus!="released" && now-a.EpisodesChecked>=TimeSpan.FromHours(6);
 }
 public static bool Apply(Anime a,CatalogEntry entry,DateTime now) {
  if(a.CatalogId!=entry.Id || (a.MalId>0 && entry.MalId>0 && a.MalId!=entry.MalId) || !new[]{"anons","ongoing","released"}.Contains(entry.AiringStatus)) return false;
  // This is catalog metadata only. Never call SetProgress or change Updated.
  if(entry.Episodes>0) a.Total=entry.Episodes;
  a.EpisodesAired=Math.Max(0,entry.EpisodesAired); a.AiringStatus=entry.AiringStatus; a.EpisodesChecked=now;
  return true;
 }
 public static void Test() {
  var now=DateTime.UtcNow;
  foreach(string status in MalImport.StatusKeys) {
   var a=new Anime { CatalogId=21,MalId=21,Watched=100,Total=120,Status=status,Rewatching=true,RewatchEpisode=4,MalXml="original" };
   var updated=a.Updated;
   if(!Due(a,now)) throw new Exception("Initial catalog discovery");
   Apply(a,new CatalogEntry { Id=21,MalId=21,Episodes=90,EpisodesAired=85,AiringStatus="ongoing" },now);
   if(a.Watched!=100 || a.Status!=status || a.Updated!=updated || !a.Rewatching || a.RewatchEpisode!=4 || a.MalXml!="original" || a.Total!=90 || Due(a,now)) throw new Exception("Catalog update changed user progress");
   Apply(a,new CatalogEntry { Id=21,Episodes=0,EpisodesAired=91,AiringStatus="ongoing" },now);
   if(a.Total!=90 || !Due(a,now.AddHours(6))) throw new Exception("Unknown total or refresh interval");
   if(Apply(a,new CatalogEntry { Id=22,AiringStatus="released" },now) || Apply(a,new CatalogEntry { Id=21,MalId=22,AiringStatus="released" },now)) throw new Exception("Catalog identity mismatch");
   Apply(a,new CatalogEntry { Id=21,Episodes=130,AiringStatus="released" },now);
   if(Due(a,now.AddDays(10)) || a.Watched!=100 || a.Status!=status) throw new Exception("Final airing update");
  }
 }
}


public static class Ratings {
 public static readonly string[] Labels=new[]{"Без оценки"}.Concat(Enumerable.Range(1,10).Select(n=>n+" / 10")).ToArray();
 public static int FromMal(string xml) {
  if(String.IsNullOrWhiteSpace(xml)) return 0;
  try { int value; return Int32.TryParse((string)XElement.Parse(xml).Element("my_score"),out value) && value>=0 && value<=10?value:0; } catch(System.Xml.XmlException) { return 0; }
 }
 public static Anime[] Top(Library library) { return library.Items.Where(a=>a.Score>0).OrderByDescending(a=>a.Score).ThenBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.Id).ToArray(); }
 public static Anime Find(Library library,CatalogEntry entry) {
  return library.Items.FirstOrDefault(a=>entry.Id>0 && a.CatalogId==entry.Id)
   ?? library.Items.FirstOrDefault(a=>entry.MalId>0 && a.MalId==entry.MalId);
 }
 public static Anime AddPlanned(Library library,CatalogEntry entry,string path) {
  var existing=Find(library,entry); if(existing!=null) return existing;
  if(entry.Id<=0 || String.IsNullOrWhiteSpace(entry.Title)) throw new ArgumentException("Нет данных выбранного аниме.");
  var a=new Anime { CatalogId=entry.Id,MalId=entry.MalId,Title=entry.Title,English=entry.English,Japanese=entry.Japanese,Romaji=entry.Romaji,CoverUrl=entry.CoverUrl,Genres=entry.Genres,Total=entry.Episodes,EpisodesAired=entry.EpisodesAired,AiringStatus=entry.AiringStatus,EpisodesChecked=entry.AiringStatus==""?DateTime.MinValue:DateTime.UtcNow,Status="planned",Watched=0,Score=0 };
  var next=new Library { Left=library.Left,Top=library.Top,Items=library.Items.Concat(new[]{a}).ToList() };
  // Publish in memory only after a successful atomic save.
  next.Save(path); library.Items.Add(a); return a;
 }
 public static void UpdatePersonal(Library library,Anime anime,string status,int? score,string path) {
  if(!library.Items.Contains(anime)) throw new InvalidOperationException("Аниме больше нет в библиотеке.");
  if(status!=null && !MalImport.StatusKeys.Contains(status)) throw new ArgumentException("Неизвестный статус.");
  if(score.HasValue && (score.Value<0 || score.Value>10)) throw new ArgumentOutOfRangeException("score");
  var changed=anime.Copy();
  if(status!=null && status!=anime.Status) {
   changed.Status=status;
   if(status=="completed" && changed.Total>0) changed.Watched=changed.Total;
  }
  if(score.HasValue) changed.Score=score.Value;
  if(changed.Status==anime.Status && changed.Score==anime.Score && changed.Watched==anime.Watched) return;
  changed.Updated=DateTime.UtcNow;
  var next=new Library { Left=library.Left,Top=library.Top,Items=library.Items.Select(a=>a==anime?changed:a).ToList() };
  next.Save(path);
  anime.Status=changed.Status; anime.Score=changed.Score; anime.Watched=changed.Watched; anime.Updated=changed.Updated;
 }
 public static void Test(string dir) {
  Directory.CreateDirectory(dir); string path=Path.Combine(dir,"ratings.xml");
  try {
   var a=new Anime { Title="A",Score=8,Watched=5,Status="on_hold",MalXml="<anime><my_score>9</my_score></anime>" };
   var b=new Anime { Title="B",Score=10 }; var c=new Anime { Title="C" };
   var lib=new Library(); lib.Items.AddRange(new[]{a,b,c}); lib.Save(path);
   var read=Library.Load(path); if(read.Items[0].Score!=8 || read.Items[0].Watched!=5 || read.Items[0].Status!="on_hold" || Top(read).Length!=2 || Top(read)[0].Title!="B") throw new Exception("Ratings persistence/top");
   a.Score=0; lib.Save(path); if(Library.Load(path).Items[0].Score!=0) throw new Exception("Cleared rating restored from MAL");
   var legacy=XDocument.Load(path); legacy.Root.Elements("anime").First().Attribute("score").Remove(); legacy.Save(path); if(Library.Load(path).Items[0].Score!=9) throw new Exception("MAL rating migration");
   if(LibraryView.Select(lib.Items,"","","",8)[0]!=b || LibraryView.Select(lib.Items,"","","",9).Last().Score!=0) throw new Exception("Rating sort");
   var parsed=Catalog.Parse("{\"data\":{\"animes\":[{\"id\":\"1\",\"name\":\"Test\",\"score\":9.25}]}}"); if(parsed[0].Score!=9.25) throw new Exception("Catalog score parsing");
   var entry=new CatalogEntry { Id=123,MalId=456,Russian="Новый",English="New",Japanese="日本語",Episodes=12,Score=9.5,Genres="Экшен",CoverUrl="cover",AiringStatus="ongoing",EpisodesAired=4 };
   var added=AddPlanned(lib,entry,path); var saved=Library.Load(path).Items.Single(x=>x.CatalogId==123);
   if(saved.Status!="planned" || saved.Watched!=0 || saved.Score!=0 || saved.Total!=12 || saved.Genres!="Экшен" || saved.Japanese!="日本語" || saved.CoverUrl!="cover") throw new Exception("Add from ranking persistence");
   added.Status="completed"; added.Watched=12; added.Score=8; var updated=added.Updated;
   if(AddPlanned(lib,entry,path)!=added || lib.Items.Count!=4 || added.Score!=8 || added.Status!="completed" || added.Watched!=12 || added.Updated!=updated) throw new Exception("Repeated ranking add changed existing anime");
   added.CatalogId=0; if(Find(lib,entry)!=added) throw new Exception("MAL identity matching");
   if(Find(lib,new CatalogEntry { Id=789,MalId=987,Russian=added.Title })!=null) throw new Exception("Different IDs matched by title");
   bool failed=false; try { AddPlanned(lib,new CatalogEntry { Id=789,Russian="Failure" },dir); } catch(IOException) { failed=true; } catch(UnauthorizedAccessException) { failed=true; }
   if(!failed || lib.Items.Count!=4) throw new Exception("Failed save changed library");
   added.Status="watching"; added.Watched=3; added.Rewatching=true; added.RewatchEpisode=2;
   UpdatePersonal(lib,added,"completed",null,path);
   saved=Library.Load(path).Items.Single(x=>x.Id==added.Id);
   if(saved.Status!="completed" || saved.Watched!=12 || saved.Score!=8 || !saved.Rewatching || saved.RewatchEpisode!=2) throw new Exception("Inline completed status persistence");
   added.Watched=7; UpdatePersonal(lib,added,null,10,path);
   if(added.Watched!=7 || added.Status!="completed" || Library.Load(path).Items.Single(x=>x.Id==added.Id).Score!=10) throw new Exception("Score edit changed progress");
   UpdatePersonal(lib,added,"planned",0,path); if(added.Watched!=7 || added.Score!=0 || added.Status!="planned") throw new Exception("Inline status/clear score");
   added.Total=0; UpdatePersonal(lib,added,"completed",null,path); if(added.Watched!=7) throw new Exception("Unknown total changed progress");
   updated=added.Updated; failed=false; try { UpdatePersonal(lib,added,"dropped",5,dir); } catch(IOException) { failed=true; } catch(UnauthorizedAccessException) { failed=true; }
   if(!failed || added.Status!="completed" || added.Score!=0 || added.Updated!=updated) throw new Exception("Inline failed save changed memory");
  } finally { foreach(var file in new[]{path,path+".bak",path+".tmp",dir+".tmp"}) if(File.Exists(file)) File.Delete(file); }
 }
}


public class SequelSuggestion {
 public CatalogEntry Entry;
 public string[] Sources;
}
public static class SequelSuggestions {
 public static List<SequelSuggestion> Select(Library library,Dictionary<int,List<CatalogEntry>> links,bool includeCompleted,string airing) {
  var sources=library.Items.Where(a=>a.Status=="completed" && a.CatalogId>0).ToArray();
  return sources.Where(a=>links.ContainsKey(a.CatalogId)).SelectMany(a=>links[a.CatalogId].Where(e=>e.Id!=a.CatalogId).Select(e=>new { Entry=e,Source=a.Title }))
   .GroupBy(x=>x.Entry.Id).Select(g=>new SequelSuggestion { Entry=g.First().Entry,Sources=g.Select(x=>x.Source).Distinct().OrderBy(x=>x).ToArray() })
   .Where(x=>includeCompleted || Ratings.Find(library,x.Entry)==null || Ratings.Find(library,x.Entry).Status!="completed")
   .Where(x=>airing=="" || x.Entry.AiringStatus==airing)
   .OrderBy(x=>x.Entry.AiringStatus=="released"?0:x.Entry.AiringStatus=="ongoing"?1:2).ThenBy(x=>x.Entry.Title,StringComparer.CurrentCultureIgnoreCase).ToList();
 }
 public static void Test() {
  var parsed=Catalog.ParseSequels("{\"data\":{\"animes\":[{\"id\":\"1\",\"related\":[{\"relationKind\":\"sequel\",\"anime\":{\"id\":\"2\",\"malId\":\"20\",\"name\":\"Next\",\"status\":\"released\"}},{\"relationKind\":\"prequel\",\"anime\":{\"id\":\"3\",\"name\":\"Previous\"}},{\"relationKind\":\"sequel\",\"anime\":null}]}]}}");
  if(parsed[1].Count!=1 || parsed[1][0].Id!=2) throw new Exception("Sequel direction/type");
  var lib=new Library(); lib.Items.Add(new Anime { CatalogId=1,Title="First",Status="completed" }); lib.Items.Add(new Anime { CatalogId=4,Title="Other",Status="completed" }); parsed[4]=parsed[1];
  var suggestions=Select(lib,parsed,false,""); if(suggestions.Count!=1 || suggestions[0].Sources.Length!=2) throw new Exception("Sequel deduplication");
  lib.Items.Add(new Anime { MalId=20,Title="Next",Status="completed" });
  if(Select(lib,parsed,false,"").Count!=0 || Select(lib,parsed,true,"").Count!=1 || Select(lib,parsed,true,"anons").Count!=0) throw new Exception("Sequel library/status filtering");
  lib.Items[0].Status=lib.Items[1].Status="watching"; if(Select(lib,parsed,true,"").Count!=0) throw new Exception("Only completed sources");
 }
}


public class ScheduleEntry {
 public int Id;
 public DateTimeOffset? Next;
 public string Status="";
 public DateTime Checked;
}
public static class CalendarNotes {
 public static DateTime Moscow(DateTimeOffset date) { return date.ToOffset(TimeSpan.FromHours(3)).DateTime; }
 public static DateTime Week(DateTime date) { return date.Date.AddDays(-((int)date.DayOfWeek+6)%7); }
 public static void SaveNotes(Library library,Anime anime,string notes,string path) {
  if(!library.Items.Contains(anime)) throw new InvalidOperationException("Аниме больше нет в библиотеке.");
  if(notes==anime.Notes) return;
  var changed=anime.Copy(); changed.Notes=notes; changed.Updated=DateTime.UtcNow;
  var next=new Library { Left=library.Left,Top=library.Top,Items=library.Items.Select(a=>a==anime?changed:a).ToList() };
  next.Save(path); anime.Notes=notes; anime.Updated=changed.Updated;
 }
 public static Dictionary<int,ScheduleEntry> Load(string path) {
  foreach(var file in new[]{path,path+".bak"}) try {
   if(!File.Exists(file)) continue;
   return XDocument.Load(file).Root.Elements("anime").Select(e=>new ScheduleEntry { Id=(int)e.Attribute("id"),Status=(string)e.Attribute("status")??"",Checked=(DateTime)e.Attribute("checked"),Next=(DateTimeOffset?)e.Attribute("next") }).ToDictionary(e=>e.Id);
  } catch(Exception) { }
  return new Dictionary<int,ScheduleEntry>();
 }
 public static void Save(Dictionary<int,ScheduleEntry> entries,string path) {
  Directory.CreateDirectory(Path.GetDirectoryName(path));
  new XDocument(new XElement("schedule",entries.Values.Select(e=>new XElement("anime",new XAttribute("id",e.Id),new XAttribute("status",e.Status),new XAttribute("checked",e.Checked.ToString("o")),e.Next.HasValue?new XAttribute("next",e.Next.Value.ToString("o")):null)))).Save(path+".tmp");
  if(File.Exists(path)) File.Replace(path+".tmp",path,path+".bak"); else File.Move(path+".tmp",path);
 }
 public static void Test(string dir) {
  Directory.CreateDirectory(dir); string path=Path.Combine(dir,"notes.xml"),cache=Path.Combine(dir,"schedule.xml");
  try {
   var lib=new Library(); var anime=new Anime { Title="Test",Watched=5,Total=12,Status="completed",Score=8,MalXml="original" }; lib.Items.Add(anime);
   SaveNotes(lib,anime,"Строка 1\n日本語 & <заметка>",path); var read=Library.Load(path).Items[0];
   if(read.Notes!=anime.Notes || read.Watched!=5 || read.Status!="completed" || read.Score!=8 || read.MalXml!="original") throw new Exception("Notes round-trip/preserved progress");
   SaveNotes(lib,anime,"",path); if(Library.Load(path).Items[0].Notes!="") throw new Exception("Clear notes");
   var legacy=XDocument.Load(path); legacy.Root.Element("anime").Element("notes").Remove(); legacy.Save(path); if(Library.Load(path).Items[0].Notes!="") throw new Exception("Legacy notes");
   bool failed=false; try { SaveNotes(lib,anime,"unsaved",dir); } catch(IOException) { failed=true; } catch(UnauthorizedAccessException) { failed=true; } if(!failed || anime.Notes!="") throw new Exception("Notes failed write");
   var date=DateTimeOffset.Parse("2026-10-25T23:30:00Z"); if(Moscow(date).Date!=new DateTime(2026,10,26) || Week(Moscow(date))!=new DateTime(2026,10,26)) throw new Exception("Moscow day/week boundary");
   var entries=new Dictionary<int,ScheduleEntry>(); entries[1]=new ScheduleEntry { Id=1,Next=date,Status="ongoing",Checked=DateTime.UtcNow }; entries[2]=new ScheduleEntry { Id=2,Next=null,Checked=DateTime.UtcNow }; Save(entries,cache);
   var loaded=Load(cache); if(loaded.Count!=2 || loaded[1].Next!=date || loaded[2].Next!=null) throw new Exception("Calendar cache");
   var parsed=Catalog.Parse("{\"data\":{\"animes\":[{\"id\":\"1\",\"name\":\"Test\",\"nextEpisodeAt\":\"2026-10-25T23:30:00Z\"}]}}"); if(parsed[0].NextEpisodeAt!=date) throw new Exception("Calendar parsing");
  } finally { foreach(var file in new[]{path,path+".bak",path+".tmp",cache,cache+".bak",cache+".tmp",dir+".tmp"}) if(File.Exists(file)) File.Delete(file); }
 }
}


public static class CardEdits {
 public static void Save(Library library,Anime anime,string title,int watched,int total,string status,int score,string notes,string path) {
  if(!library.Items.Contains(anime)) throw new InvalidOperationException("Аниме больше нет в библиотеке.");
  title=title.Trim();
  if(title.Length==0 || watched<0 || total<0 || score<0 || score>10 || !MalImport.StatusKeys.Contains(status)) throw new ArgumentException("Проверьте название, серии, статус и оценку.");
  if(library.Items.Any(a=>a!=anime && String.Equals(a.Title,title,StringComparison.CurrentCultureIgnoreCase))) throw new ArgumentException("Такое название уже есть в библиотеке.");
  var changed=anime.Copy(); changed.Title=title; changed.Score=score; changed.Notes=notes;
  // Opening and saving notes must not normalize imported or older progress.
  if(watched!=anime.Watched || total!=anime.Total || status!=anime.Status) changed.SetProgress(watched,total,status);
  if(changed.Title==anime.Title && changed.Score==anime.Score && changed.Notes==anime.Notes && changed.Watched==anime.Watched && changed.Total==anime.Total && changed.Status==anime.Status) return;
  changed.Updated=DateTime.UtcNow;
  new Library { Left=library.Left,Top=library.Top,Items=library.Items.Select(a=>a==anime?changed:a).ToList() }.Save(path);
  anime.Title=changed.Title; anime.Watched=changed.Watched; anime.Total=changed.Total; anime.Status=changed.Status; anime.Score=changed.Score; anime.Notes=changed.Notes; anime.Updated=changed.Updated;
 }
 public static void Test(string dir) {
  var a=new Anime { Title="Card",Status="completed",Watched=7,Total=12,Score=8,MalXml="original",Rewatching=true,RewatchEpisode=3 };
  var library=new Library(); library.Items.Add(a); var path=Path.Combine(dir,"card.xml");
  Save(library,a,a.Title,7,12,a.Status,9,"Заметка\n日本語",path);
  var read=Library.Load(path).Items.Single();
  if(read.Watched!=7 || read.Score!=9 || read.Notes!=a.Notes || read.MalXml!="original" || !read.Rewatching || read.RewatchEpisode!=3) throw new Exception("Card changed unrelated data");
  Save(library,a,a.Title,7,12,"watching",9,a.Notes,path); Save(library,a,a.Title,7,12,"completed",9,a.Notes,path);
  if(a.Watched!=12) throw new Exception("Card completion progress");
  bool failed=false; try { Save(library,a,"Changed",1,12,"watching",1,"lost",dir); } catch(IOException) { failed=true; } catch(UnauthorizedAccessException) { failed=true; }
  if(!failed || a.Title!="Card" || a.Watched!=12 || a.Score!=9) throw new Exception("Card failed save mutated library");
 }
}


public class WidgetOptions {
 public string Pinned=""; public int Style; public bool Enabled=true;
 public static WidgetOptions Load(string path) {
  foreach(var file in new[]{path,path+".bak"}) try { if(!File.Exists(file)) continue; var x=XElement.Load(file); int style=(int?)x.Attribute("style")??0; return new WidgetOptions { Pinned=(string)x.Attribute("pinned")??"",Style=style>=0&&style<=2?style:0,Enabled=(bool?)x.Attribute("enabled")??true }; } catch(Exception) { }
  return new WidgetOptions();
 }
 public void Save(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)); new XElement("widget",new XAttribute("pinned",Pinned),new XAttribute("style",Style),new XAttribute("enabled",Enabled)).Save(path+".tmp"); if(File.Exists(path)) File.Replace(path+".tmp",path,path+".bak"); else File.Move(path+".tmp",path); }
 public static Anime Selected(Library library,string pinned) { return library.Items.FirstOrDefault(a=>a.Id==pinned)??library.Items.OrderByDescending(a=>a.Updated).FirstOrDefault(); }
 public static WidgetDimensions Size(int style) { return style==1?new WidgetDimensions(320,100):style==2?new WidgetDimensions(260,270):new WidgetDimensions(260,124); }
}
public static class EveningPrerequisites {
 public static bool Ready(CatalogEntry entry,Library library,Dictionary<int,List<CatalogEntry>> links){return Ready(entry.Id,library,links,new HashSet<int>());}
 static bool Ready(int id,Library library,Dictionary<int,List<CatalogEntry>> links,HashSet<int> path) {
  List<CatalogEntry> previous;if(!links.TryGetValue(id,out previous)||!path.Add(id))return false;
  try {foreach(var entry in previous){var owned=Ratings.Find(library,entry);if(owned==null||owned.Status!="completed"||!Ready(entry.Id,library,links,path))return false;}return true;}finally{path.Remove(id);}
 }
 public static int[] Missing(Library library,Dictionary<int,List<CatalogEntry>> links){return links.Values.SelectMany(items=>items).Where(e=>{var owned=Ratings.Find(library,e);return owned!=null&&owned.Status=="completed"&&!links.ContainsKey(e.Id);}).Select(e=>e.Id).Distinct().ToArray();}
 public static async Task<Dictionary<int,List<CatalogEntry>>> Load(List<CatalogEntry> entries,Library library,CancellationToken token,Action<int> progress) {
  var links=entries.Where(e=>e.Prequels!=null).ToDictionary(e=>e.Id,e=>e.Prequels);var attempted=new HashSet<int>();
  while(attempted.Count<1000){
   var missing=entries.Where(e=>!links.ContainsKey(e.Id)).Select(e=>e.Id).Concat(Missing(library,links)).Where(id=>!attempted.Contains(id)).Distinct().Take(10).ToArray();if(missing.Length==0)break;
   foreach(int id in missing)attempted.Add(id);progress(attempted.Count);
   try{var result=await Catalog.Relations(missing,"prequel",token);foreach(var pair in result)links[pair.Key]=pair.Value;}catch(OperationCanceledException){throw;}catch(Exception){/* Unverified predecessors remain ineligible. */}
   token.ThrowIfCancellationRequested();
  }return links;
 }
 public static void Test() {
  var first=new CatalogEntry{Id=1,MalId=101,Romaji="First"};var second=new CatalogEntry{Id=2,Romaji="Second"};var third=new CatalogEntry{Id=3,Romaji="Third"};
  var lib=new Library();var links=new Dictionary<int,List<CatalogEntry>>{{1,new List<CatalogEntry>()},{2,new List<CatalogEntry>{first}},{3,new List<CatalogEntry>{second}}};
  if(!Ready(first,lib,links)||Ready(second,lib,links)||Ready(third,lib,links))throw new Exception("Evening unseen prequel");
  var watched=new Anime{MalId=101,Status="watching",Watched=12,Total=12};lib.Items.Add(watched);if(Ready(second,lib,links))throw new Exception("Evening unfinished status");watched.Status="completed";if(!Ready(second,lib,links))throw new Exception("Evening MAL prequel match");
  lib.Items.Add(new Anime{CatalogId=2,Status="completed"});if(!Ready(third,lib,links))throw new Exception("Evening completed chain");watched.Status="planned";if(Ready(third,lib,links))throw new Exception("Evening missing earlier ancestor");watched.Status="completed";
  links.Remove(1);if(Ready(third,lib,links)||!Missing(lib,links).Contains(1))throw new Exception("Evening unknown relations");links[1]=new List<CatalogEntry>{third};lib.Items.Add(new Anime{CatalogId=3,Status="completed"});if(Ready(third,lib,links))throw new Exception("Evening relation cycle");
  var parsed=Catalog.Parse("{\"data\":{\"animes\":[{\"id\":\"8\",\"name\":\"Series\",\"related\":[{\"relationKind\":\"prequel\",\"anime\":{\"id\":\"9\",\"name\":\"Before\"}},{\"relationKind\":\"side_story\",\"anime\":{\"id\":\"10\",\"name\":\"Optional\"}}]}]}}").Single();if(parsed.Prequels==null||parsed.Prequels.Count!=1||parsed.Prequels[0].Id!=9)throw new Exception("Evening prequel parsing");
 }
}
public static class EveningChoice {
 public static readonly string[] Moods={"Любое настроение","Посмеяться","Спокойный вечер","Приключения и драйв","Драма и чувства","Интрига и загадки"};
 static readonly string[][] MoodGenres={new string[0],new[]{"Комедия"},new[]{"Повседневность","Иясикэй"},new[]{"Экшен","Приключения"},new[]{"Драма","Романтика"},new[]{"Детектив","Триллер","Психологическое"}};
 public static bool Matches(Anime a,CatalogEntry meta,int mood,string genre,int format,int minutes,bool fromTop=false) {
  if(a.Status=="completed" || (!fromTop && a.Status!="planned") || (meta==null?a.AiringStatus:meta.AiringStatus)=="anons") return false;
  var genres=(meta!=null && meta.Genres.Length>0?meta.Genres:a.Genres).Split('|');
  if(mood>0 && mood<MoodGenres.Length && !genres.Intersect(MoodGenres[mood]).Any()) return false;
  if(genre!="" && !genres.Contains(genre)) return false;
  string kind=meta==null?"":meta.Kind; int total=meta==null?a.Total:meta.Episodes;
  if(format==1 && kind!="movie") return false;
  bool series=new[]{"tv","ova","ona"}.Contains(kind);
  if(format==2 && (!series || total<=0 || total>13)) return false;
  if(format==3 && !series) return false;
  int watched=Progress(a,fromTop);
  if(total>0 && watched>=total) return false;
  if(meta!=null && meta.AiringStatus=="ongoing" && meta.EpisodesAired<=watched) return false;
  if(minutes>0 && (meta==null || meta.Duration<=0 || (long)meta.Duration*(kind=="movie"?Math.Max(1,total-watched):1)>minutes)) return false;
  return true;
 }
 public static int Progress(Anime a,bool fromTop){return a.Watched;}
 public static void Test(string dir) {
  EveningPrerequisites.Test();
  var lib=new Library(); var first=new Anime {Title="First",Status="planned",Total=12,Genres="Комедия"}; var second=new Anime {Title="Second",Updated=first.Updated.AddMinutes(1)}; lib.Items.AddRange(new[]{first,second});
  if(WidgetOptions.Selected(lib,first.Id)!=first || WidgetOptions.Selected(lib,"missing")!=second) throw new Exception("Widget pin/fallback");
  var path=Path.Combine(dir,"widget.xml"); new WidgetOptions {Pinned=first.Id,Style=2,Enabled=false}.Save(path); var read=WidgetOptions.Load(path); if(read.Pinned!=first.Id||read.Style!=2||read.Enabled) throw new Exception("Widget preferences restart");
  var meta=new CatalogEntry {Kind="tv",Duration=24,Episodes=12,Genres="Комедия",AiringStatus="released"};
  if(!Matches(first,meta,1,"",2,30)||Matches(first,meta,1,"",2,15)||Matches(first,meta,3,"",0,30)||Matches(first,null,0,"",0,30)) throw new Exception("Evening duration/mood/unknown");
  meta.Kind="movie"; meta.Episodes=1; meta.Duration=100; if(Matches(first,meta,0,"",1,60)||!Matches(first,meta,0,"",1,120)) throw new Exception("Evening film budget");
  first.Status="completed"; if(Matches(first,meta,0,"",0,0)) throw new Exception("Evening only planned"); first.Watched=1; if(Matches(first,meta,0,"",1,120,true))throw new Exception("Evening excludes watched from top"); first.Status="on_hold";first.Watched=0;if(!Matches(first,meta,0,"",0,0,true)||Matches(first,meta,0,"",0,0))throw new Exception("Evening source selection"); first.Status="planned"; meta.AiringStatus="anons"; if(Matches(first,meta,0,"",0,0)) throw new Exception("Evening unreleased");
 }
}


public class TopVote { public string Winner,Loser; public int Score; }
public class PersonalOrder {
 public List<TopVote> Votes=new List<TopVote>();
 public static PersonalOrder Load(string path) {
  foreach(var file in new[]{path,path+".bak"}) try {if(!File.Exists(file))continue; return new PersonalOrder {Votes=XElement.Load(file).Elements("vote").Select(x=>new TopVote {Winner=(string)x.Attribute("winner"),Loser=(string)x.Attribute("loser"),Score=(int)x.Attribute("score")}).Where(v=>v.Winner!=null&&v.Loser!=null&&v.Winner!=v.Loser&&v.Score>0&&v.Score<=10).ToList()};} catch(Exception) { }
  return new PersonalOrder();
 }
 public void Save(string path) {Directory.CreateDirectory(Path.GetDirectoryName(path)); new XElement("comparisons",Votes.Select(v=>new XElement("vote",new XAttribute("winner",v.Winner),new XAttribute("loser",v.Loser),new XAttribute("score",v.Score)))).Save(path+".tmp"); if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);}
 public List<TopVote> Active(IEnumerable<Anime> items) {var ids=items.Where(a=>a.Score>0).ToDictionary(a=>a.Id,a=>a.Score);return Votes.Where(v=>ids.ContainsKey(v.Winner)&&ids.ContainsKey(v.Loser)&&ids[v.Winner]==v.Score&&ids[v.Loser]==v.Score).ToList();}
 static bool Reaches(string from,string to,List<TopVote> votes) {var seen=new HashSet<string>();var pending=new Stack<string>();pending.Push(from);while(pending.Count>0){var id=pending.Pop();if(id==to)return true;if(!seen.Add(id))continue;foreach(var edge in votes.Where(v=>v.Winner==id))pending.Push(edge.Loser);}return false;}
 public Anime[] Rank(IEnumerable<Anime> source) {
  var items=source.Where(a=>a.Score>0).ToArray();var votes=Active(items);var result=new List<Anime>();
  foreach(var group in items.GroupBy(a=>a.Score).OrderByDescending(g=>g.Key)) {var remaining=group.OrderBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.Id).ToList();while(remaining.Count>0){var ids=new HashSet<string>(remaining.Select(a=>a.Id));var next=remaining.FirstOrDefault(a=>!votes.Any(v=>v.Loser==a.Id&&ids.Contains(v.Winner)))??remaining[0];result.Add(next);remaining.Remove(next);}}
  return result.ToArray();
 }
 public Anime[] Pair(Anime[] all,Anime[] eligible,HashSet<string> skipped) {var votes=Active(all);for(int i=0;i<eligible.Length;i++)for(int j=i+1;j<eligible.Length;j++){var a=eligible[i];var b=eligible[j];if(a.Score==b.Score&&!skipped.Contains(Key(a,b))&&!Reaches(a.Id,b.Id,votes)&&!Reaches(b.Id,a.Id,votes))return new[]{a,b};}return null;}
 public static string Key(Anime a,Anime b) {return String.CompareOrdinal(a.Id,b.Id)<0?a.Id+"|"+b.Id:b.Id+"|"+a.Id;}
 public void Choose(Anime[] all,Anime winner,Anime loser,string path) {
  if(winner.Score<=0||winner.Score!=loser.Score||!all.Contains(winner)||!all.Contains(loser)||winner==loser)throw new InvalidOperationException("Сравнивать можно разные аниме с одинаковой оценкой.");
  if(Reaches(loser.Id,winner.Id,Active(all)))throw new InvalidOperationException("Этот порядок уже определён предыдущими сравнениями.");
  var next=new PersonalOrder {Votes=Votes.Concat(new[]{new TopVote {Winner=winner.Id,Loser=loser.Id,Score=winner.Score}}).ToList()};next.Save(path);Votes=next.Votes;
 }
 public static string Kind(Anime anime,Dictionary<int,string> kinds) {string kind;if(kinds.TryGetValue(anime.CatalogId,out kind))return kind;try{var value=(string)XElement.Parse(anime.MalXml).Element("series_type");return (value??"").ToLowerInvariant();}catch{return "";}}
 public static bool Matches(Anime a,int format,string genre,Dictionary<int,string> kinds){var kind=Kind(a,kinds);return (format==0||(format==1?kind=="movie":new[]{"tv","ova","ona"}.Contains(kind)))&&(genre==""||LibraryView.Genres(a).Contains(genre));}
 public static void Test(string dir) {
  var a=new Anime{Title="A",Score=9};var b=new Anime{Title="B",Score=9};var c=new Anime{Title="C",Score=10};var d=new Anime{Title="D",Score=9};var all=new[]{a,b,c,d};var order=new PersonalOrder();var path=Path.Combine(dir,"order.xml");order.Choose(all,b,a,path);order.Choose(all,a,d,path);
  if(!order.Rank(all).SequenceEqual(new[]{c,b,a,d})||PersonalOrder.Load(path).Rank(all)[1]!=b||order.Pair(all,new[]{b,d},new HashSet<string>())!=null)throw new Exception("Personal order persistence/transitivity");
  b.Score=8;if(order.Rank(all).Last()!=b)throw new Exception("Ratings must outrank comparisons");b.Score=9;
  bool failed=false;try{order.Choose(all,d,b,path);}catch(InvalidOperationException){failed=true;}if(!failed)throw new Exception("Comparison cycle");
  var fresh=new PersonalOrder();failed=false;try{fresh.Choose(all,b,a,dir);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}if(!failed||fresh.Votes.Count!=0)throw new Exception("Comparison failed write");
  a.MalXml="<anime><series_type>Movie</series_type></anime>";a.Genres="Комедия";if(!Matches(a,1,"Комедия",new Dictionary<int,string>())||Matches(a,2,"",new Dictionary<int,string>()))throw new Exception("Top categories");
 }
}


public class AnimeArc {
 public string Id=Guid.NewGuid().ToString(),AnimeId="",Name="",Type="auto";
 public int From,To; public string Intervals="",CatalogKey=""; public bool Hidden;
}
public class EpisodeFlags {
 public DateTime Checked;
 public Dictionary<int,string> Items=new Dictionary<int,string>();
}
public static class ArcData {
 public static readonly string[] Types={"auto","canon","filler","mixed","unknown"};
 public static readonly string[] Labels={"По отметкам серий","Не филлер","Филлер","Смешанная","Нет данных"};
 static readonly SemaphoreSlim Gate=new SemaphoreSlim(1,1);
 static DateTime lastRequest=DateTime.MinValue;
 static readonly HttpClient Client=CreateClient();
 static HttpClient CreateClient(){
#if !MODERN_DOTNET
ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
#endif
var client=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};client.DefaultRequestHeaders.UserAgent.ParseAdd("AnimeTrackerDesktop/1.0");return client;}
 public static string Label(string type){if(type=="recap")return "Рекап";if(type=="anime")return "Канон аниме";int i=Array.IndexOf(Types,type);return i<0?"Нет данных":Labels[i];}
 public static List<AnimeArc> Load(string path) {
  if(!File.Exists(path))return new List<AnimeArc>();
  var root=XElement.Load(path);if(root.Name!="arcs")throw new InvalidDataException("Неверный файл арок.");
  var items=root.Elements("arc").Select(x=>new AnimeArc{Id=(string)x.Attribute("id"),AnimeId=(string)x.Attribute("anime"),Name=(string)x.Attribute("name"),From=(int)x.Attribute("from"),To=(int)x.Attribute("to"),Type=(string)x.Attribute("type"),Intervals=(string)x.Attribute("intervals")??"",CatalogKey=(string)x.Attribute("catalog")??"",Hidden=(bool?)x.Attribute("hidden")??false}).ToList();foreach(var a in items)Validate(a);return items;
 }
 public static void Validate(AnimeArc a){if(String.IsNullOrWhiteSpace(a.Name)||a.Name.Length>120||String.IsNullOrWhiteSpace(a.AnimeId)||a.From<1||a.To<a.From||a.To>100000||!Types.Contains(a.Type))throw new ArgumentException("Укажите название и диапазон серий от 1 до 100000.");var set=ArcCatalog.Episodes(a);if(set.Min!=a.From||set.Max!=a.To)throw new ArgumentException("Границы не совпадают с диапазонами.");}
 public static void Save(List<AnimeArc> arcs,string path){foreach(var a in arcs)Validate(a);Directory.CreateDirectory(Path.GetDirectoryName(path));new XElement("arcs",arcs.Select(a=>new XElement("arc",new XAttribute("id",a.Id),new XAttribute("anime",a.AnimeId),new XAttribute("name",a.Name),new XAttribute("from",a.From),new XAttribute("to",a.To),new XAttribute("type",a.Type),new XAttribute("intervals",a.Intervals),new XAttribute("catalog",a.CatalogKey),new XAttribute("hidden",a.Hidden)))).Save(path+".tmp");if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);}
 public static string Classification(AnimeArc arc,EpisodeFlags flags){if(arc.Type!="auto")return arc.Type;var types=new HashSet<string>();foreach(int n in ArcCatalog.Episodes(arc)){string value;if(!flags.Items.TryGetValue(n,out value)||value=="unknown")return "unknown";types.Add(value=="anime"?"canon":value);}return types.Count==1?types.First():"mixed";}
 public static EpisodeFlags ReadCache(string path){foreach(var file in new[]{path,path+".bak"})try{if(!File.Exists(file))continue;var root=XElement.Load(file);return new EpisodeFlags{Checked=(DateTime)root.Attribute("checked"),Items=root.Elements("episode").ToDictionary(x=>(int)x.Attribute("n"),x=>(string)x.Attribute("type"))};}catch{}return new EpisodeFlags();}
 public static void SaveCache(EpisodeFlags flags,string path){Directory.CreateDirectory(Path.GetDirectoryName(path));new XElement("episodes",new XAttribute("checked",flags.Checked.ToString("o")),flags.Items.OrderBy(x=>x.Key).Select(x=>new XElement("episode",new XAttribute("n",x.Key),new XAttribute("type",x.Value)))).Save(path+".tmp");if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);}
 public static int ParsePage(string json,EpisodeFlags flags) {
  var root=new JsonCodec().DeserializeObject(json) as Dictionary<string,object>;object raw;
  if(root==null||!root.TryGetValue("data",out raw)||!(raw is object[]))throw new InvalidDataException("Неверный ответ источника серий.");
  foreach(var value in (object[])raw){var entry=value as Dictionary<string,object>;int n;object id;if(entry==null||!entry.TryGetValue("mal_id",out id)||!Int32.TryParse(Convert.ToString(id),out n)||n<1||n>100000)throw new InvalidDataException("Неверный номер серии.");object filler,recap;bool hasFiller=entry.TryGetValue("filler",out filler)&&filler is bool,hasRecap=entry.TryGetValue("recap",out recap)&&recap is bool;flags.Items[n]=hasRecap&&(bool)recap?"recap":hasFiller?(bool)filler?"filler":"canon":"unknown";}
  object page;if(!root.TryGetValue("pagination",out page))throw new InvalidDataException("Нет данных страниц.");var pagination=page as Dictionary<string,object>;object last;int pages;if(pagination==null||!pagination.TryGetValue("last_visible_page",out last)||!Int32.TryParse(Convert.ToString(last),out pages)||pages<1||pages>200)throw new InvalidDataException("Неверное количество страниц.");return pages;
 }
 public static async Task<EpisodeFlags> Fetch(int malId,string path,CancellationToken token,Action<int,int> progress) {
  if(malId<=0)throw new ArgumentException("Нет ID MyAnimeList.");await Gate.WaitAsync(token);
  try{var fresh=new EpisodeFlags();int pages=1;for(int page=1;page<=pages;page++){int wait=(int)(lastRequest.AddMilliseconds(1100)-DateTime.UtcNow).TotalMilliseconds;if(wait>0)await Task.Delay(wait,token);lastRequest=DateTime.UtcNow;progress(page,pages);using(var response=await Client.GetAsync("https://api.jikan.moe/v4/anime/"+malId+"/episodes?page="+page,token)){response.EnsureSuccessStatusCode();pages=ParsePage(await response.Content.ReadAsStringAsync(),fresh);}}token.ThrowIfCancellationRequested();fresh.Checked=DateTime.UtcNow;SaveCache(fresh,path);return fresh;}finally{Gate.Release();}
 }
 public static List<AnimeArc> Ranges(EpisodeFlags flags){var result=new List<AnimeArc>();foreach(var item in flags.Items.OrderBy(x=>x.Key)){var last=result.LastOrDefault();if(last!=null&&last.To+1==item.Key&&last.Type==item.Value)last.To=item.Key;else result.Add(new AnimeArc{From=item.Key,To=item.Key,Type=item.Value});}return result;}
 public static void Test(string dir){var flags=new EpisodeFlags();ParsePage("{\"pagination\":{\"last_visible_page\":1},\"data\":[{\"mal_id\":1,\"filler\":false,\"recap\":false},{\"mal_id\":2,\"filler\":true,\"recap\":false},{\"mal_id\":4,\"filler\":null,\"recap\":false}]}",flags);var arc=new AnimeArc{AnimeId="test",Name="Арка & 日本語",From=1,To=2};if(Classification(arc,flags)!="mixed")throw new Exception("Mixed arc");arc.To=3;if(Classification(arc,flags)!="unknown"||Ranges(flags).Count!=3)throw new Exception("Missing episodes treated as canon");arc.Type="filler";if(Classification(arc,flags)!="filler")throw new Exception("Manual arc override");var path=Path.Combine(dir,"arcs.xml");Save(new List<AnimeArc>{arc},path);if(Load(path).Single().Name!=arc.Name)throw new Exception("Arc roundtrip");var cache=Path.Combine(dir,"flags.xml");flags.Checked=DateTime.UtcNow;SaveCache(flags,cache);if(ReadCache(cache).Items[4]!="unknown")throw new Exception("Episode cache");}
}


public class QueueOption { public Anime Anime; public string Title { get { return Anime.Title; } } }
public class HiddenAnime {
 public string LibraryId="",Title="";
 public int CatalogId,MalId;
 public bool Matches(Anime anime){return (!String.IsNullOrEmpty(LibraryId)&&LibraryId==anime.Id)||(CatalogId>0&&CatalogId==anime.CatalogId)||(MalId>0&&MalId==anime.MalId);}
 public HiddenAnime Copy(){return (HiddenAnime)MemberwiseClone();}
}
public class WatchChoices {
 public List<string> Queue=new List<string>();
 public List<HiddenAnime> Hidden=new List<HiddenAnime>();
 public WatchChoices Copy(){return new WatchChoices{Queue=Queue.ToList(),Hidden=Hidden.Select(x=>x.Copy()).ToList()};}
 public bool IsHidden(Anime anime){return Hidden.Any(x=>x.Matches(anime));}
 public static bool CanQueue(Anime anime){return anime.Status!="completed"&&(anime.Total<=0||anime.Watched<anime.Total);}
 public Anime[] Active(Library library){return Queue.Select(id=>library.Items.FirstOrDefault(a=>a.Id==id)).Where(a=>a!=null&&CanQueue(a)).ToArray();}
 public void Add(Anime anime){if(!CanQueue(anime))throw new InvalidOperationException("Это аниме уже просмотрено.");if(!Queue.Contains(anime.Id))Queue.Add(anime.Id);}
 public void Move(string id,int direction,Library library){var active=Active(library);int index=Array.FindIndex(active,a=>a.Id==id);int target=index+direction;if(index<0||target<0||target>=active.Length)return;int first=Queue.IndexOf(id),second=Queue.IndexOf(active[target].Id);Queue[first]=active[target].Id;Queue[second]=id;}
 public void Hide(Anime anime,CatalogEntry entry){var value=new HiddenAnime{LibraryId=anime.Id.StartsWith("top:")?"":anime.Id,Title=anime.Title,CatalogId=entry==null?anime.CatalogId:entry.Id,MalId=entry==null?anime.MalId:entry.MalId};Hidden.RemoveAll(x=>x.Matches(anime)||(value.CatalogId>0&&x.CatalogId==value.CatalogId)||(value.MalId>0&&x.MalId==value.MalId));Hidden.Add(value);}
 public void Save(string path){
  Directory.CreateDirectory(Path.GetDirectoryName(path));
  new XElement("watchChoices",new XAttribute("version",1),new XElement("queue",Queue.Select(id=>new XElement("anime",new XAttribute("id",id)))),new XElement("hidden",Hidden.Select(a=>new XElement("anime",new XAttribute("id",a.LibraryId),new XAttribute("catalogId",a.CatalogId),new XAttribute("malId",a.MalId),new XAttribute("title",a.Title))))).Save(path+".tmp");
  if(File.Exists(path))File.Replace(path+".tmp",path,path+".bak");else File.Move(path+".tmp",path);
 }
 public static WatchChoices Load(string path){
  bool exists=false;foreach(var file in new[]{path,path+".bak"})try{
   if(!File.Exists(file))continue;exists=true;var root=XElement.Load(file);if(root.Name!="watchChoices"||root.Element("queue")==null||root.Element("hidden")==null)throw new InvalidDataException();
   var state=new WatchChoices{Queue=root.Element("queue").Elements("anime").Select(x=>(string)x.Attribute("id")).ToList(),Hidden=root.Element("hidden").Elements("anime").Select(x=>new HiddenAnime{LibraryId=(string)x.Attribute("id")??"",Title=(string)x.Attribute("title")??"",CatalogId=(int?)x.Attribute("catalogId")??0,MalId=(int?)x.Attribute("malId")??0}).ToList()};
   if(state.Queue.Any(String.IsNullOrWhiteSpace)||state.Queue.Distinct().Count()!=state.Queue.Count||state.Hidden.Any(a=>a.CatalogId<0||a.MalId<0||(String.IsNullOrWhiteSpace(a.LibraryId)&&a.CatalogId==0&&a.MalId==0)))throw new InvalidDataException();return state;
  }catch{}
  if(exists)throw new InvalidDataException("Не удалось прочитать очередь и скрытые аниме. Файлы сохранены; изменение этих списков отключено.");return new WatchChoices();
 }
 public static void Test(string dir){
  var first=new Anime{Title="Первая & 日本語",Status="planned",CatalogId=12,MalId=112};var second=new Anime{Title="Second",Status="planned"};var done=new Anime{Title="Done",Status="completed",Total=12,Watched=12};var lib=new Library();lib.Items.AddRange(new[]{first,done,second});var state=new WatchChoices();state.Add(first);state.Add(second);state.Add(first);state.Queue.Insert(1,done.Id);state.Move(second.Id,-1,lib);if(!state.Active(lib).SequenceEqual(new[]{second,first}))throw new Exception("Queue reorder/dedup/completed");
  state.Queue.Add("missing");state.Hide(new Anime{Id="top:12",Title="Русское название",CatalogId=12,MalId=112},null);if(!state.IsHidden(first)||state.IsHidden(second))throw new Exception("Persistent hidden identity");
  var path=Path.Combine(dir,"watch-choices.xml");state.Save(path);var read=Load(path);if(!read.Active(lib).SequenceEqual(new[]{second,first})||!read.IsHidden(new Anime{MalId=112})||read.Hidden[0].Title!="Русское название")throw new Exception("Queue/hidden persistence");
  read.Hidden.Clear();read.Save(path);if(Load(path).IsHidden(first))throw new Exception("Restore hidden title");File.WriteAllText(path,"broken");if(!Load(path).IsHidden(first))throw new Exception("Watch choices backup");
  File.WriteAllText(path+".bak","broken");bool rejected=false;try{Load(path);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Protect corrupt preferences");
  first.Status="completed";if(state.Active(lib).Contains(first))throw new Exception("Completed removed from active queue");first.Status="planned";if(!state.Active(lib).Contains(first)||first.Watched!=0||first.Score!=0)throw new Exception("Queue changed personal fields");
 }
}

}
