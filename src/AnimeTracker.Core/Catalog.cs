using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace AnimeTracker {
public class CatalogEntry {
 public int Id, MalId, Episodes, EpisodesAired, Duration;
 public List<CatalogEntry> Prequels;
 public string AiringStatus="";
 public double Score;
 public DateTimeOffset? NextEpisodeAt;
 public string Genres="", Description="";
 public string Russian="", English="", Japanese="", Romaji="", CoverUrl="", Year="", Kind="";
 public string Title { get { return !String.IsNullOrWhiteSpace(Russian)?Russian:!String.IsNullOrWhiteSpace(English)?English:Romaji; } }
 public string Names { get { return String.Join(" · ",new[]{English,Japanese,Romaji}.Where(s=>!String.IsNullOrWhiteSpace(s)).Distinct()); } }
 public string Info { get { return Year+" · "+Kind+" · "+(Episodes>0?Episodes+" серий":"число серий неизвестно"); } }
}
public static partial class Catalog {
 const string Origin="https://shikimori.io";
 static readonly HttpClient Client=CreateClient();
 static readonly SemaphoreSlim SearchGate=new SemaphoreSlim(1,1);
 static DateTime lastSearch=DateTime.MinValue;
 static HttpClient CreateClient() { 
#if !MODERN_DOTNET
ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
#endif
 var c=new HttpClient(); c.Timeout=TimeSpan.FromSeconds(15); c.DefaultRequestHeaders.UserAgent.ParseAdd("AnimeTrackerDesktop/1.0"); return c; }
 static string Str(IDictionary<string,object> d,string key) { object v; return d!=null && d.TryGetValue(key,out v) && v!=null?Convert.ToString(v):""; }
 static IDictionary<string,object> Obj(IDictionary<string,object> d,string key) { object v; return d!=null && d.TryGetValue(key,out v)?v as IDictionary<string,object>:null; }
 public static List<CatalogEntry> Parse(string json) {
  var root=new JsonCodec().DeserializeObject(json) as IDictionary<string,object>;
  if(root==null || root.ContainsKey("errors")) throw new InvalidDataException("Каталог вернул ошибку. Попробуйте позже.");
  var data=Obj(root,"data"); object raw;
  if(data==null || !data.TryGetValue("animes",out raw) || !(raw is IEnumerable)) throw new InvalidDataException("Неверный ответ каталога.");
  var result=new List<CatalogEntry>();
  foreach(var item in (IEnumerable)raw) {
   var d=item as IDictionary<string,object>; if(d==null) continue;
   int id,episodes; if(!Int32.TryParse(Str(d,"id"),out id) || id<=0) continue; Int32.TryParse(Str(d,"episodes"),out episodes);
   var e=new CatalogEntry { Id=id, Episodes=Math.Max(0,episodes), Russian=Str(d,"russian"), English=Str(d,"english"), Japanese=Str(d,"japanese"), Romaji=Str(d,"name"), CoverUrl=Str(Obj(d,"poster"),"mainUrl"), Year=Str(Obj(d,"airedOn"),"year"), Kind=Str(d,"kind") };
   e.Description=System.Text.RegularExpressions.Regex.Replace(Str(d,"description"),@"\[/?[^\]]+\]","");
   Int32.TryParse(Str(d,"malId"),out e.MalId); Int32.TryParse(Str(d,"duration"),out e.Duration);
   DateTimeOffset next; if(DateTimeOffset.TryParse(Str(d,"nextEpisodeAt"),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out next)) e.NextEpisodeAt=next;
   object score; if(d.TryGetValue("score",out score) && score!=null) Double.TryParse(Convert.ToString(score,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out e.Score);
   Int32.TryParse(Str(d,"episodesAired"),out e.EpisodesAired); e.AiringStatus=Str(d,"status");
   object genres; if(d.TryGetValue("genres",out genres) && genres is IEnumerable) e.Genres=String.Join("|",((IEnumerable)genres).Cast<object>().Select(x=>x as IDictionary<string,object>).Where(x=>x!=null).Select(x=>String.IsNullOrWhiteSpace(Str(x,"russian"))?Str(x,"name"):Str(x,"russian")).Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct());
   object related;
   if(d.TryGetValue("related",out related)) {
    if(!(related is IEnumerable))throw new InvalidDataException("Не удалось проверить предыдущие сезоны.");
    var previous=new List<object>();bool known=true;
    foreach(var relation in (IEnumerable)related){var link=relation as IDictionary<string,object>;if(link==null)throw new InvalidDataException("Неверная связь аниме.");if(Str(link,"relationKind")!="prequel")continue;var target=Obj(link,"anime");if(target==null){known=false;continue;}previous.Add(target);}
    e.Prequels=Parse(new JsonCodec().Serialize(new {data=new {animes=previous}}));
    if(!known||e.Prequels.Count!=previous.Count)e.Prequels=null;
   }
   if(!String.IsNullOrWhiteSpace(e.Title)) result.Add(e);
  } return result;
 }
 public static async Task<List<CatalogEntry>> Search(string query,CancellationToken token) {
  query=query.Trim(); if(query.Length<2) return new List<CatalogEntry>();
  await SearchGate.WaitAsync(token);
  try {
   int wait=(int)(lastSearch.AddMilliseconds(750)-DateTime.UtcNow).TotalMilliseconds; if(wait>0) await Task.Delay(wait,token);
   lastSearch=DateTime.UtcNow;
   var serializer=new JsonCodec();
   var body=serializer.Serialize(new { query="query($search: String!) { animes(search: $search, limit: 12) { id malId name russian english japanese episodes episodesAired status nextEpisodeAt kind duration genres { name russian } airedOn { year } poster { mainUrl } } }", variables=new { search=query } });
   using(var request=new HttpRequestMessage(HttpMethod.Post,Origin+"/api/graphql")) {
    request.Content=new StringContent(body,Encoding.UTF8,"application/json");
    using(var response=await Client.SendAsync(request,token)) {
     if((int)response.StatusCode==429) throw new InvalidOperationException("Каталог просит подождать. Повторите поиск через минуту.");
     response.EnsureSuccessStatusCode(); return Parse(await response.Content.ReadAsStringAsync());
    }
   }
  } finally { SearchGate.Release(); }
 }
 public static async Task<List<CatalogEntry>> ByIds(IEnumerable<int> ids,CancellationToken token,bool details=false) {
  var list=ids.Where(x=>x>0).Distinct().ToArray(); if(list.Length==0 || list.Length>50) throw new ArgumentException("1–50 IDs required");
  await SearchGate.WaitAsync(token);
  try {
   int wait=(int)(lastSearch.AddMilliseconds(750)-DateTime.UtcNow).TotalMilliseconds; if(wait>0) await Task.Delay(wait,token); lastSearch=DateTime.UtcNow;
   var body=new JsonCodec().Serialize(new { query="query($ids: String!) { animes(ids: $ids, limit: 50, censored: false) { "+(details?"description ":"")+"id malId name russian english japanese episodes episodesAired status nextEpisodeAt kind duration genres { name russian } airedOn { year } poster { mainUrl } } }", variables=new { ids=String.Join(",",list) } });
   using(var response=await Client.PostAsync(Origin+"/api/graphql",new StringContent(body,Encoding.UTF8,"application/json"),token)) { response.EnsureSuccessStatusCode(); return Parse(await response.Content.ReadAsStringAsync()); }
  } finally { SearchGate.Release(); }
 }
 public static Dictionary<int,List<CatalogEntry>> ParseSequels(string json) {return ParseRelations(json,"sequel");}
 public static Dictionary<int,List<CatalogEntry>> ParseRelations(string json,string direction) {
  var serializer=new JsonCodec();
  var root=serializer.DeserializeObject(json) as IDictionary<string,object>;
  if(root==null || root.ContainsKey("errors")) throw new InvalidDataException("Ошибка связей каталога.");
  var data=Obj(root,"data"); object raw;
  if(data==null || !data.TryGetValue("animes",out raw) || !(raw is IEnumerable)) throw new InvalidDataException("Неверный ответ каталога.");
  var result=new Dictionary<int,List<CatalogEntry>>();
  foreach(var value in (IEnumerable)raw) {
   var anime=value as IDictionary<string,object>; int id; if(!Int32.TryParse(Str(anime,"id"),out id)) continue;
   object related; if(!anime.TryGetValue("related",out related) || !(related is IEnumerable)) throw new InvalidDataException("Отсутствуют связи аниме.");
   var targets=new List<object>();
   foreach(var item in (IEnumerable)related) { var link=item as IDictionary<string,object>; var target=Obj(link,"anime"); if(Str(link,"relationKind")==direction){if(target==null){if(direction=="prequel")throw new InvalidDataException("Не удалось определить связь аниме.");continue;}targets.Add(target);} }
   result[id]=Parse(serializer.Serialize(new { data=new { animes=targets } }));if(result[id].Count!=targets.Count)throw new InvalidDataException("Неполные связи аниме.");
  }
  return result;
 }
 public static Task<Dictionary<int,List<CatalogEntry>>> Sequels(IEnumerable<int> ids,CancellationToken token) {return Relations(ids,"sequel",token);}
 public static async Task<Dictionary<int,List<CatalogEntry>>> Relations(IEnumerable<int> ids,string direction,CancellationToken token) {
  var list=ids.Where(x=>x>0).Distinct().ToArray(); if(list.Length==0 || list.Length>10) throw new ArgumentException("1–10 source IDs required");
  await SearchGate.WaitAsync(token);
  try {
   int wait=(int)(lastSearch.AddMilliseconds(750)-DateTime.UtcNow).TotalMilliseconds; if(wait>0) await Task.Delay(wait,token); lastSearch=DateTime.UtcNow;
   var body=new JsonCodec().Serialize(new { query="query($ids: String!) { animes(ids: $ids, limit: 10, censored: false) { id related { relationKind anime { id malId name russian english japanese episodes episodesAired status nextEpisodeAt kind duration genres { name russian } airedOn { year } poster { mainUrl } } } } }", variables=new { ids=String.Join(",",list) } });
   using(var response=await Client.PostAsync(Origin+"/api/graphql",new StringContent(body,Encoding.UTF8,"application/json"),token)) { response.EnsureSuccessStatusCode(); return ParseRelations(await response.Content.ReadAsStringAsync(),direction); }
  } finally { SearchGate.Release(); }
 }
 public static async Task<List<CatalogEntry>> Top(int page,CancellationToken token) {
  if(page<1) throw new ArgumentOutOfRangeException("page");
  await SearchGate.WaitAsync(token);
  try {
   int wait=(int)(lastSearch.AddMilliseconds(750)-DateTime.UtcNow).TotalMilliseconds; if(wait>0) await Task.Delay(wait,token); lastSearch=DateTime.UtcNow;
   var body=new JsonCodec().Serialize(new { query="query($page: PositiveInt!) { animes(page: $page, limit: 50, order: ranked) { related { relationKind anime { id malId name russian english japanese } } id malId name russian english japanese score episodes episodesAired status nextEpisodeAt kind duration genres { name russian } airedOn { year } poster { mainUrl } } }", variables=new { page=page } });
   using(var response=await Client.PostAsync(Origin+"/api/graphql",new StringContent(body,Encoding.UTF8,"application/json"),token)) { response.EnsureSuccessStatusCode(); return Parse(await response.Content.ReadAsStringAsync()); }
  } finally { SearchGate.Release(); }
 }
 public static bool Matches(Anime a,string query) { return new[]{a.Title,a.English,a.Japanese,a.Romaji}.Any(s=>(s??"").IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0); }
}
}
