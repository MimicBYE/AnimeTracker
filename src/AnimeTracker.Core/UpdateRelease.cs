using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace AnimeTracker {
public sealed class UpdateVersion {
 public Version Base;public long Development;
 public static UpdateVersion Parse(string value){
  if(value==null||value.Length>64)throw new InvalidDataException("Неизвестный формат версии.");
  var m=Regex.Match(value??"",@"^(\d+)\.(\d+)(?:\.(\d+))?(?:-dev\.(\d+))?$");if(!m.Success)throw new InvalidDataException("Неизвестный формат версии.");
  try{return new UpdateVersion{Base=new Version(Int32.Parse(m.Groups[1].Value),Int32.Parse(m.Groups[2].Value),m.Groups[3].Success?Int32.Parse(m.Groups[3].Value):0),Development=m.Groups[4].Success?Int64.Parse(m.Groups[4].Value):0};}catch(OverflowException error){throw new InvalidDataException("Слишком большой номер версии.",error);}
 }
}
public sealed class UpdateBuild {
 public string applicationVersion,packageVersion,channel,commit;public int updateProtocol;
 public static UpdateBuild Read(string path){if(new FileInfo(path).Length>65536)throw new InvalidDataException("Слишком большой BUILD-INFO.json.");Dictionary<string,object> d;try{d=UpdateJson.Map(new JsonCodec().DeserializeObject(File.ReadAllText(path)));}catch(Exception error){throw new InvalidDataException("Некорректный BUILD-INFO.json.",error);}var b=new UpdateBuild{applicationVersion=UpdateJson.Text(d,"applicationVersion"),packageVersion=UpdateJson.Text(d,"packageVersion"),channel=UpdateJson.Text(d,"channel"),commit=UpdateJson.Text(d,"commit")};Int32.TryParse(UpdateJson.Text(d,"updateProtocol"),out b.updateProtocol);UpdateVersion.Parse(b.packageVersion);if(!new[]{"stable","dev","local"}.Contains(b.channel)||!Regex.IsMatch(b.commit,@"^(local|[a-f0-9]{40})$"))throw new InvalidDataException("Повреждён BUILD-INFO.json.");return b;}
}
static class UpdateJson {
 internal static DateTime Utc(string value){DateTime result;return DateTime.TryParse(value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AdjustToUniversal|System.Globalization.DateTimeStyles.AssumeUniversal,out result)?result:DateTime.MaxValue;}
 internal static Dictionary<string,object> Map(object value){var d=value as Dictionary<string,object>;if(d==null)throw new InvalidDataException("Некорректный ответ сервера обновлений.");return d;}
 internal static string Text(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)?Convert.ToString(value):"";}
 internal static bool Flag(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)&&value is bool&&(bool)value;}
 internal static object[] List(Dictionary<string,object> d,string key){object value;return d.TryGetValue(key,out value)?value as object[]??new object[0]:new object[0];}
}
public sealed class UpdateRelease {
 public string Version,Notes,Url,AssetName,DownloadUrl,Sha256;public long Size;public DateTime Published;
 public static bool IsDownloadUri(Uri uri){return uri!=null&&uri.Scheme=="https"&&uri.UserInfo.Length==0&&uri.IsDefaultPort&&(uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com");}
 public static List<UpdateRelease> Parse(string json,string platform){
  if(!new[]{"windows-x64","macos-arm64","linux-x64"}.Contains(platform))throw new ArgumentException("Платформа обновления не поддерживается.");
  var output=new List<UpdateRelease>();var rows=new JsonCodec().DeserializeObject(json) as object[];if(rows==null)throw new InvalidDataException("Некорректный список релизов.");
  foreach(var item in rows){var d=UpdateJson.Map(item);if(UpdateJson.Flag(d,"draft"))continue;var tag=UpdateJson.Text(d,"tag_name");if(!tag.StartsWith("v",StringComparison.Ordinal))continue;
   UpdateVersion version;try{version=UpdateVersion.Parse(tag.Substring(1));}catch(Exception e){if(e is FormatException||e is OverflowException||e is InvalidDataException)continue;throw;}
   if(UpdateJson.Flag(d,"prerelease")!=(version.Development>0))continue;
   var suffix=platform=="linux-x64"?".tar.gz":".zip";var name="AnimeTracker-"+tag.Substring(1)+"-"+platform+suffix;
   var assets=UpdateJson.List(d,"assets").Select(UpdateJson.Map).Where(a=>UpdateJson.Text(a,"name")==name).ToArray();if(assets.Length!=1)continue;
   var asset=assets[0];var url="https://github.com/MimicBYE/AnimeTracker/releases/download/"+tag+"/"+name;
   if(UpdateJson.Text(asset,"browser_download_url")!=url)continue;
   var digest=UpdateJson.Text(asset,"digest");if(!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))continue;
   long size;if(!Int64.TryParse(UpdateJson.Text(asset,"size"),out size)||size<=0||size>512L*1024*1024)continue;
   DateTime published;if(!DateTime.TryParse(UpdateJson.Text(d,"published_at"),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AdjustToUniversal|System.Globalization.DateTimeStyles.AssumeUniversal,out published))continue;
   output.Add(new UpdateRelease{Version=tag.Substring(1),Notes=UpdateJson.Text(d,"body"),Url="https://github.com/MimicBYE/AnimeTracker/releases/tag/"+tag,AssetName=name,DownloadUrl=url,Sha256=digest.Substring(7).ToLowerInvariant(),Size=size,Published=published});
  }return output;
 }
 public static UpdateRelease Select(IEnumerable<UpdateRelease> releases,string current,bool development,DateTime currentPublished){
  var before=UpdateVersion.Parse(current);
  var candidates=releases.Where(r=>{var v=UpdateVersion.Parse(r.Version);if(v.Development>0&&!development)return false;int order=v.Base.CompareTo(before.Base);if(order!=0)return order>0;if(r.Version==current)return false;if(before.Development>0)return v.Development>before.Development;return development&&v.Development>0&&r.Published>currentPublished;}).ToArray();
  if(candidates.Length==0)return null;var best=candidates.OrderByDescending(r=>UpdateVersion.Parse(r.Version).Base).First();var same=candidates.Where(r=>UpdateVersion.Parse(r.Version).Base==UpdateVersion.Parse(best.Version).Base);
  var stable=same.Where(r=>UpdateVersion.Parse(r.Version).Development==0).OrderByDescending(r=>r.Published).FirstOrDefault();var dev=same.Where(r=>UpdateVersion.Parse(r.Version).Development>0).OrderByDescending(r=>UpdateVersion.Parse(r.Version).Development).FirstOrDefault();
  return stable!=null&&(dev==null||stable.Published>=dev.Published)?stable:dev;
 }
}
public sealed class UpdateClient:IDisposable {
 readonly HttpClient client;
 public UpdateClient():this(new HttpClientHandler{AllowAutoRedirect=true}){}
 public UpdateClient(HttpMessageHandler handler){client=new HttpClient(handler);client.Timeout=TimeSpan.FromMinutes(10);client.DefaultRequestHeaders.UserAgent.ParseAdd("AnimeTracker-Updater/1.0");client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");}
 public async Task<UpdateRelease> Check(UpdateBuild build,string platform,bool development,CancellationToken token){
  var all=new List<UpdateRelease>();DateTime currentPublished=DateTime.MaxValue;
  for(int page=1;page<=3;page++){
   string json=await Text("https://api.github.com/repos/MimicBYE/AnimeTracker/releases?per_page=100&page="+page,token);var rows=new JsonCodec().DeserializeObject(json) as object[];if(rows==null)throw new InvalidDataException("Некорректный список релизов.");
   foreach(var item in rows){var d=UpdateJson.Map(item);if(UpdateJson.Text(d,"tag_name")=="v"+build.packageVersion)currentPublished=UpdateJson.Utc(UpdateJson.Text(d,"published_at"));}
   all.AddRange(UpdateRelease.Parse(json,platform));if(rows.Length<100)break;
  }
  // A development opt-in on an old stable build needs its publication date, not SemVer alone.
  if(development&&UpdateVersion.Parse(build.packageVersion).Development==0&&currentPublished==DateTime.MaxValue){
   using(var response=await client.GetAsync("https://api.github.com/repos/MimicBYE/AnimeTracker/releases/tags/v"+build.packageVersion,token))if(response.IsSuccessStatusCode){var d=UpdateJson.Map(new JsonCodec().DeserializeObject(await response.Content.ReadAsStringAsync()));currentPublished=UpdateJson.Utc(UpdateJson.Text(d,"published_at"));}
  }
  return UpdateRelease.Select(all,build.packageVersion,development,currentPublished);
 }
 async Task<string> Text(string uri,CancellationToken token){using(var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token)){response.EnsureSuccessStatusCode();using(var stream=await response.Content.ReadAsStreamAsync())using(var output=new MemoryStream()){var buffer=new byte[8192];int read;while((read=await stream.ReadAsync(buffer,0,buffer.Length,token))>0){if(output.Length+read>8*1024*1024)throw new InvalidDataException("Ответ сервера слишком большой.");output.Write(buffer,0,read);}return System.Text.Encoding.UTF8.GetString(output.ToArray());}}}
 public async Task Download(UpdateRelease release,string target,IProgress<double> progress,CancellationToken token){
  Uri uri;if(!Uri.TryCreate(release.DownloadUrl,UriKind.Absolute,out uri)||uri.AbsoluteUri!="https://github.com/MimicBYE/AnimeTracker/releases/download/v"+release.Version+"/"+release.AssetName||!UpdateRelease.IsDownloadUri(uri))throw new InvalidDataException("Недопустимый адрес обновления.");
  bool created=false;try{using(var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token)){
   response.EnsureSuccessStatusCode();if(!UpdateRelease.IsDownloadUri(response.RequestMessage.RequestUri))throw new InvalidDataException("Недопустимое перенаправление загрузки.");
   if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=release.Size)throw new InvalidDataException("Размер обновления не совпадает.");
   using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){created=true;var buffer=new byte[81920];long total=0;int read;while((read=await input.ReadAsync(buffer,0,buffer.Length,token))>0){total+=read;if(total>release.Size)throw new InvalidDataException("Размер обновления превышен.");await output.WriteAsync(buffer,0,read,token);if(progress!=null)progress.Report(100.0*total/release.Size);}if(total!=release.Size)throw new InvalidDataException("Обновление скачано не полностью.");}
  }if(UpdatePackage.Hash(target)!=release.Sha256)throw new InvalidDataException("Контрольная сумма обновления не совпала.");}catch{if(created&&File.Exists(target))File.Delete(target);throw;}
 }
 public void Dispose(){client.Dispose();}
}
}
