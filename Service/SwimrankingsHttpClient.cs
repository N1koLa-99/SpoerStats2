using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SpoerStats2.Repository;

namespace SpoerStats2.Service
{
    public class SwimrankingsHttpClient : ISwimrankingsHttp
    {
        private readonly HttpClient _http;
        private readonly ILogger<SwimrankingsHttpClient> _logger;

        public SwimrankingsHttpClient(HttpClient http, ILogger<SwimrankingsHttpClient> logger)
        {
            _http = http;
            _logger = logger;

            // User-Agent е задължителен
            _http.DefaultRequestHeaders.UserAgent.TryParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36");

            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9,bg;q=0.8");
        }

        public Task<string> GetStringAsync(string url, CancellationToken ct = default)
            => GetStringAsync(url, ct, referer: null, ajax: false);

        public async Task<string> GetStringAsync(string url, CancellationToken ct, string? referer, bool ajax)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(referer))
                req.Headers.Referrer = new Uri(referer);

            if (ajax)
                req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            return await resp.Content.ReadAsStringAsync(ct);
        }
    }
}
