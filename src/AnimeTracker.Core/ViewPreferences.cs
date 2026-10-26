using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AnimeTracker {
public class ViewPreferences {
 public int Sort;
 public string Status="", Search="";
 public bool MatchAll=true;
 public List<string> Genres=new List<string>();
 public void Save(string path) {
  Directory.CreateDirectory(Path.GetDirectoryName(path));
  var doc=new XDocument(new XElement("view",new XAttribute("sort",Sort),new XAttribute("status",Status),new XAttribute("search",Search),new XAttribute("matchAll",MatchAll),Genres.Distinct().Select(g=>new XElement("genre",g))));
  doc.Save(path+".tmp"); if(File.Exists(path)) File.Replace(path+".tmp",path,path+".bak"); else File.Move(path+".tmp",path);
 }
 public static ViewPreferences Load(string path) {
  foreach(string candidate in new[]{path,path+".bak"}) {
   if(!File.Exists(candidate)) continue;
   try {
    var root=XDocument.Load(candidate).Root; if(root==null || root.Name!="view") continue;
    var p=new ViewPreferences { Sort=(int?)root.Attribute("sort")??0, Status=(string)root.Attribute("status")??"", Search=(string)root.Attribute("search")??"", MatchAll=(bool?)root.Attribute("matchAll")??true, Genres=root.Elements("genre").Select(e=>e.Value).Where(s=>!String.IsNullOrWhiteSpace(s)).Distinct().ToList() };
    if(p.Sort<0 || p.Sort>=LibraryView.SortLabels.Length) p.Sort=0;
    if(!MalImport.StatusKeys.Contains(p.Status)) p.Status="";
    return p;
   } catch(System.Xml.XmlException) {} catch(IOException) {} catch(FormatException) {} catch(OverflowException) {}
  }
  return new ViewPreferences();
 }
 public static void Test(string directory) {
  string file=Path.Combine(directory,"view-settings.xml");
  try {
   var settings=new ViewPreferences { Sort=4,Status="completed",Search="日本語 & тест",MatchAll=false,Genres=new List<string>{"Экшен","Комедия"} };
   settings.Save(file); var loaded=Load(file);
   if(loaded.Sort!=4 || loaded.Status!="completed" || loaded.Search!=settings.Search || loaded.MatchAll || !loaded.Genres.SequenceEqual(settings.Genres)) throw new Exception("View preferences restart round-trip");
   settings.Sort=2; settings.MatchAll=true; settings.Save(file); if(Load(file).Sort!=2 || !Load(file).MatchAll) throw new Exception("View preferences update");
   File.WriteAllText(file,"invalid XML"); if(Load(file).Sort!=4) throw new Exception("View preferences backup recovery");
   File.Delete(file); File.Delete(file+".bak"); if(Load(file).Sort!=0 || !Load(file).MatchAll || Load(file).Genres.Count!=0) throw new Exception("View preferences defaults");
  } finally { foreach(string path in new[]{file,file+".bak",file+".tmp"}) if(File.Exists(path)) File.Delete(path); }
 }
}
}
