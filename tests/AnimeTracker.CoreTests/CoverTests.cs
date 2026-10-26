using AnimeTracker.Desktop;
using System.Net;
using System.Net.Http.Headers;

static class CoverTests
{
    sealed class Handler:HttpMessageHandler
    {
        public List<string> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            var status=request.RequestUri.AbsolutePath.Contains("old")?HttpStatusCode.NotFound:HttpStatusCode.OK;
            var response=new HttpResponseMessage(status){Content=new ByteArrayContent(new byte[]{1,2,3})};
            response.Content.Headers.ContentType=new MediaTypeHeaderValue("image/webp");return Task.FromResult(response);
        }
    }
    public static async Task Run()
    {
        var handler=new Handler();int resolves=0;var source=new CoverSource((id,token)=>{resolves++;return Task.FromResult("/new-cover.webp");},handler);
        var bytes=await source.Fetch(21,"",CancellationToken.None);if(bytes.Length!=3||resolves!=1||handler.Requests.Single()!="/new-cover.webp")throw new Exception("Missing cover URL fallback");
        await source.Fetch(21,"https://shikimori.io/old.webp",CancellationToken.None);if(resolves!=1||handler.Requests.Last()!="/new-cover.webp")throw new Exception("Resolved cover URL cache");
        await source.Fetch(22,"https://shikimori.io/old.webp",CancellationToken.None);if(resolves!=2||!handler.Requests.Contains("/old.webp"))throw new Exception("Expired cover URL fallback");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();bool stopped=false;try{await source.Fetch(23,"",cancellation.Token);}catch(OperationCanceledException){stopped=true;}if(!stopped||resolves!=2)throw new Exception("Cover cancellation");
        Console.WriteLine("PASS: missing and expired cover URLs, resolved URL reuse, cancellation");
    }
}
