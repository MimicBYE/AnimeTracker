using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AnimeTracker {
public static class LibraryStorageTests {
 static void Check(bool ok,string message) {if(!ok)throw new Exception(message);}
 static void Reject(Action action,string message) {bool failed=false;try{action();}catch(IOException){failed=true;}catch(InvalidDataException){failed=true;}catch(UnauthorizedAccessException){failed=true;}catch(System.Xml.XmlException){failed=true;}catch(FormatException){failed=true;}catch(ArgumentException){failed=true;}Check(failed,message);}
 public static void Run(string root) {
  var folder=Path.Combine(root,"storage");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"library.xml");
  var anime=new Anime {Title="Тайтл 日本語",Watched=11,Total=12,Status="watching",Score=8,Notes="Личные заметки",Updated=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc)};
  var library=new Library();library.Items.Add(anime);library.Save(path);var original=File.ReadAllBytes(path);var before=anime.Copy();
  Directory.CreateDirectory(path+".tmp");
  Reject(()=>LibraryStorage.Increment(library,anime,path),"Failed increment was accepted");
  Check(anime.Watched==11&&anime.Status=="watching"&&anime.Updated==before.Updated&&File.ReadAllBytes(path).SequenceEqual(original),"Failed increment changed memory/disk");
  var edited=anime.Copy();edited.Title="Changed";edited.Score=2;
  Reject(()=>LibraryStorage.SaveItem(library,anime,edited,path),"Failed edit was accepted");
  Reject(()=>LibraryStorage.SaveItem(library,null,new Anime{Title="New"},path),"Failed add was accepted");
  Reject(()=>LibraryStorage.Remove(library,anime,path),"Failed removal was accepted");
  Check(library.Items.Count==1&&anime.Title==before.Title&&anime.Score==8,"Failed mutations changed live objects");
  Directory.Delete(path+".tmp");
  Check(LibraryStorage.Increment(library,anime,path)&&anime.Watched==12&&anime.Completed,"Successful increment did not complete");
  Check(Library.Load(path).Items.Single().Watched==12,"Increment not persisted");
  Directory.CreateDirectory(path+".tmp");Reject(()=>LibraryStorage.RestoreProgress(library,anime,before,path),"Failed undo was accepted");
  Check(anime.Watched==12&&anime.Completed&&Library.Load(path).Items.Single().Watched==12,"Failed undo changed memory/disk");
  Directory.Delete(path+".tmp");LibraryStorage.RestoreProgress(library,anime,before,path);
  Check(anime.Watched==11&&anime.Updated==before.Updated&&Library.Load(path).Items.Single().Status=="watching","Undo retry lost progress/date");
  LibraryStorage.Remove(library,anime,path);Directory.CreateDirectory(path+".tmp");Reject(()=>LibraryStorage.Insert(library,anime,0,path),"Failed delete undo accepted");Check(library.Items.Count==0,"Failed insert changed memory");Directory.Delete(path+".tmp");LibraryStorage.Insert(library,anime,0,path);
  Check(library.Items.Single()==anime&&Library.Load(path).Items.Single().Id==anime.Id,"Delete undo lost identity");
  foreach(string status in MalImport.StatusKeys) {
   var item=new Anime{Title=status,Status=status,Watched=3,Total=0};var sample=new Library();sample.Items.Add(item);sample.Save(path);
   bool changed=LibraryStorage.Increment(sample,item,path);Check(changed==(status!="completed")&&item.Watched==(status=="completed"?3:4),"Increment status parity: "+status);
  }
  library.Save(path);string valid=File.ReadAllText(path);var doc=XDocument.Parse(valid);
  foreach(string xml in new[]{"<wrong/>","<library><anim/></library>","<library>lost data</library>","<library xmlns='other'><anime/></library>","<!DOCTYPE library [<!ENTITY x 'value'>]><library/>",valid.Replace("<notes>","<unexpected>").Replace("</notes>","</unexpected>")}) {File.WriteAllText(path,xml);Reject(()=>Library.Load(path),"Invalid structure accepted: "+xml);}
  foreach(string key in new[]{"id","title","watched","total","completed","updated"}) {var invalid=XDocument.Parse(valid);invalid.Root.Element("anime").Attribute(key).Remove();File.WriteAllText(path,invalid.ToString());Reject(()=>Library.Load(path),"Missing required field: "+key);}
  var duplicate=XDocument.Parse(valid);duplicate.Root.Add(new XElement(duplicate.Root.Element("anime")));File.WriteAllText(path,duplicate.ToString());Reject(()=>Library.Load(path),"Duplicate id accepted");
  var badStatus=XDocument.Parse(valid);badStatus.Root.Element("anime").SetAttributeValue("status","unknown");File.WriteAllText(path,badStatus.ToString());Reject(()=>Library.Load(path),"Unknown status accepted");
  // Old files lacked the newer catalog/status/score/notes fields; unusual imported progress is retained.
  var legacy=XDocument.Parse(valid);var entry=legacy.Root.Element("anime");foreach(string key in new[]{"status","score","catalogId","malId","english","japanese","romaji","coverUrl"})entry.Attribute(key).Remove();entry.Element("notes").Remove();entry.SetAttributeValue("watched",30);File.WriteAllText(path,legacy.ToString());Check(Library.Load(path).Items.Single().Watched==30,"Legacy/unusual progress rejected");
  File.WriteAllText(path,"<library/>");Check(Library.Load(path).Items.Count==0,"Valid empty library rejected");
  File.WriteAllText(path+".bak",valid);File.WriteAllText(path,"<wrong/>");var backup=File.ReadAllBytes(path+".bak");
  var restored=LibraryStorage.Open(path);Check(!restored.ReadOnly&&restored.Message.Contains("резервная")&&restored.Library.Items.Single().Id==anime.Id,"Backup not restored");
  Check(File.ReadAllText(Directory.GetFiles(folder,"library.xml.corrupt-*").Single())=="<wrong/>"&&File.ReadAllBytes(path+".bak").SequenceEqual(backup),"Recovery destroyed original/backup");
  Check(Library.Load(path).Items.Single().Notes==anime.Notes&&LibraryStorage.Open(path).Message=="","Recovered file not valid on restart");
  File.WriteAllText(path,"broken");File.WriteAllText(path+".bak","<wrong/>");var blocked=LibraryStorage.Open(path);Check(blocked.ReadOnly&&File.ReadAllText(path)=="broken"&&File.ReadAllText(path+".bak")=="<wrong/>","Invalid backup overwrote primary");
  File.Delete(path);File.WriteAllText(path+".bak",valid);Check(!LibraryStorage.Open(path).ReadOnly&&Library.Load(path).Items.Count==1,"Missing primary not restored");
  File.Delete(path);Directory.CreateDirectory(path);var denied=LibraryStorage.Open(path);Check(denied.ReadOnly&&denied.Library.Items.Count==1&&Directory.Exists(path)&&File.ReadAllText(path+".bak")==valid,"Failed recovery did not preserve read-only backup/original");Directory.Delete(path);
  File.Delete(path+".bak");Check(!LibraryStorage.Open(path).ReadOnly&&Library.Load(path).Items.Count==0,"New library blocked");
 }
}
}
