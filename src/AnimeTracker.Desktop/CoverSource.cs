using System.Net.Http;

namespace AnimeTracker.Desktop;

// Transport is separate from bitmap decoding so fallback and size limits can be verified offline.
public sealed class CoverSource
{
    readonly HttpClient client;
    readonly Func<int,CancellationToken,Task<string>> resolve;
    readonly Dictionary<int,string> resolved=[];
    const int Limit=8*1024*1024;
    public CoverSource(Func<int,CancellationToken,Task<string>> resolve,HttpMessageHandler? handler=null)
    {
        this.resolve=resolve;client=handler==null?new HttpClient():new HttpClient(handler);client.Timeout=TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AnimeTrackerDesktop/1.1");
        client.DefaultRequestHeaders.Referrer=new Uri("https://shikimori.io/");
    }
    public async Task<byte[]> Fetch(int id,string url,CancellationToken token)
    {
        if(resolved.TryGetValue(id,out var cached))url=cached;
        if(!string.IsNullOrWhiteSpace(url))
        {
            try{return await Download(url,token);}catch(HttpRequestException){}catch(ArgumentException){}
        }
        token.ThrowIfCancellationRequested();
        var fresh=await resolve(id,token);
        if(string.IsNullOrWhiteSpace(fresh))throw new IOException("В каталоге нет обложки.");
        var bytes=await Download(fresh,token);resolved[id]=fresh;return bytes;
    }
    async Task<byte[]> Download(string url,CancellationToken token)
    {
        if(url.StartsWith('/'))url="https://shikimori.io"+url;
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")throw new ArgumentException("Недопустимая ссылка обложки.");
        using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>Limit)throw new IOException("Обложка слишком большая.");
        var media=response.Content.Headers.ContentType?.MediaType;
        if(media!=null&&!media.StartsWith("image/",StringComparison.OrdinalIgnoreCase))throw new HttpRequestException("Получен ответ без изображения.");
        using var stream=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await stream.ReadAsync(buffer,token))>0){if(output.Length+count>Limit)throw new IOException("Обложка слишком большая.");output.Write(buffer,0,count);}
        return output.ToArray();
    }
}
