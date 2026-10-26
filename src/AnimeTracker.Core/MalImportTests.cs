using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace AnimeTracker {
public static class MalImportTests {
 static void Check(bool value,string name) { if(!value) throw new Exception(name); }
 static XElement Entry(int id,string status,int watched) {
  return new XElement("anime",new XElement("series_animedb_id",id),new XElement("series_title","Тест & 日本語 "+id),new XElement("series_episodes",24),new XElement("my_watched_episodes",watched),new XElement("my_status",status),new XElement("my_score",9),new XElement("my_tags","tag & test"),new XElement("my_start_date","2025-01-02"),new XElement("my_rewatching",id==1?1:0),new XElement("my_rewatching_ep",id==1?3:0));
 }
 public static string Run() {
  string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tests","mal-import"); Directory.CreateDirectory(folder);
  string file=Path.Combine(folder,"sample.xml"), gz=Path.Combine(folder,"sample.xml.gz"), libraryFile=Path.Combine(folder,"library.xml");
  string[] statuses={"Watching","Completed","On-Hold","Dropped","Plan to Watch","1","2","3","4","6"};
  var doc=new XDocument(new XElement("myanimelist",new XElement("myinfo",new XElement("user_total_anime",150)),Enumerable.Range(1,150).Select(id=>Entry(id,statuses[(id-1)%10],id%25)))); doc.Save(file);
  using(var output=File.Create(gz)) using(var zip=new GZipStream(output,CompressionMode.Compress)) using(var input=File.OpenRead(file)) input.CopyTo(zip);
  var items=MalImport.Read(file); var zipped=MalImport.Read(gz);
  Check(items.Count==150 && zipped.Count==150 && items.Select(x=>x.Status).Distinct().Count()==5,"150 records/all statuses/gzip");
  Check(items[0].Rewatching && items[0].RewatchEpisode==3 && items[0].MalXml.Contains("my_score") && items[0].MalXml.Contains("my_start_date"),"extra fields/rewatch preserved");
  var current=new Library(); current.Items.Add(new Anime { MalId=1,Title="Местное название",Watched=18,Total=24,Status="on_hold" });
  var preview=MalImport.Preview(current,items); Check(preview.Count(x=>x.Existing!=null)==1,"ID matching");
  var merged=MalImport.Merge(current,preview,false); Check(current.Items.Count==1 && current.Items[0].Watched==18,"non-mutating preview/merge");
  Check(merged.Items.Count==150 && merged.Items[0].Watched==18 && merged.Items[0].Status=="on_hold","keep local status/progress");
  var sourceStatus=MalImport.Merge(current,preview,true); Check(sourceStatus.Items[0].Status=="watching" && sourceStatus.Items[0].Watched==18,"source status option preserves progress");
  var twice=MalImport.Merge(merged,MalImport.Preview(merged,items),false); Check(twice.Items.Count==150 && twice.Items[0].Watched==18,"repeat import idempotence");
  string backup=MalImport.Commit(current,merged,gz,libraryFile);
  Check(File.Exists(Path.Combine(backup,"library-before.xml")) && File.ReadAllBytes(Path.Combine(backup,"source.xml.gz")).SequenceEqual(File.ReadAllBytes(gz)),"original and library backups");
  var restored=Library.Load(libraryFile); Check(restored.Items.Count==150 && restored.Items[0].Status=="on_hold" && restored.Items[0].RewatchEpisode==3 && restored.Items[0].MalXml.Contains("my_score"),"new schema round-trip");
  var exceeded=MalImport.Parse(new XDocument(new XElement("myanimelist",Entry(900,"Watching",30)))); Check(exceeded[0].Watched==30 && exceeded[0].Total==24,"no clipping of imported progress");
  var over=new Library(); over.Items=exceeded; over.Save(Path.Combine(folder,"over.xml")); Check(Library.Load(Path.Combine(folder,"over.xml")).Items[0].Watched==30,"unusual progress reload");
  var old=XDocument.Load(libraryFile); foreach(var e in old.Root.Elements("anime")) foreach(string key in new[]{"status","malId","malXml","rewatching","rewatchEpisode"}) e.Attribute(key).Remove(); old.Save(Path.Combine(folder,"legacy.xml"));
  Check(Library.Load(Path.Combine(folder,"legacy.xml")).Items.Count==150,"legacy schema");
  foreach(var invalid in new[]{new XDocument(new XElement("myanimelist",Entry(1,"Mystery",0))),new XDocument(new XElement("myanimelist",Entry(1,"Watching",-1))),new XDocument(new XElement("myanimelist",Entry(1,"Watching",0),Entry(1,"Completed",24))),new XDocument(new XElement("myanimelist",new XElement("manga")))}) {
   bool failed=false; try { MalImport.Parse(invalid); } catch(InvalidDataException) { failed=true; } Check(failed,"invalid data rejected");
  }
  File.WriteAllText(Path.Combine(folder,"unsafe.xml"),"<!DOCTYPE myanimelist [<!ENTITY x SYSTEM 'file:///example'>]><myanimelist>&x;</myanimelist>");
  bool rejected=false; try { MalImport.Read(Path.Combine(folder,"unsafe.xml")); } catch(System.Xml.XmlException) { rejected=true; } Check(rejected,"DTD rejected");
  return "PASS: 150 entries, 5 statuses (text and numeric), XML and GZIP, Unicode, rewatch and raw fields, exact ID matching, non-mutating preview, safe progress/status merge, repeated import, backup integrity, schema round-trip, legacy compatibility, unusual progress, invalid/duplicate/manga input, DTD rejection.";
 }
}
}
