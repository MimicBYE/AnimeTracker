using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
namespace AnimeTracker {
public static partial class Catalog {
 static readonly SemaphoreSlim CoverGate=new SemaphoreSlim(3,3);
 static readonly System.Collections.Concurrent.ConcurrentDictionary<int,BitmapSource> Images=new System.Collections.Concurrent.ConcurrentDictionary<int,BitmapSource>();
 static BitmapSource Decode(byte[] bytes) {
  using(var stream=new MemoryStream(bytes)) { var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth=240; bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap; }
 }
 public static async Task<BitmapSource> Cover(int id,string url,CancellationToken token) {
  if(id<=0) return null;
  BitmapSource known; if(Images.TryGetValue(id,out known)) return known;
  await CoverGate.WaitAsync(token);
  try {
   string path=CoverPath(id);
   if(File.Exists(path)) { try { var cached=await Task.Run(()=>Decode(File.ReadAllBytes(path)),token); Images[id]=cached; return cached; } catch(OperationCanceledException) { throw; } catch {} }
   // Modern posters may use WebP. WIC codecs vary; the legacy JPEG is a fallback.
   foreach(string candidate in new[]{url,Origin+"/system/animes/original/"+id+".jpg"}.Distinct()) {
    Uri uri; if(!Uri.TryCreate(candidate,UriKind.Absolute,out uri) || uri.Scheme!="https" || !(uri.Host=="shikimori.io" || uri.Host.EndsWith(".shikimori.io") || uri.Host=="shikimori.one" || uri.Host.EndsWith(".shikimori.one"))) continue;
    try {
     using(var response=await Client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token)) {
      response.EnsureSuccessStatusCode();
      if(response.Content.Headers.ContentLength>4*1024*1024) continue;
      byte[] bytes;
      using(var input=await response.Content.ReadAsStreamAsync()) using(var output=new MemoryStream()) {
       byte[] buffer=new byte[8192]; int n;
       while((n=await input.ReadAsync(buffer,0,buffer.Length,token))>0) { if(output.Length+n>4*1024*1024) throw new InvalidDataException("Cover too large"); output.Write(buffer,0,n); }
       bytes=output.ToArray();
      }
      var bitmap=await Task.Run(()=>Decode(bytes),token);
      try { Directory.CreateDirectory(Path.GetDirectoryName(path)); var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using(var output=new MemoryStream()) { png.Save(output); File.WriteAllBytes(path,output.ToArray()); } } catch(IOException) {} catch(UnauthorizedAccessException) {}
      Images[id]=bitmap; return bitmap;
     }
    } catch(OperationCanceledException) { if(token.IsCancellationRequested) throw; } catch(HttpRequestException) {} catch(NotSupportedException) {} catch(System.Runtime.InteropServices.COMException) {} catch(FileFormatException) {} catch(IOException) {}
   }
   return null;
  } finally { CoverGate.Release(); }
 }

}
}