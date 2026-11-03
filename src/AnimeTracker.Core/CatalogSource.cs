using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeTracker {
public interface ICatalogSource {
 Task<List<CatalogEntry>> ByIds(IEnumerable<int> ids,CancellationToken token);
}
public sealed class ShikimoriCatalogSource : ICatalogSource {
 public Task<List<CatalogEntry>> ByIds(IEnumerable<int> ids,CancellationToken token) {return Catalog.ByIds(ids,token);}
}
}
