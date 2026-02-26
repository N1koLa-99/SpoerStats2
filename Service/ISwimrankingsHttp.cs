using System.Threading;
using System.Threading.Tasks;

namespace SpoerStats2.Repository
{
    public interface ISwimrankingsHttp
    {
        Task<string> GetStringAsync(string url, CancellationToken ct = default);

        // NEW: за AJAX/SEARCH заявки + Referer (Swimrankings понякога връща празно без това)
        Task<string> GetStringAsync(string url, CancellationToken ct, string? referer, bool ajax);
    }
}
