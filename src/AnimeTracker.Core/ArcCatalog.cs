using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace AnimeTracker {
public class ArcCatalogEntry {
 public string Source,FlagsSource,Date;
 public List<AnimeArc> Arcs=new List<AnimeArc>();
 public EpisodeFlags Flags=new EpisodeFlags();
}
public static class ArcCatalog {
 public static SortedSet<int> Episodes(string ranges) {
  var result=new SortedSet<int>();
  foreach(var part in (ranges??"").Replace('–','-').Replace('—','-').Split(',')) {
   var ends=part.Trim().Split('-');int first,last;
   if(ends.Length>2||!Int32.TryParse(ends[0],out first)||first<1||first>100000)throw new ArgumentException("Пример диапазонов: 1–12, 15, 18–24.");
   last=first;if(ends.Length==2&&(!Int32.TryParse(ends[1],out last)||last<first||last>100000))throw new ArgumentException("Неверный диапазон серий.");
   for(int n=first;n<=last;n++)if(!result.Add(n))throw new ArgumentException("Диапазоны серий пересекаются.");
  }return result;
 }
 public static string Format(IEnumerable<int> values) {
  var spans=new List<string>();int start=0,last=0;
  foreach(int n in values.Distinct().OrderBy(n=>n)){if(start==0){start=last=n;continue;}if(n==last+1){last=n;continue;}spans.Add(start==last?start.ToString():start+"–"+last);start=last=n;}
  if(start>0)spans.Add(start==last?start.ToString():start+"–"+last);return String.Join(", ",spans);
 }
 public static SortedSet<int> Episodes(AnimeArc arc){return Episodes(String.IsNullOrEmpty(arc.Intervals)?arc.From+"-"+arc.To:arc.Intervals);}
 public static void SetEpisodes(AnimeArc arc,IEnumerable<int> values){arc.Intervals=Format(values);var set=Episodes(arc.Intervals);arc.From=set.Min;arc.To=set.Max;}
 public static ArcCatalogEntry Find(int malId,string animeId) {
  using(var stream=typeof(ArcCatalog).Assembly.GetManifestResourceStream("AnimeTracker.ArcCatalog")) {
   var root=XElement.Load(stream);var entry=root.Elements("anime").SingleOrDefault(x=>(int)x.Attribute("mal")==malId);if(entry==null)return null;
   var result=new ArcCatalogEntry{Source=(string)entry.Attribute("source"),FlagsSource=(string)entry.Attribute("flagsSource"),Date=(string)entry.Attribute("date")};
   foreach(var x in entry.Elements("arc")){var a=new AnimeArc{Id="catalog:"+malId+":"+(string)x.Attribute("key"),AnimeId=animeId,Name=(string)x.Attribute("name"),CatalogKey=malId+":"+(string)x.Attribute("key")};SetEpisodes(a,Episodes((string)x.Attribute("episodes")));ArcData.Validate(a);result.Arcs.Add(a);}
   foreach(var x in entry.Elements("flags"))foreach(int n in Episodes((string)x.Attribute("episodes")))result.Flags.Items.Add(n,(string)x.Attribute("type"));
   return result;
  }
 }
 // Personal records are kept separately. Catalog updates cannot replace them.
 public static List<AnimeArc> Merge(ArcCatalogEntry catalog,List<AnimeArc> personal,string animeId) {
  var own=personal.Where(a=>a.AnimeId==animeId).ToList();var result=own.Where(a=>!a.Hidden).ToList();
  var occupied=new HashSet<int>(result.SelectMany(a=>Episodes(a)));
  if(catalog!=null)foreach(var a in catalog.Arcs){if(own.Any(o=>o.CatalogKey==a.CatalogKey))continue;var available=Episodes(a).Where(n=>!occupied.Contains(n)).ToArray();if(available.Length==0)continue;var copy=new AnimeArc{Id=a.Id,AnimeId=animeId,Name=a.Name,Type=a.Type,CatalogKey=a.CatalogKey};SetEpisodes(copy,available);result.Add(copy);}
  return result.OrderBy(a=>a.From).ToList();
 }
 public static List<AnimeArc> Replace(List<AnimeArc> items,AnimeArc replacement){return items.Where(a=>a.Id!=replacement.Id||a.AnimeId!=replacement.AnimeId).Concat(new[]{replacement}).ToList();}
 public static void Test(string dir) {
  foreach(int id in new[]{20,21,269}){var c=Find(id,"test");int count=id==20?220:id==21?1168:366;if(c.Flags.Items.Count!=count||c.Flags.Items.Keys.Min()!=1||c.Flags.Items.Keys.Max()!=count)throw new Exception("Catalog coverage");var numbers=c.Arcs.SelectMany(a=>Episodes(a)).ToArray();if(numbers.Distinct().Count()!=numbers.Length)throw new Exception("Catalog arc overlap");}
  if(Find(1735,"test")!=null||Find(41467,"test")!=null)throw new Exception("Season mapping");
  var catalog=Find(21,"test");var arc=catalog.Arcs.Single(a=>a.CatalogKey=="21:impel");if(Episodes(arc).Contains(426)||Episodes(arc).Count!=27)throw new Exception("Disjoint arcs");
  var personal=new AnimeArc{AnimeId="test",Name="Моя арка",From=423,To=424,Type="filler"};var merged=Merge(catalog,new List<AnimeArc>{personal},"test");if(merged.Single(a=>a.CatalogKey==arc.CatalogKey).Intervals.Contains("423")||merged.SelectMany(a=>Episodes(a)).Count(n=>n==423)!=1)throw new Exception("Personal precedence");
  var hidden=new AnimeArc{Id=arc.Id,AnimeId="test",Name=arc.Name,From=arc.From,To=arc.To,Intervals=arc.Intervals,CatalogKey=arc.CatalogKey,Hidden=true};var path=System.IO.Path.Combine(dir,"catalog-overrides.xml");ArcData.Save(new List<AnimeArc>{hidden},path);if(Merge(catalog,ArcData.Load(path),"test").Any(a=>a.CatalogKey==arc.CatalogKey))throw new Exception("Hidden catalog arc");
  if(catalog.Flags.Items.ContainsKey(1169))throw new Exception("Unknown future episode");
  bool rejected=false;try{Episodes("1-4,3-6");}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Overlapping input");
 }
}
}
