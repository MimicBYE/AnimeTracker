using System;
using System.Collections.Generic;
using System.Linq;

namespace AnimeTracker {
public static class LibraryView {
 public static readonly string[] SortLabels={"Недавно обновлённые","Название: А → Я","Название: Я → А","Всего серий: по возрастанию","Всего серий: по убыванию","Просмотрено: по возрастанию","Просмотрено: по убыванию","Статус","Моя оценка: по убыванию","Моя оценка: по возрастанию"};
 public static string[] Genres(Anime a) { return (a.Genres??"").Split(new[]{'|'},StringSplitOptions.RemoveEmptyEntries); }
 public static List<Anime> Select(IEnumerable<Anime> all,string query,string status,string genre,int sort) {
  return Select(all,query,status,String.IsNullOrEmpty(genre)?new string[0]:new[]{genre},true,sort);
 }
 public static List<Anime> Select(IEnumerable<Anime> all,string query,string status,IEnumerable<string> genres,bool matchAll,int sort) {
  var chosen=genres.Distinct().ToArray();
  var filtered=all.Where(a=>Catalog.Matches(a,query??"") && (String.IsNullOrEmpty(status) || a.Status==status) && (chosen.Length==0 || (matchAll?chosen.All(g=>g=="Без жанров"?Genres(a).Length==0:Genres(a).Contains(g)):chosen.Any(g=>g=="Без жанров"?Genres(a).Length==0:Genres(a).Contains(g)))));
  IOrderedEnumerable<Anime> ordered;
  switch(sort) {
   case 1: ordered=filtered.OrderBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase); break;
   case 2: ordered=filtered.OrderByDescending(a=>a.Title,StringComparer.CurrentCultureIgnoreCase); break;
   case 3: ordered=filtered.OrderBy(a=>a.Total==0).ThenBy(a=>a.Total); break;
   case 4: ordered=filtered.OrderBy(a=>a.Total==0).ThenByDescending(a=>a.Total); break;
   case 5: ordered=filtered.OrderBy(a=>a.Watched); break;
   case 6: ordered=filtered.OrderByDescending(a=>a.Watched); break;
   case 7: ordered=filtered.OrderBy(a=>Array.IndexOf(MalImport.StatusKeys,a.Status)); break;
   case 8: ordered=filtered.OrderBy(a=>a.Score==0).ThenByDescending(a=>a.Score); break;
   case 9: ordered=filtered.OrderBy(a=>a.Score==0).ThenBy(a=>a.Score); break;
   default: ordered=filtered.OrderByDescending(a=>a.Updated); break;
  }
  return ordered.ThenBy(a=>a.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(a=>a.Id).ToList();
 }
 public static void Test() {
  var a=new Anime { Title="Б",Total=12,Watched=3,Status="watching",Genres="Экшен|Комедия" };
  var b=new Anime { Title="А",Total=24,Watched=24,Status="completed",Genres="Комедия" };
  var c=new Anime { Title="В",Total=0,Watched=8,Status="planned" };
  var all=new[]{a,b,c};
  if(Select(all,"","",new[]{"Экшен","Комедия"},true,0).Single()!=a || Select(all,"","",new[]{"Экшен","Комедия"},false,0).Count!=2 || Select(all,"","completed",new[]{"Экшен","Комедия"},true,0).Count!=0 || Select(all,"","",new string[0],true,0).Count!=3) throw new Exception("Multiple genre filters");
  if(Select(all,"","","",1)[0]!=b || Select(all,"","","",2)[0]!=c || Select(all,"","","",3)[0]!=a || Select(all,"","","",4)[0]!=b || Select(all,"","","",3).Last()!=c || Select(all,"","","",4).Last()!=c || Select(all,"","","",5)[0]!=a || Select(all,"","","",6)[0]!=b || Select(all,"","","",7)[0]!=a || Select(all,"","completed","Комедия",0).Single()!=b || Select(all,"Б","","Экшен",0).Single()!=a || Select(all,"","","Без жанров",0).Single()!=c) throw new Exception("Sort/filter test failed");
 }
}
}
