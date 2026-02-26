using SpoerStats2.Models;
using SpoerStats2.Repository;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SpoerStats2.Service
{
    public class SwimrankingsService
    {
        private const string BaseUrl = "https://www.swimrankings.net/";
        private readonly IDisciplineRepository _disciplineRepo;
        private readonly IUserRepository _userRepo;
        private readonly ISwimrankingsHttp _http;
        private readonly ILogger<SwimrankingsService> _logger;

        private static readonly Regex HrefRegex =
            new Regex(@"href\s*=\s*['""](?<href>[^'""]+)['""]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AnchorRegex =
            new Regex(@"<a\b[^>]*href\s*=\s*['""](?<href>[^'""]+)['""][^>]*>(?<text>.*?)</a>",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex MeetIdRegex =
            new Regex(@"meetId=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ClubIdRegex =
            new Regex(@"clubId=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AthleteIdRegex =
            new Regex(@"athleteId=(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // NEW: хваща и JSON стил: "athleteId":"123" или athleteId:123
        private static readonly Regex AthleteIdAnyRegex =
            new Regex(@"athleteId\s*[:=]\s*[""']?(?<id>\d+)[""']?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TdRegex =
            new Regex(@"<td\b[^>]*>(?<v>.*?)</td>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex TrRegex =
            new Regex(@"<tr\b[^>]*>(?<row>.*?)</tr>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex HtmlTagRegex =
            new Regex(@"<[^>]+>", RegexOptions.Compiled);

        private static readonly Regex TimeRegex =
            new Regex(@"^\s*(\d+:\d{2}\.\d{2}|\d{1,2}\.\d{2})\s*$", RegexOptions.Compiled);

        private static readonly Regex PoolRegex =
            new Regex(@"\b(25|50)\s*m\b|\b(25|50)m\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DateRegex =
            new Regex(@"\b\d{1,2}\s+[A-Za-z]{3,}\s+\d{4}\b", RegexOptions.Compiled);

        private static readonly Regex YearRegex =
            new Regex(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);

        private static readonly Regex H1H2Regex =
            new Regex(@"<h[12][^>]*>(?<t>.*?)</h[12]>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex TitleRegex =
            new Regex(@"<title[^>]*>(?<t>.*?)</title>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public SwimrankingsService(IDisciplineRepository disciplineRepo, IUserRepository userRepo, ISwimrankingsHttp http)
            : this(disciplineRepo, userRepo, http, NullLogger<SwimrankingsService>.Instance)
        {
        }

        public SwimrankingsService(
            IDisciplineRepository disciplineRepo,
            IUserRepository userRepo,
            ISwimrankingsHttp http,
            ILogger<SwimrankingsService> logger)
        {
            _disciplineRepo = disciplineRepo ?? throw new ArgumentNullException(nameof(disciplineRepo));
            _userRepo = userRepo ?? throw new ArgumentNullException(nameof(userRepo));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger ?? NullLogger<SwimrankingsService>.Instance;
        }

        // ======= ключ за дедуп =======
        public static string ResultKey(Result r)
            => $"{r.UserId}|{r.DisciplineId}|{r.SwimmingPoolStandart}|{r.ResultDate:yyyy-MM-dd}|{r.ValueTime:0.00}";

        // ==========================================================
        // MAIN: new meets only (cachedMeetIds already contains processed)
        // ==========================================================
        public async Task<(List<Result> Results, HashSet<string> ProcessedMeetIds)> ImportAthleteAllResultsNewMeets(
            int userId,
            HashSet<string> cachedMeetIds,
            CancellationToken ct)
        {
            var processedMeetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var results = new List<Result>();

            var user = await _userRepo.GetUserById(userId);
            if (user == null) return (results, processedMeetIds);

            // 1) athleteId
            var athleteId = await EnsureAthleteId(user, ct);
            if (string.IsNullOrWhiteSpace(athleteId))
                return (results, processedMeetIds);

            // имена за fallback (ако секцията няма athleteId линкове по някоя страница)
            var lnLat = HasCyrillic(user.LastName ?? "") ? BgToLatin((user.LastName ?? "").Trim()) : (user.LastName ?? "").Trim();
            var fnLat = HasCyrillic(user.FirstName ?? "") ? BgToLatin((user.FirstName ?? "").Trim()) : (user.FirstName ?? "").Trim();

            // 2) athleteDetail page (lists meets)
            var athleteDetailUrl = $"{BaseUrl}index.php?page=athleteDetail&athleteId={athleteId}";
            var athleteDetailHtml = await _http.GetStringAsync(athleteDetailUrl, ct);
            var decoded = WebUtility.HtmlDecode(athleteDetailHtml ?? "");

            // 3) extract meet links
            var meetLinks = ExtractMeetLinks(decoded)
                .GroupBy(x => x.MeetId)
                .Select(g => g.First())
                .ToList();

            var newMeetLinks = meetLinks
                .Where(x => !cachedMeetIds.Contains(x.MeetId))
                .ToList();

            _logger.LogInformation("Swimrankings: athleteId={athleteId}, meetsTotal={total}, meetsNew={newCount}",
                athleteId, meetLinks.Count, newMeetLinks.Count);

            // 4) scrape each new meet (ONLY this athlete)
            foreach (var meet in newMeetLinks)
            {
                ct.ThrowIfCancellationRequested();

                var meetUrl = $"{BaseUrl}index.php?page=meetDetail&meetId={meet.MeetId}&clubId={meet.ClubId}";
                var meetResults = await ScrapeMeetResultsWithFallback(
                    startUrl: meetUrl,
                    meetId: meet.MeetId,
                    userId: userId,
                    athleteId: athleteId,
                    lnLat: lnLat,
                    fnLat: fnLat,
                    ct: ct);

                if (meetResults.Count > 0)
                {
                    results.AddRange(meetResults);
                    processedMeetIds.Add(meet.MeetId);
                }
                else
                {
                    _logger.LogWarning("Swimrankings: meetId={meetId} -> 0 parsed rows (NOT marking as processed). URL={url}",
                        meet.MeetId, meetUrl);
                }
            }

            _logger.LogInformation("Swimrankings: TOTAL parsedResults={count}", results.Count);
            return (results, processedMeetIds);
        }

        // ==========================================================
        // OPTIONAL: PB import (оставям го както е)
        // ==========================================================
        public async Task<List<Result>> ImportAthletePBs(int userId)
        {
            var user = await _userRepo.GetUserById(userId);
            if (user == null) return new List<Result>();

            var athleteId = await EnsureAthleteId(user, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(athleteId)) return new List<Result>();

            var pbUrl = $"{BaseUrl}index.php?page=athleteDetail&athleteId={athleteId}&mode=pb";
            var html = await _http.GetStringAsync(pbUrl, CancellationToken.None);
            var decoded = WebUtility.HtmlDecode(html ?? "");

            var rows = ExtractResultRowsSimple(decoded);
            var list = new List<Result>();

            foreach (var rr in rows)
            {
                var mapped = await MapToResult(rr.Event, rr.Time, rr.Pool, rr.Date, userId, meetId: null, meetLabel: "PB");
                if (mapped != null) list.Add(mapped);
            }

            return list;
        }

        // ==========================================================
        // MAP
        // ==========================================================
        public Task<Result?> MapToResult(string eventName, string time, string pool, string dateStr, int userId)
            => MapToResult(eventName, time, pool, dateStr, userId, meetId: null, meetLabel: null);

        public async Task<Result?> MapToResult(string eventName, string time, string pool, string dateStr, int userId, string? meetId, string? meetLabel)
        {
            var normalizedEvent = NormalizeEventName(eventName);
            if (string.IsNullOrWhiteSpace(normalizedEvent))
                return null;

            var discipline = await _disciplineRepo.GetByName(normalizedEvent);
            if (discipline == null)
            {
                var alt = CollapseSpaces(normalizedEvent);
                if (!string.Equals(alt, normalizedEvent, StringComparison.Ordinal))
                    discipline = await _disciplineRepo.GetByName(alt);
            }

            if (discipline == null)
            {
                _logger.LogWarning("Swimrankings: UNKNOWN discipline '{eventName}' (normalized='{normalized}')", eventName, normalizedEvent);
                return null;
            }

            var seconds = ParseTimeToSeconds(time);
            if (seconds <= 0) return null;

            var poolStd = ParsePool(pool);
            var date = ParseDate(dateStr);
            if (date == default) return null;

            var label = !string.IsNullOrWhiteSpace(meetLabel) ? meetLabel : "Swimrankings";
            var loc = $"Swimrankings Import - {label}";
            if (!string.IsNullOrWhiteSpace(meetId))
                loc += $" (meetId={meetId})";

            return new Result
            {
                UserId = userId,
                DisciplineId = discipline.Id,
                SwimmingPoolStandart = poolStd,
                ResultDate = date,
                ValueTime = (decimal)Math.Round(seconds, 2),
                Location = loc
            };
        }

        // ==========================================================
        // INTERNAL: athleteId resolve (по-здраво + seed + ajax)
        // ==========================================================
        private async Task<string?> EnsureAthleteId(User user, CancellationToken ct)
        {
            var current = user.SwimrankingsId?.Trim();
            if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, "NONE", StringComparison.OrdinalIgnoreCase))
                return current;

            var lnRaw = (user.LastName ?? "").Trim();
            var fnRaw = (user.FirstName ?? "").Trim();

            var lnLat = HasCyrillic(lnRaw) ? BgToLatin(lnRaw) : lnRaw;
            var fnLat = HasCyrillic(fnRaw) ? BgToLatin(fnRaw) : fnRaw;

            var birthYear = TryGetBirthYear(user);

            // 0) seed страница (cookies/session)
            var seedUrl = $"{BaseUrl}index.php?page=athleteSelect";
            _ = await SafeGet(seedUrl, ct, referer: null, ajax: false);

            // 1) пробваме няколко варианта на SEARCH (по наблюдения: сайтът е капризен)
            var urlsToTry = BuildAthleteSearchUrls(lnLat, fnLat)
                .Concat(BuildAthleteSearchUrls(lnRaw, fnRaw))
                .Distinct()
                .ToList();

            foreach (var url in urlsToTry)
            {
                ct.ThrowIfCancellationRequested();

                var isAjax = url.Contains("selectPage=SEARCH", StringComparison.OrdinalIgnoreCase);
                var raw = await SafeGet(url, ct, referer: seedUrl, ajax: isAjax);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var decoded = WebUtility.HtmlDecode(raw);

                // ако страницата не съдържа нито фамилия, нито athleteId – няма смисъл
                var looksRelevant =
                    decoded.Contains(lnLat, StringComparison.OrdinalIgnoreCase) ||
                    decoded.Contains(lnRaw, StringComparison.OrdinalIgnoreCase) ||
                    AthleteIdAnyRegex.IsMatch(decoded);

                if (!looksRelevant) continue;

                // A) HTML кандидати (линкове athleteId=)
                var candidates = ParseCandidatesFromHtml(decoded);

                // B) ако HTML парса не дава – вади athleteId от каквото има (вкл. JSON)
                if (candidates.Count == 0)
                {
                    var ids = ExtractAthleteIds(decoded);
                    candidates = ids.Select(id => (AthleteId: id, RowText: "", AnchorText: "")).ToList();
                }

                if (candidates.Count == 0) continue;

                // ако са много и нямаме никакво name match, това е 99% дефолтна листа (твоя проблем)
                var hasNameSignal =
                    decoded.Contains(lnLat, StringComparison.OrdinalIgnoreCase) ||
                    decoded.Contains(lnRaw, StringComparison.OrdinalIgnoreCase);

                if (candidates.Count >= 20 && !hasNameSignal)
                    continue;

                var pick = await PickBestCandidate(
                    candidates,
                    lnLat: lnLat,
                    fnLat: fnLat,
                    lnRaw: lnRaw,
                    fnRaw: fnRaw,
                    birthYear: birthYear,
                    ct: ct);

                if (pick != null)
                {
                    await _userRepo.UpdateSwimrankingsId(user.Id, pick.Value.AthleteId);
                    _logger.LogInformation("Swimrankings: Resolved athleteId={athleteId} for userId={userId}", pick.Value.AthleteId, user.Id);
                    return pick.Value.AthleteId;
                }
            }

            _logger.LogWarning("Swimrankings: FAILED to resolve athleteId for userId={userId} ln='{ln}' fn='{fn}' year={year}",
                user.Id, lnRaw, fnRaw, birthYear);

            return null;
        }

        private static IEnumerable<string> BuildAthleteSearchUrls(string lastName, string firstName)
        {
            lastName = (lastName ?? "").Trim();
            firstName = (firstName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(lastName))
                yield break;

            // Variant 1: SEARCH (както в логовете ти)
            yield return $"{BaseUrl}index.php?page=athleteSelect&selectPage=SEARCH&nationId=0&gender=0&athlete_lastname={Uri.EscapeDataString(lastName)}&athlete_firstname={Uri.EscapeDataString(firstName)}";
            yield return $"{BaseUrl}index.php?page=athleteSelect&selectPage=SEARCH&nationId=0&genderId=0&athlete_lastname={Uri.EscapeDataString(lastName)}&athlete_firstname={Uri.EscapeDataString(firstName)}";

            // Variant 2: без firstname (понякога работи по-добре)
            yield return $"{BaseUrl}index.php?page=athleteSelect&selectPage=SEARCH&nationId=0&gender=0&athlete_lastname={Uri.EscapeDataString(lastName)}&athlete_firstname=";

            // Variant 3: old-style (някои инстанции още работят така)
            yield return $"{BaseUrl}index.php?page=athleteSelect&athlete_lastname={Uri.EscapeDataString(lastName)}&athlete_firstname={Uri.EscapeDataString(firstName)}";
        }

        private async Task<string?> SafeGet(string url, CancellationToken ct, string? referer, bool ajax)
        {
            try
            {
                return await _http.GetStringAsync(url, ct, referer, ajax);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Swimrankings: failed GET {url}", url);
                return null;
            }
        }

        private static List<(string AthleteId, string AnchorText, string RowText)> ParseCandidatesFromHtml(string decoded)
        {
            var candidates = new List<(string AthleteId, string AnchorText, string RowText)>();

            foreach (Match tr in TrRegex.Matches(decoded))
            {
                var rowHtml = tr.Groups["row"].Value;
                if (string.IsNullOrWhiteSpace(rowHtml)) continue;

                Match? bestAnchor = null;
                foreach (Match am in AnchorRegex.Matches(rowHtml))
                {
                    var href = am.Groups["href"].Value ?? "";
                    if (href.Contains("athleteId=", StringComparison.OrdinalIgnoreCase))
                    {
                        bestAnchor = am;
                        break;
                    }
                }

                if (bestAnchor == null) continue;

                var href2 = bestAnchor.Groups["href"].Value;
                var idm = AthleteIdRegex.Match(href2);
                if (!idm.Success) continue;

                var athleteId = idm.Groups[1].Value;
                var anchorText = CollapseSpaces(StripTags(WebUtility.HtmlDecode(bestAnchor.Groups["text"].Value ?? ""))).Trim();
                var rowText = CollapseSpaces(StripTags(WebUtility.HtmlDecode(rowHtml))).Trim();

                candidates.Add((athleteId, anchorText, rowText));
            }

            return candidates
                .GroupBy(x => x.AthleteId)
                .Select(g => g.First())
                .ToList();
        }

        private static List<string> ExtractAthleteIds(string decoded)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in AthleteIdAnyRegex.Matches(decoded ?? ""))
            {
                var id = m.Groups["id"].Value;
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }

            return ids.ToList();
        }

        private async Task<(string AthleteId, string? Display)?> PickBestCandidate(
            List<(string AthleteId, string AnchorText, string RowText)> candidates,
            string lnLat,
            string fnLat,
            string lnRaw,
            string fnRaw,
            string? birthYear,
            CancellationToken ct)
        {
            int BaseScore((string AthleteId, string AnchorText, string RowText) c)
            {
                var s = $"{c.AnchorText} {c.RowText}".ToLowerInvariant();

                int score = 0;

                // name match (латиница)
                if (!string.IsNullOrWhiteSpace(lnLat) && s.Contains(lnLat.ToLowerInvariant())) score += 50;
                if (!string.IsNullOrWhiteSpace(fnLat) && s.Contains(fnLat.ToLowerInvariant())) score += 30;

                // name match (кирилица – ако случайно я има)
                if (!string.IsNullOrWhiteSpace(lnRaw) && s.Contains(lnRaw.ToLowerInvariant())) score += 40;
                if (!string.IsNullOrWhiteSpace(fnRaw) && s.Contains(fnRaw.ToLowerInvariant())) score += 20;

                if (!string.IsNullOrWhiteSpace(birthYear) && s.Contains(birthYear)) score += 40;

                return score;
            }

            var scored = candidates
                .Select(c => new { C = c, Score = BaseScore(c) })
                .OrderByDescending(x => x.Score)
                .ToList();

            var topScore = scored.FirstOrDefault()?.Score ?? 0;
            if (topScore <= 0)
                return null;

            var top = scored.Where(x => x.Score == topScore).Select(x => x.C).ToList();

            // ако е 1 – взимаме
            if (top.Count == 1)
                return (top[0].AthleteId, null);

            // иначе валидираме по athleteDetail (до 12 кандидата)
            var detailScores = new List<(string AthleteId, int Score, string? Display)>();

            foreach (var c in top.Take(12))
            {
                ct.ThrowIfCancellationRequested();

                var detailUrl = $"{BaseUrl}index.php?page=athleteDetail&athleteId={c.AthleteId}";
                var html = await SafeGet(detailUrl, ct, referer: null, ajax: false);
                if (string.IsNullOrWhiteSpace(html)) continue;

                var decoded = WebUtility.HtmlDecode(html);
                var display = ExtractAthleteHeader(decoded);

                var ds = 0;
                var dlow = (display ?? "").ToLowerInvariant();

                if (!string.IsNullOrWhiteSpace(lnLat) && dlow.Contains(lnLat.ToLowerInvariant())) ds += 80;
                if (!string.IsNullOrWhiteSpace(fnLat) && dlow.Contains(fnLat.ToLowerInvariant())) ds += 60;

                if (!string.IsNullOrWhiteSpace(lnRaw) && dlow.Contains(lnRaw.ToLowerInvariant())) ds += 40;
                if (!string.IsNullOrWhiteSpace(fnRaw) && dlow.Contains(fnRaw.ToLowerInvariant())) ds += 25;

                if (!string.IsNullOrWhiteSpace(birthYear) && dlow.Contains(birthYear)) ds += 80;

                detailScores.Add((c.AthleteId, ds, display));
            }

            if (detailScores.Count == 0) return null;

            var best = detailScores.OrderByDescending(x => x.Score).First();
            var ties = detailScores.Where(x => x.Score == best.Score).ToList();

            if (ties.Count != 1)
            {
                _logger.LogWarning("Swimrankings: athleteSelect NOT unique after athleteDetail validation. ties={ties}",
                    string.Join(" | ", ties.Select(t => $"{t.AthleteId}:{t.Display}")));
                return null;
            }

            return (best.AthleteId, best.Display);
        }

        private static string? ExtractAthleteHeader(string decodedHtml)
        {
            if (string.IsNullOrWhiteSpace(decodedHtml)) return null;

            foreach (Match m in H1H2Regex.Matches(decodedHtml))
            {
                var t = CollapseSpaces(StripTags(WebUtility.HtmlDecode(m.Groups["t"].Value ?? ""))).Trim();
                if (t.Length < 4) continue;
                return t;
            }

            var mt = TitleRegex.Match(decodedHtml);
            if (mt.Success)
            {
                var t = CollapseSpaces(StripTags(WebUtility.HtmlDecode(mt.Groups["t"].Value ?? ""))).Trim();
                var idx = t.IndexOf(" - ", StringComparison.Ordinal);
                if (idx > 0) t = t.Substring(0, idx).Trim();
                if (t.Length >= 4) return t;
            }

            return null;
        }

        private class MeetLink
        {
            public string MeetId { get; set; } = "";
            public string ClubId { get; set; } = "";
            public string Href { get; set; } = "";
        }

        private static string? TryGetBirthYear(User u)
        {
            try
            {
                var t = u.GetType();

                var p1 = t.GetProperty("BirthYear") ?? t.GetProperty("YearOfBirth");
                if (p1 != null)
                {
                    var v = p1.GetValue(u);
                    if (v != null)
                    {
                        if (v is int i) return i.ToString();
                        var s = Convert.ToString(v);
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }

                var p2 = t.GetProperty("BirthDate") ?? t.GetProperty("DateOfBirth");
                if (p2 != null)
                {
                    var v = p2.GetValue(u);
                    if (v is DateTime dt) return dt.Year.ToString();
                }
            }
            catch { }

            return null;
        }

        // ==========================================================
        // INTERNAL: meet scraping with fallback (ONLY this athlete)
        // ==========================================================
        private async Task<List<Result>> ScrapeMeetResultsWithFallback(
            string startUrl,
            string meetId,
            int userId,
            string athleteId,
            string lnLat,
            string fnLat,
            CancellationToken ct)
        {
            var outList = new List<Result>();

            var queue = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            queue.Enqueue(startUrl);

            while (queue.Count > 0 && visited.Count < 25)
            {
                ct.ThrowIfCancellationRequested();

                var url = NormalizeUrl(queue.Dequeue());
                if (string.IsNullOrWhiteSpace(url) || visited.Contains(url)) continue;
                visited.Add(url);

                string html;
                try
                {
                    html = await _http.GetStringAsync(url, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Swimrankings: failed GET {url}", url);
                    continue;
                }

                var decoded = WebUtility.HtmlDecode(html ?? "");
                var meetTitle = ExtractMeetTitle(decoded) ?? "Swimrankings Meet";

                // 1) parse ONLY this athlete section
                var rows = ExtractMeetResultRowsForAthlete(decoded, athleteId, lnLat, fnLat);

                if (rows.Count > 0)
                {
                    foreach (var rr in rows)
                    {
                        rr.Label = meetTitle;
                        var mapped = await MapToResult(rr.Event, rr.Time, rr.Pool, rr.Date, userId, meetId, meetLabel: rr.Label);
                        if (mapped != null) outList.Add(mapped);
                    }

                    if (outList.Count > 0)
                        return outList;
                }

                // 2) no rows -> follow sublinks (same meetId)
                foreach (var link in ExtractAnchors(decoded))
                {
                    var href = link.Href;
                    if (string.IsNullOrWhiteSpace(href)) continue;

                    if (!href.Contains($"meetId={meetId}", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!(href.Contains("page=meet", StringComparison.OrdinalIgnoreCase) ||
                          href.Contains("meetDetail", StringComparison.OrdinalIgnoreCase) ||
                          href.Contains("result", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var abs = MakeAbsolute(href);
                    if (!visited.Contains(abs))
                        queue.Enqueue(abs);
                }
            }

            return outList;
        }

        // ==========================================================
        // PARSERS
        // ==========================================================
        private static List<MeetLink> ExtractMeetLinks(string decodedHtml)
        {
            var list = new List<MeetLink>();

            foreach (Match m in HrefRegex.Matches(decodedHtml))
            {
                var href = (m.Groups["href"].Value ?? "").Trim();
                if (href.Length == 0) continue;

                if (!href.Contains("page=meetDetail", StringComparison.OrdinalIgnoreCase))
                    continue;

                var meet = MeetIdRegex.Match(href);
                var club = ClubIdRegex.Match(href);
                if (!meet.Success || !club.Success) continue;

                list.Add(new MeetLink
                {
                    MeetId = meet.Groups[1].Value,
                    ClubId = club.Groups[1].Value,
                    Href = href
                });
            }

            return list;
        }

        private class Anchor
        {
            public string Href { get; set; } = "";
            public string Text { get; set; } = "";
        }

        private static List<Anchor> ExtractAnchors(string decodedHtml)
        {
            var list = new List<Anchor>();

            foreach (Match m in AnchorRegex.Matches(decodedHtml))
            {
                var href = m.Groups["href"].Value?.Trim() ?? "";
                var text = StripTags(WebUtility.HtmlDecode(m.Groups["text"].Value ?? ""));
                text = CollapseSpaces(text).Trim();

                if (href.Length == 0) continue;
                list.Add(new Anchor { Href = href, Text = text });
            }

            return list;
        }

        private class RawRow
        {
            public string Event { get; set; } = "";
            public string Time { get; set; } = "";
            public string Pool { get; set; } = "";
            public string Date { get; set; } = "";
            public string? Label { get; set; }
        }

        // NEW: meet pages са групирани по атлет (header row с athleteId, след това редове с дисциплини)
        private static List<RawRow> ExtractMeetResultRowsForAthlete(string decodedHtml, string athleteId, string lnLat, string fnLat)
        {
            var rows = new List<RawRow>();
            bool capture = false;

            var ln = (lnLat ?? "").Trim();
            var fn = (fnLat ?? "").Trim();

            foreach (Match tr in TrRegex.Matches(decodedHtml))
            {
                var rowHtml = tr.Groups["row"].Value;
                if (string.IsNullOrWhiteSpace(rowHtml)) continue;

                // ако редът съдържа athleteId (обикновено header)
                var anyId = AthleteIdAnyRegex.Match(rowHtml);
                if (anyId.Success)
                {
                    var id = anyId.Groups["id"].Value;
                    capture = string.Equals(id, athleteId, StringComparison.OrdinalIgnoreCase);

                    // header ред – не е дисциплина
                    continue;
                }

                // fallback: ако няма athleteId header (рядко), пробвай по име
                if (!capture && !string.IsNullOrWhiteSpace(ln))
                {
                    var rowText = CollapseSpaces(StripTags(WebUtility.HtmlDecode(rowHtml))).Trim();
                    var low = rowText.ToLowerInvariant();

                    if (low.Contains(ln.ToLowerInvariant()) && (string.IsNullOrWhiteSpace(fn) || low.Contains(fn.ToLowerInvariant())))
                        capture = true;
                }

                if (!capture) continue;

                var tds = TdRegex.Matches(rowHtml)
                    .Select(m => CollapseSpaces(StripTags(WebUtility.HtmlDecode(m.Groups["v"].Value ?? ""))).Trim())
                    .Where(s => s.Length > 0)
                    .ToList();

                if (tds.Count < 4) continue;

                var time = tds.FirstOrDefault(x => TimeRegex.IsMatch(x));
                if (string.IsNullOrWhiteSpace(time)) continue;

                var pool = tds.FirstOrDefault(x => PoolRegex.IsMatch(x));
                if (string.IsNullOrWhiteSpace(pool)) continue;

                var date = tds.FirstOrDefault(x => DateRegex.IsMatch(x));
                if (string.IsNullOrWhiteSpace(date)) continue;

                var ev = tds[0];

                rows.Add(new RawRow
                {
                    Event = ev,
                    Time = time,
                    Pool = pool,
                    Date = date
                });
            }

            return rows;
        }

        // старият simple extractor (за PB страници)
        private static List<RawRow> ExtractResultRowsSimple(string decodedHtml)
        {
            var rows = new List<RawRow>();

            foreach (Match tr in TrRegex.Matches(decodedHtml))
            {
                var rowHtml = tr.Groups["row"].Value;
                if (string.IsNullOrWhiteSpace(rowHtml)) continue;

                var tds = TdRegex.Matches(rowHtml)
                    .Select(m => CollapseSpaces(StripTags(WebUtility.HtmlDecode(m.Groups["v"].Value ?? ""))).Trim())
                    .Where(s => s.Length > 0)
                    .ToList();

                if (tds.Count < 4) continue;

                var time = tds.FirstOrDefault(x => TimeRegex.IsMatch(x));
                if (string.IsNullOrWhiteSpace(time)) continue;

                var pool = tds.FirstOrDefault(x => PoolRegex.IsMatch(x));
                if (string.IsNullOrWhiteSpace(pool)) continue;

                var date = tds.FirstOrDefault(x => DateRegex.IsMatch(x));
                if (string.IsNullOrWhiteSpace(date)) continue;

                var ev = tds[0];

                rows.Add(new RawRow
                {
                    Event = ev,
                    Time = time,
                    Pool = pool,
                    Date = date
                });
            }

            return rows;
        }

        // ==========================================================
        // HELPERS
        // ==========================================================
        private static string NormalizeEventName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var s = WebUtility.HtmlDecode(input).Trim();
            s = StripTags(s);
            s = CollapseSpaces(s);

            var idx = s.IndexOf('(');
            if (idx > 0) s = s.Substring(0, idx).Trim();

            s = Regex.Replace(s, @"(\d+)\s*m\b", "$1m", RegexOptions.IgnoreCase);

            return s.Trim();
        }

        private static string StripTags(string s) => HtmlTagRegex.Replace(s, "");

        private static string CollapseSpaces(string s)
            => Regex.Replace(s ?? "", @"\s+", " ").Trim();

        private static double ParseTimeToSeconds(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0;

            input = input.Trim();

            if (input.Contains(":"))
            {
                var parts = input.Split(':', 2);
                if (parts.Length != 2) return 0;

                if (!int.TryParse(parts[0], out var minutes)) return 0;

                if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var sec))
                {
                    if (!double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out sec))
                        return 0;
                }

                return minutes * 60 + sec;
            }

            if (!double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
            {
                if (!double.TryParse(input.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out seconds))
                    return 0;
            }

            return seconds;
        }

        private static int ParsePool(string pool)
        {
            if (string.IsNullOrWhiteSpace(pool)) return 50;

            var m = PoolRegex.Match(pool);
            if (!m.Success) return 50;

            var v = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return int.TryParse(v, out var n) ? n : 50;
        }

        private static DateTime ParseDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return default;

            dateStr = CollapseSpaces(dateStr);

            var formats = new[]
            {
                "d MMM yyyy",
                "dd MMM yyyy",
                "d MMMM yyyy",
                "dd MMMM yyyy"
            };

            if (DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;

            if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt;

            if (DateTime.TryParse(dateStr, new CultureInfo("en-US"), DateTimeStyles.None, out dt))
                return dt;

            return default;
        }

        private static string MakeAbsolute(string href)
        {
            href = href.Trim();
            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return href;

            if (href.StartsWith("/"))
                return BaseUrl.TrimEnd('/') + href;

            return BaseUrl + href.TrimStart('/');
        }

        private static string NormalizeUrl(string url)
        {
            url = (url ?? "").Trim();
            if (url.Length == 0) return url;
            return url.Replace("&amp;", "&");
        }

        private static string? ExtractMeetTitle(string decodedHtml)
        {
            if (string.IsNullOrWhiteSpace(decodedHtml)) return null;

            foreach (Match m in H1H2Regex.Matches(decodedHtml))
            {
                var t = CollapseSpaces(StripTags(WebUtility.HtmlDecode(m.Groups["t"].Value ?? ""))).Trim();
                if (t.Length < 4) continue;
                if (t.Contains("Swimrankings", StringComparison.OrdinalIgnoreCase)) continue;
                return t;
            }

            var mt = TitleRegex.Match(decodedHtml);
            if (mt.Success)
            {
                var t = CollapseSpaces(StripTags(WebUtility.HtmlDecode(mt.Groups["t"].Value ?? ""))).Trim();
                var idx = t.IndexOf(" - ", StringComparison.Ordinal);
                if (idx > 0) t = t.Substring(0, idx).Trim();
                if (t.Length >= 4) return t;
            }

            return null;
        }

        private static bool HasCyrillic(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (var ch in s)
                if (ch >= 0x0400 && ch <= 0x04FF) return true;
            return false;
        }

        private static string BgToLatin(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;

            var map = new Dictionary<char, string>
            {
                ['А'] = "A",
                ['а'] = "a",
                ['Б'] = "B",
                ['б'] = "b",
                ['В'] = "V",
                ['в'] = "v",
                ['Г'] = "G",
                ['г'] = "g",
                ['Д'] = "D",
                ['д'] = "d",
                ['Е'] = "E",
                ['е'] = "e",
                ['Ж'] = "Zh",
                ['ж'] = "zh",
                ['З'] = "Z",
                ['з'] = "z",
                ['И'] = "I",
                ['и'] = "i",
                ['Й'] = "Y",
                ['й'] = "y",
                ['К'] = "K",
                ['к'] = "k",
                ['Л'] = "L",
                ['л'] = "l",
                ['М'] = "M",
                ['м'] = "m",
                ['Н'] = "N",
                ['н'] = "n",
                ['О'] = "O",
                ['о'] = "o",
                ['П'] = "P",
                ['п'] = "p",
                ['Р'] = "R",
                ['р'] = "r",
                ['С'] = "S",
                ['с'] = "s",
                ['Т'] = "T",
                ['т'] = "t",
                ['У'] = "U",
                ['у'] = "u",
                ['Ф'] = "F",
                ['ф'] = "f",
                ['Х'] = "H",
                ['х'] = "h",
                ['Ц'] = "Ts",
                ['ц'] = "ts",
                ['Ч'] = "Ch",
                ['ч'] = "ch",
                ['Ш'] = "Sh",
                ['ш'] = "sh",
                ['Щ'] = "Sht",
                ['щ'] = "sht",
                ['Ъ'] = "A",
                ['ъ'] = "a",
                ['Ь'] = "Y",
                ['ь'] = "y",
                ['Ю'] = "Yu",
                ['ю'] = "yu",
                ['Я'] = "Ya",
                ['я'] = "ya",
            };

            var sb = new StringBuilder();
            foreach (var ch in s.Trim())
                sb.Append(map.TryGetValue(ch, out var v) ? v : ch.ToString());
            return sb.ToString();
        }
    }
}
