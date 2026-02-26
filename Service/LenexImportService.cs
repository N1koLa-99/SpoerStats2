using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SpoerStats2.Models;
using SpoerStats2.Repository;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SpoerStats2.Service
{
    public class LenexImportService
    {
        private readonly IUserRepository _userRepository;
        private readonly IResultRepository _resultRepository;
        private readonly ClubDisciplineService _clubDisciplineService;
        private readonly ClubService _clubService;
        private readonly ILogger<LenexImportService> _logger;

        private readonly Microsoft.AspNetCore.Identity.PasswordHasher<User> _hasher = new();

        public LenexImportService(
            IUserRepository userRepository,
            IResultRepository resultRepository,
            ClubDisciplineService clubDisciplineService,
            ClubService clubService,
            ILogger<LenexImportService> logger)
        {
            _userRepository = userRepository;
            _resultRepository = resultRepository;
            _clubDisciplineService = clubDisciplineService;
            _clubService = clubService;
            _logger = logger;
        }

        public async Task<LenexImportReport> ImportForClub(IFormFile file, int clubId)
        {
            var report = new LenexImportReport();

            _logger.LogInformation("LENEX ImportForClub started. ClubId={ClubId} File={File} Len={Len}",
                clubId, file?.FileName, file?.Length ?? 0);

            var dbClub = await _clubService.GetClubById(clubId);
            if (dbClub == null) throw new Exception("Невалиден клуб.");

            var doc = LoadXml(file);
            var (location, poolLen, meetDate, eventMap) = BuildMeetContext(doc);

            _logger.LogInformation("LENEX meet context (club): Location='{Location}', PoolLen={PoolLen}, MeetDate={MeetDate}, EventsMapped={Events}",
                location, poolLen, meetDate.ToString("yyyy-MM-dd"), eventMap.Count);

            var xmlClubs = GetXmlClubs(doc);

            var code = (dbClub.Code ?? "").Trim();
            var nation = (dbClub.Nation ?? "").Trim();

            var matching = new List<XmlClub>();

            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(nation))
            {
                matching = xmlClubs
                    .Where(x => SameCodeNation(x.Code, x.Nation, code, nation))
                    .ToList();
            }

            if (matching.Count == 0)
            {
                matching = xmlClubs
                    .Where(x => IsSameClub(x.Name, dbClub.Name))
                    .ToList();
            }

            if (matching.Count == 0)
            {
                report.Skipped_ClubMismatch++;
                AddSample(report, $"Клубът '{dbClub.Name}' (Code={dbClub.Code}, Nation={dbClub.Nation}) не е намерен в XML файла.");
                return report;
            }

            foreach (var xc in matching)
            {
                _logger.LogInformation(
                    "LENEX single-club import: XML club '{XmlName}' (code='{XmlCode}', nation='{XmlNation}') -> DB club '{DbName}' (Id={DbId}, Code='{DbCode}', Nation='{DbNation}')",
                    xc.Name, xc.Code, xc.Nation,
                    dbClub.Name, dbClub.Id, dbClub.Code, dbClub.Nation
                );

                await ProcessXmlClub(
                    clubId: dbClub.Id,
                    dbClub: dbClub,
                    clubEl: xc.Element,
                    poolLen: poolLen,
                    location: location,
                    meetDate: meetDate,
                    eventMap: eventMap,
                    report: report
                );
            }

            return report;
        }

        public async Task<LenexImportReport> ImportForAllClubs(IFormFile file)
        {
            var report = new LenexImportReport();

            _logger.LogInformation("LENEX ImportForAllClubs started. File={File} Len={Len}",
                file?.FileName, file?.Length ?? 0);

            var doc = LoadXml(file);
            var (location, poolLen, meetDate, eventMap) = BuildMeetContext(doc);

            _logger.LogInformation("LENEX meet context (all): Location='{Location}', PoolLen={PoolLen}, MeetDate={MeetDate}, EventsMapped={Events}",
                location, poolLen, meetDate.ToString("yyyy-MM-dd"), eventMap.Count);

            var xmlClubs = GetXmlClubs(doc);

            _logger.LogInformation("LENEX XML clubs found: {Count}", xmlClubs.Count);

            foreach (var xc in xmlClubs)
            {
                Club? dbClub = null;
                var xmlCode = (xc.Code ?? "").Trim();
                var xmlNation = (xc.Nation ?? "").Trim();
                var xmlName = (xc.Name ?? "").Trim();

                string mappedBy = "none";

                // ✅ ВАЖНО: ако XML дава code+nation -> търсим САМО strict match.
                // Ако няма strict match, НЕ падаме на loose name (за да не вкара атлетите в грешен клуб).
                if (!string.IsNullOrWhiteSpace(xmlCode) && !string.IsNullOrWhiteSpace(xmlNation))
                {
                    dbClub = await _clubService.GetClubByCodeNation(xmlCode, xmlNation);
                    mappedBy = "code+nation";

                    if (dbClub == null)
                    {
                        report.Skipped_ClubNotFound++;
                        if (report.ClubNotFoundSample.Count < 30)
                        {
                            report.ClubNotFoundSample.Add(
                                $"STRICT code+nation not found: XML '{xmlName}' (code='{xmlCode}', nation='{xmlNation}')"
                            );
                        }

                        _logger.LogWarning(
                            "LENEX SKIP club (strict mismatch): XML club '{XmlName}' (code='{XmlCode}', nation='{XmlNation}') not found by code+nation. Loose name fallback blocked.",
                            xmlName, xmlCode, xmlNation
                        );

                        continue;
                    }
                }
                else
                {
                    // ✅ fallback по име само когато XML няма code/nation
                    dbClub = await _clubService.FindClubByNameLoose(xmlName);
                    mappedBy = "loose-name";
                }

                if (dbClub == null)
                {
                    report.Skipped_ClubNotFound++;
                    if (report.ClubNotFoundSample.Count < 30)
                        report.ClubNotFoundSample.Add($"XML club not found in DB: '{xc.Name}' (code='{xc.Code}', nation='{xc.Nation}')");

                    _logger.LogWarning(
                        "LENEX SKIP club: XML '{XmlName}' (code='{XmlCode}', nation='{XmlNation}') -> no DB club (mappedBy={MappedBy})",
                        xmlName, xmlCode, xmlNation, mappedBy
                    );

                    continue;
                }

                // ✅ Допълнителна защита: ако е loose-name и прилича съмнително -> лог (не блокираме, защото може да е валидно)
                if (mappedBy == "loose-name" && !IsSameClub(xmlName, dbClub.Name))
                {
                    _logger.LogWarning(
                        "LENEX suspicious loose-name match: XML '{XmlName}' -> DB '{DbName}' (Id={DbId})",
                        xmlName, dbClub.Name, dbClub.Id
                    );
                }

                _logger.LogWarning(
                    "LENEX MAP: XML club '{XmlName}' (code='{XmlCode}', nation='{XmlNation}') -> DB club '{DbName}' (Id={DbId}, Code='{DbCode}', Nation='{DbNation}') via {MappedBy}",
                    xmlName, xmlCode, xmlNation,
                    dbClub.Name, dbClub.Id, dbClub.Code, dbClub.Nation, mappedBy
                );

                await ProcessXmlClub(
                    clubId: dbClub.Id,
                    dbClub: dbClub,
                    clubEl: xc.Element,
                    poolLen: poolLen,
                    location: location,
                    meetDate: meetDate,
                    eventMap: eventMap,
                    report: report
                );
            }

            return report;
        }

        // =========================
        // Core
        // =========================
        private async Task ProcessXmlClub(
            int clubId,
            Club dbClub,
            XElement clubEl,
            int poolLen,
            string location,
            DateTime meetDate,
            Dictionary<int, EventInfo> eventMap,
            LenexImportReport report)
        {
            var athletes = clubEl.Descendants().Where(x => x.Name.LocalName == "ATHLETE").ToList();

            _logger.LogInformation("ProcessXmlClub start: DB club '{ClubName}' (Id={ClubId}), athletesInXml={Athletes}",
                dbClub.Name, clubId, athletes.Count);

            // 1) users in club
            var clubUsers = (await _userRepository.GetUsersByClubId(clubId)).ToList();

            var bySwrid = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in clubUsers)
            {
                if (!string.IsNullOrWhiteSpace(u.Swrid))
                {
                    var k = u.Swrid.Trim();
                    if (!bySwrid.ContainsKey(k))
                        bySwrid[k] = u;
                }
            }

            var byPerson = new Dictionary<string, List<User>>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in clubUsers)
            {
                var fnn = u.FirstNameNorm ?? Norm(u.FirstName);
                var lnn = u.LastNameNorm ?? Norm(u.LastName);
                var g = NormGenderToChar(u.Gender);

                if (string.IsNullOrWhiteSpace(fnn) || string.IsNullOrWhiteSpace(lnn) || u.YearOfBirth <= 0)
                    continue;

                var key = PersonKey(fnn, lnn, u.YearOfBirth, g);
                if (!byPerson.TryGetValue(key, out var list))
                {
                    list = new List<User>();
                    byPerson[key] = list;
                }
                list.Add(u);
            }

            // 2) disciplines for club
            var clubDisciplines = (await _clubDisciplineService.GetDisciplinesByClubId(clubId)).ToList();
            var disciplineByName = clubDisciplines.ToDictionary(
                d => NormalizeDiscipline(d.DisciplineName),
                d => d.Id,
                StringComparer.OrdinalIgnoreCase
            );

            // 3) existing source keys for dedup
            var userIdsNow = clubUsers.Select(u => u.Id).ToList();
            var existingSourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (userIdsNow.Count > 0)
            {
                var keys = await _resultRepository.GetExistingSourceKeys("LENEX", userIdsNow);
                foreach (var k in keys)
                    if (!string.IsNullOrWhiteSpace(k)) existingSourceKeys.Add(k);
            }

            foreach (var athleteEl in athletes)
            {
                var fn = ((string?)athleteEl.Attribute("firstname") ?? "").Trim();
                var ln = ((string?)athleteEl.Attribute("lastname") ?? "").Trim();
                var aid = ((string?)athleteEl.Attribute("athleteid") ?? "").Trim();

                var year = TryGetBirthYear(athleteEl);

                var gXml = ((string?)athleteEl.Attribute("gender") ?? "").Trim();
                var genderChar = NormGender(gXml);

                if (year == null)
                {
                    report.Skipped_NotInApp++;
                    AddSample(report, $"Skip athlete (no birth year): {fn} {ln}, athleteid={aid}, club={dbClub.Name}");
                    continue;
                }

                var swridXml = ExtractSwrid(athleteEl);

                var fnNorm = Norm(fn);
                var lnNorm = Norm(ln);
                var pKey = PersonKey(fnNorm, lnNorm, year.Value, genderChar);

                User? user = null;

                if (!string.IsNullOrWhiteSpace(swridXml) && bySwrid.TryGetValue(swridXml, out var uBySw))
                    user = uBySw;

                if (user == null && byPerson.TryGetValue(pKey, out var candidates) && candidates.Count > 0)
                {
                    if (candidates.Count == 1) user = candidates[0];
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(swridXml))
                        {
                            var exact = candidates.FirstOrDefault(x =>
                                !string.IsNullOrWhiteSpace(x.Swrid) &&
                                string.Equals(x.Swrid, swridXml, StringComparison.OrdinalIgnoreCase));
                            if (exact != null) user = exact;
                        }

                        if (user == null)
                        {
                            report.Skipped_AmbiguousUser++;
                            AddSample(report, $"Ambiguous athlete match: {fn} {ln}, y={year}, gender={genderChar}, swrid={swridXml}, club={dbClub.Name}");
                            continue;
                        }
                    }
                }

                if (user == null)
                {
                    var swridFinal = swridXml;
                    if (string.IsNullOrWhiteSpace(swridFinal))
                        swridFinal = await GenerateUniqueSwrid(dbClub);

                    var newUser = new User
                    {
                        FirstName = fn,
                        LastName = ln,
                        YearOfBirth = year.Value,
                        Gender = genderChar == 'M' ? "Male" : (genderChar == 'F' ? "Female" : "Male"),
                        RoleID = 1,
                        ClubID = clubId,
                        StatusID = 2,

                        Swrid = swridFinal,
                        FirstNameNorm = fnNorm,
                        LastNameNorm = lnNorm,

                        Email = $"{swridFinal}@import.local",
                        profileImage_url = "/profile-pictures/default.png",
                    };

                    var rawPass = Guid.NewGuid().ToString("N");
                    newUser.Password = _hasher.HashPassword(newUser, rawPass);

                    // ✅ Диагностичен лог - виждаш в кой клуб реално тръгва insert-ът
                    _logger.LogWarning(
                        "LENEX INSERT USER: {First} {Last}, y={Year}, swrid={Swrid}, target ClubID={ClubId}, target ClubName={ClubName}",
                        newUser.FirstName, newUser.LastName, newUser.YearOfBirth, newUser.Swrid, newUser.ClubID, dbClub.Name
                    );

                    await _userRepository.AddUser(newUser);
                    report.InsertedAthletes++;

                    clubUsers.Add(newUser);
                    if (!string.IsNullOrWhiteSpace(newUser.Swrid))
                        bySwrid[newUser.Swrid] = newUser;

                    if (!byPerson.TryGetValue(pKey, out var list))
                    {
                        list = new List<User>();
                        byPerson[pKey] = list;
                    }
                    list.Add(newUser);

                    userIdsNow.Add(newUser.Id);
                    user = newUser;
                }
                else
                {
                    if (user.ClubID != clubId)
                    {
                        report.Skipped_NotInApp++;
                        AddSample(report, $"Athlete matched existing user in another club: {fn} {ln}, userClub={user.ClubID}, xmlClub={dbClub.Name}/{clubId}");
                        continue;
                    }
                }

                // ✅ namespace-safe RESULTS/RESULT
                var resultsEl = athleteEl.Elements().FirstOrDefault(x => x.Name.LocalName == "RESULTS");
                if (resultsEl == null) continue;

                foreach (var r in resultsEl.Elements().Where(x => x.Name.LocalName == "RESULT"))
                {
                    int? eventId = TryParseIntAttr(r, "eventid");
                    string? disciplineKey = null;
                    DateTime resultDate = meetDate;

                    if (eventId.HasValue && eventMap.TryGetValue(eventId.Value, out var evInfo))
                    {
                        disciplineKey = evInfo.DisciplineKey;
                        resultDate = evInfo.Date;
                    }
                    else
                    {
                        var swim = r.Elements().FirstOrDefault(x => x.Name.LocalName == "SWIMSTYLE")
                                ?? r.Descendants().FirstOrDefault(x => x.Name.LocalName == "SWIMSTYLE");
                        disciplineKey = swim != null ? BuildDisciplineKeyFromSwimstyle(swim) : null;
                    }

                    if (string.IsNullOrWhiteSpace(disciplineKey))
                    {
                        report.Skipped_UnknownDiscipline++;
                        continue;
                    }

                    if (!TryMapDisciplineId(disciplineKey, disciplineByName, out var disciplineId))
                    {
                        report.Skipped_UnknownDiscipline++;
                        report.UnknownDisciplines.Add(disciplineKey);
                        continue;
                    }

                    var timeStr = ExtractTimeString(r);
                    if (string.IsNullOrWhiteSpace(timeStr) || !TryParseSwimTimeToSeconds(timeStr, out var seconds))
                    {
                        report.Skipped_NoTime++;
                        continue;
                    }

                    var valueTime = Math.Round((decimal)seconds, 2);

                    var sk = BuildLenexSourceKey(
                        clubId: clubId,
                        swrid: user!.Swrid ?? "",
                        eventId: eventId,
                        disciplineId: disciplineId,
                        resultDate: resultDate.Date,
                        poolLen: poolLen,
                        valueTime: valueTime,
                        location: location,
                        resultEl: r
                    );

                    if (existingSourceKeys.Contains(sk))
                    {
                        report.Skipped_Duplicates++;
                        continue;
                    }

                    existingSourceKeys.Add(sk);

                    // ✅ 1) insert result and get ID
                    var newResult = new Result
                    {
                        UserId = user!.Id,
                        DisciplineId = disciplineId,
                        ValueTime = valueTime,
                        ResultDate = resultDate.Date,
                        SwimmingPoolStandart = poolLen,
                        Location = location,
                        Source = "LENEX",
                        SourceKey = sk
                    };

                    var newResultId = await _resultRepository.AddResultReturnId(newResult);
                    report.Inserted++;

                    // ✅ 2) extract + insert splits (ако има)
                    var splits = ExtractSplits(r, newResultId);
                    if (splits.Count > 0)
                        await _resultRepository.AddSplitsBulk(splits);
                }
            }
        }

        // =========================
        // SPLITS
        // =========================
        private static List<ResultSplit> ExtractSplits(XElement resultEl, int resultId)
        {
            // резултатът може да има <SPLITS><SPLIT .../></SPLITS>
            var splitEls = resultEl.Descendants()
                .Where(x => x.Name.LocalName == "SPLIT");

            var list = new List<ResultSplit>();

            foreach (var s in splitEls)
            {
                var distStr = ((string?)s.Attribute("distance") ?? "").Trim();
                var timeStr = ((string?)s.Attribute("swimtime") ?? "").Trim();

                if (!int.TryParse(distStr, out var dist)) continue;
                if (string.IsNullOrWhiteSpace(timeStr)) continue;

                if (!TryParseSwimTimeToSeconds(timeStr, out var sec)) continue;

                list.Add(new ResultSplit
                {
                    ResultId = resultId,
                    Distance = dist,
                    ValueTime = Math.Round((decimal)sec, 2)
                });
            }

            return list
                .GroupBy(x => x.Distance)
                .Select(g => g.First())
                .OrderBy(x => x.Distance)
                .ToList();
        }

        // =========================
        // XML context + helpers
        // =========================
        private static XDocument LoadXml(IFormFile file)
        {
            using var s = file.OpenReadStream();
            return XDocument.Load(s);
        }

        private static (string Location, int PoolLen, DateTime MeetDate, Dictionary<int, EventInfo> EventMap) BuildMeetContext(XDocument doc)
        {
            var meet = doc.Descendants("MEET").FirstOrDefault();
            var meetCity = (string?)meet?.Attribute("city") ?? "";
            var meetName = (string?)meet?.Attribute("name") ?? "";
            var location = BuildLocation(meetCity, meetName);

            var poolLen = GetPoolLength(doc) ?? 25;
            var meetDate = TryGetMeetDate(doc) ?? DateTime.UtcNow.Date;
            var eventMap = BuildEventMap(doc, meetDate);

            return (location, poolLen, meetDate, eventMap);
        }

        private sealed class XmlClub
        {
            public string Name { get; set; } = "";
            public string Code { get; set; } = "";
            public string Nation { get; set; } = "";
            public XElement Element { get; set; } = default!;
        }

        private static List<XmlClub> GetXmlClubs(XDocument doc)
        {
            return doc.Descendants("CLUB")
                .Where(c => c.Descendants("ATHLETE").Any())
                .Select(c => new XmlClub
                {
                    Name = ((string?)c.Attribute("name") ?? "").Trim(),
                    Code = ((string?)c.Attribute("code") ?? (string?)c.Attribute("clubcode") ?? "").Trim(),
                    Nation = ((string?)c.Attribute("nation") ?? (string?)c.Attribute("country") ?? "").Trim(),
                    Element = c
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .ToList();
        }

        private static void AddSample(LenexImportReport report, string s)
        {
            if (report.SkippedAthletesSample.Count < 30)
                report.SkippedAthletesSample.Add(s);
        }

        private static bool SameCodeNation(string xmlCode, string xmlNation, string dbCode, string dbNation)
        {
            if (string.IsNullOrWhiteSpace(xmlCode) || string.IsNullOrWhiteSpace(xmlNation)) return false;
            return string.Equals(xmlCode.Trim(), dbCode.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(xmlNation.Trim(), dbNation.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildLocation(string city, string meetName)
        {
            city = (city ?? "").Trim();
            meetName = (meetName ?? "").Trim();
            if (city.Length == 0 && meetName.Length == 0) return "LENEX Import";
            if (city.Length == 0) return meetName;
            if (meetName.Length == 0) return city;
            return $"{city} - {meetName}";
        }

        private static string ExtractSwrid(XElement athleteEl)
        {
            var v =
                ((string?)athleteEl.Attribute("swrid") ??
                 (string?)athleteEl.Attribute("SWRID") ??
                 (string?)athleteEl.Attribute("swimrid") ??
                 "").Trim();

            return v;
        }

        private static string PersonKey(string fnNorm, string lnNorm, int year, char gender)
            => $"{fnNorm}|{lnNorm}|{year}|{gender}";

        private static char NormGender(string? g)
        {
            if (string.IsNullOrWhiteSpace(g)) return '?';
            g = g.Trim().ToUpperInvariant();

            if (g == "M" || g.StartsWith("MALE")) return 'M';
            if (g == "F" || g.StartsWith("FEMALE")) return 'F';

            if (g.StartsWith("M")) return 'M';
            if (g.StartsWith("F")) return 'F';

            return '?';
        }

        private static char NormGenderToChar(string? g)
        {
            g = (g ?? "").Trim().ToUpperInvariant();
            if (g.StartsWith("M")) return 'M';
            if (g.StartsWith("F")) return 'F';
            return '?';
        }

        private static string Norm(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return "";

            s = TransliterateBgToLatin(s);
            s = RemoveDiacritics(s);
            s = s.ToUpperInvariant();

            var chars = s.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-').ToArray();
            var cleaned = new string(chars);

            while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
            return cleaned.Replace("-", " ").Trim();
        }

        private static string NormClub(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return "";

            s = TransliterateBgToLatin(s);
            s = RemoveDiacritics(s);
            s = s.ToUpperInvariant();

            var chars = s.Where(ch => char.IsLetterOrDigit(ch)).ToArray();
            return new string(chars);
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var c in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string TransliterateBgToLatin(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var sb = new StringBuilder(input.Length * 2);

            foreach (var ch in input)
            {
                sb.Append(ch switch
                {
                    'А' => "A",
                    'а' => "a",
                    'Б' => "B",
                    'б' => "b",
                    'В' => "V",
                    'в' => "v",
                    'Г' => "G",
                    'г' => "g",
                    'Д' => "D",
                    'д' => "d",
                    'Е' => "E",
                    'е' => "e",
                    'Ж' => "Zh",
                    'ж' => "zh",
                    'З' => "Z",
                    'з' => "z",
                    'И' => "I",
                    'и' => "i",
                    'Й' => "Y",
                    'й' => "y",
                    'К' => "K",
                    'к' => "k",
                    'Л' => "L",
                    'л' => "l",
                    'М' => "M",
                    'м' => "m",
                    'Н' => "N",
                    'н' => "n",
                    'О' => "O",
                    'о' => "o",
                    'П' => "P",
                    'п' => "p",
                    'Р' => "R",
                    'р' => "r",
                    'С' => "S",
                    'с' => "s",
                    'Т' => "T",
                    'т' => "t",
                    'У' => "U",
                    'у' => "u",
                    'Ф' => "F",
                    'ф' => "f",
                    'Х' => "H",
                    'х' => "h",
                    'Ц' => "Ts",
                    'ц' => "ts",
                    'Ч' => "Ch",
                    'ч' => "ch",
                    'Ш' => "Sh",
                    'ш' => "sh",
                    'Щ' => "Sht",
                    'щ' => "sht",
                    'Ъ' => "A",
                    'ъ' => "a",
                    'Ь' => "",
                    'ь' => "",
                    'Ю' => "Yu",
                    'ю' => "yu",
                    'Я' => "Ya",
                    'я' => "ya",
                    _ => ch.ToString()
                });
            }

            return sb.ToString();
        }

        private static bool IsSameClub(string fileClub, string appClub)
        {
            var a = NormClub(fileClub);
            var b = NormClub(appClub);
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            return a.Contains(b) || b.Contains(a);
        }

        private static string NormalizeDiscipline(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            var chars = s.Where(ch => char.IsLetterOrDigit(ch)).ToArray();
            return new string(chars);
        }

        private static bool TryMapDisciplineId(string disciplineKey, Dictionary<string, int> disciplineByName, out int id)
        {
            var norm = NormalizeDiscipline(disciplineKey);
            if (disciplineByName.TryGetValue(norm, out id))
                return true;

            var withM = AddMetersIfMissing(disciplineKey);
            if (!string.Equals(withM, disciplineKey, StringComparison.OrdinalIgnoreCase))
            {
                if (disciplineByName.TryGetValue(NormalizeDiscipline(withM), out id))
                    return true;
            }

            var isRelay = disciplineKey.IndexOf('x') >= 0;

            var alt = disciplineKey
                .Replace("FREESTYLE", "Freestyle", StringComparison.OrdinalIgnoreCase)
                .Replace("FREE", "Freestyle", StringComparison.OrdinalIgnoreCase)
                .Replace("BACKSTROKE", "Backstroke", StringComparison.OrdinalIgnoreCase)
                .Replace("BACK", "Backstroke", StringComparison.OrdinalIgnoreCase)
                .Replace("BREASTSTROKE", "Breaststroke", StringComparison.OrdinalIgnoreCase)
                .Replace("BREAST", "Breaststroke", StringComparison.OrdinalIgnoreCase)
                .Replace("BUTTERFLY", "Butterfly", StringComparison.OrdinalIgnoreCase)
                .Replace("FLY", "Butterfly", StringComparison.OrdinalIgnoreCase);

            if (!isRelay)
            {
                alt = alt
                    .Replace("MEDLEY", "Individual Medley", StringComparison.OrdinalIgnoreCase)
                    .Replace("IM", "Individual Medley", StringComparison.OrdinalIgnoreCase);
            }

            alt = AddMetersIfMissing(alt);

            if (disciplineByName.TryGetValue(NormalizeDiscipline(alt), out id))
                return true;

            id = 0;
            return false;
        }

        private static string AddMetersIfMissing(string s)
        {
            s = (s ?? "").Trim();
            var i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;

            if (i > 0 && i < s.Length && s[i] == ' ')
                return s.Substring(0, i) + "m" + s.Substring(i);

            return s;
        }

        private static int? GetPoolLength(XDocument doc)
        {
            var meet = doc.Descendants("MEET").FirstOrDefault();
            var course = ((string?)meet?.Attribute("course") ?? "").Trim().ToUpperInvariant();

            if (course == "LCM") return 50;
            if (course == "SCM") return 25;

            var pool = doc.Descendants("POOL").FirstOrDefault();
            if (pool != null && int.TryParse((string?)pool.Attribute("length"), out var len) && (len == 25 || len == 50))
                return len;

            return null;
        }

        private static DateTime? TryGetMeetDate(XDocument doc)
        {
            var meet = doc.Descendants("MEET").FirstOrDefault();
            var sd = (string?)meet?.Attribute("startdate");
            if (TryParseLenexDate(sd, out var d1)) return d1.Date;

            var sess = doc.Descendants("SESSION").FirstOrDefault();
            var d = (string?)sess?.Attribute("date");
            if (TryParseLenexDate(d, out var d2)) return d2.Date;

            return null;
        }

        private class EventInfo
        {
            public string DisciplineKey { get; set; } = "";
            public DateTime Date { get; set; }
        }

        private static Dictionary<int, EventInfo> BuildEventMap(XDocument doc, DateTime defaultDate)
        {
            var map = new Dictionary<int, EventInfo>();

            foreach (var ev in doc.Descendants("EVENT"))
            {
                if (!int.TryParse((string?)ev.Attribute("eventid"), out var eventId))
                    continue;

                var sess = ev.Ancestors("SESSION").FirstOrDefault();
                var sessDateRaw = (string?)sess?.Attribute("date");
                var baseDate = defaultDate;
                if (TryParseLenexDate(sessDateRaw, out var sd)) baseDate = sd.Date;

                var swim = ev.Element("SWIMSTYLE");
                var key = swim != null ? BuildDisciplineKeyFromSwimstyle(swim) : "";
                if (string.IsNullOrWhiteSpace(key)) continue;

                map[eventId] = new EventInfo { DisciplineKey = key, Date = baseDate };
            }

            return map;
        }

        private static string BuildDisciplineKeyFromSwimstyle(XElement swim)
        {
            int.TryParse((string?)swim.Attribute("distance"), out var dist);
            int.TryParse((string?)swim.Attribute("relaycount"), out var relayCount);

            var stroke = ((string?)swim.Attribute("stroke") ?? (string?)swim.Attribute("name") ?? "").Trim();
            var up = stroke.ToUpperInvariant();

            var isRelay = relayCount > 1;

            var strokeName = up switch
            {
                "FREE" => "Freestyle",
                "BACK" => "Backstroke",
                "BREAST" => "Breaststroke",
                "FLY" => "Butterfly",
                "IM" => "Individual Medley",
                "MEDLEY" => isRelay ? "Medley" : "Individual Medley",
                _ => string.IsNullOrWhiteSpace(stroke) ? "Unknown" : stroke
            };

            if (isRelay && dist > 0) return $"{relayCount}x{dist}m {strokeName}";
            if (dist > 0) return $"{dist}m {strokeName}";

            var name = ((string?)swim.Attribute("name") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(name)) return name;

            return strokeName;
        }

        private static int? TryGetBirthYear(XElement athlete)
        {
            var by = (string?)athlete.Attribute("birthyear")
                  ?? (string?)athlete.Attribute("yearofbirth")
                  ?? (string?)athlete.Attribute("birthYear");

            if (int.TryParse(by, out var y) && y > 1900 && y < 2100)
                return y;

            var bd = ((string?)athlete.Attribute("birthdate")
                   ?? (string?)athlete.Attribute("birthDate"))?.Trim();

            if (string.IsNullOrWhiteSpace(bd))
                return null;

            var formats = new[]
            {
                "yyyy-MM-dd",
                "yyyyMMdd",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss.fff",
                "yyyy-MM-ddTHH:mm:ss.fffZ"
            };

            if (DateTime.TryParseExact(
                bd,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var dt))
            {
                return dt.Year;
            }

            if (DateTime.TryParse(bd, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt2))
                return dt2.Year;

            return null;
        }

        private static int? TryParseIntAttr(XElement el, string attr)
        {
            var s = (string?)el.Attribute(attr);
            if (int.TryParse(s, out var v)) return v;
            return null;
        }

        private static string? ExtractTimeString(XElement resultEl)
        {
            var direct =
                (string?)resultEl.Attribute("swimtime") ??
                (string?)resultEl.Attribute("time") ??
                (string?)resultEl.Attribute("resulttime");

            if (!string.IsNullOrWhiteSpace(direct)) return direct.Trim();

            var tEl = resultEl.Descendants()
                .FirstOrDefault(x =>
                    x.Name.LocalName.Equals("SWIMTIME", StringComparison.OrdinalIgnoreCase) ||
                    x.Name.LocalName.Equals("TIME", StringComparison.OrdinalIgnoreCase) ||
                    x.Name.LocalName.Equals("RESULTTIME", StringComparison.OrdinalIgnoreCase));

            if (tEl != null)
            {
                var a =
                    (string?)tEl.Attribute("swimtime") ??
                    (string?)tEl.Attribute("time") ??
                    (string?)tEl.Attribute("resulttime");

                if (!string.IsNullOrWhiteSpace(a)) return a.Trim();

                var inner = (tEl.Value ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(inner)) return inner;
            }

            return null;
        }

        private static bool TryParseSwimTimeToSeconds(string input, out double seconds)
        {
            seconds = 0;

            input = (input ?? "").Trim();
            if (input.Length == 0) return false;

            input = input.Replace(',', '.');
            input = string.Concat(input.Where(c => !char.IsWhiteSpace(c)));

            if (input.Any(char.IsLetter)) return false;

            var parts = input.Split(':');

            if (parts.Length == 1)
            {
                if (double.TryParse(parts[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var s) && s > 0)
                {
                    seconds = s;
                    return true;
                }
                return false;
            }

            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[0], out var min)) return false;
                if (!double.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var sec)) return false;

                var total = min * 60.0 + sec;
                if (total <= 0) return false;

                seconds = total;
                return true;
            }

            if (parts.Length == 3)
            {
                if (!int.TryParse(parts[0], out var hours)) return false;
                if (!int.TryParse(parts[1], out var min)) return false;
                if (!double.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var sec)) return false;

                var total = hours * 3600.0 + min * 60.0 + sec;
                if (total <= 0) return false;

                seconds = total;
                return true;
            }

            return false;
        }

        private static bool TryParseLenexDate(string? raw, out DateTime d)
        {
            d = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            {
                d = dt;
                return true;
            }
            return false;
        }

        private static string BuildLenexSourceKey(
            int clubId,
            string swrid,
            int? eventId,
            int disciplineId,
            DateTime resultDate,
            int poolLen,
            decimal valueTime,
            string location,
            XElement resultEl)
        {
            string A(string n) => ((string?)resultEl.Attribute(n) ?? "").Trim();

            var extra =
                $"rid={A("resultid")}|heat={A("heatid")}|lane={A("lane")}|round={A("round")}|type={A("type")}|status={A("status")}";

            var raw =
                $"LENEX|club={clubId}|swrid={swrid}|event={eventId?.ToString() ?? ""}|disc={disciplineId}|date={resultDate:yyyy-MM-dd}|pool={poolLen}|time={valueTime:0.00}|loc={NormalizeLoc(location)}|{extra}";

            return Sha256Hex(raw);
        }

        private static string NormalizeLoc(string s)
            => (s ?? "").Trim().ToUpperInvariant();

        private static string Sha256Hex(string s)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(s);
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private async Task<string> GenerateUniqueSwrid(Club club)
        {
            var nation = (club.Nation ?? "BUL").Trim().ToUpperInvariant();
            if (nation.Length > 3) nation = nation.Substring(0, 3);

            var code = (club.Code ?? club.Id.ToString()).Trim().ToUpperInvariant();
            code = new string(code.Where(char.IsLetterOrDigit).ToArray());
            if (code.Length > 5) code = code.Substring(0, 5);
            if (code.Length == 0) code = club.Id.ToString();

            var prefix = $"{nation}{code}";

            for (int i = 0; i < 30; i++)
            {
                var token = RandomBase32(10);
                var swrid = $"{prefix}{token}";

                if (swrid.Length > 20)
                    swrid = swrid.Substring(0, 20);

                if (!await _userRepository.IsSwridTaken(swrid))
                    return swrid;
            }

            return $"{prefix}{Guid.NewGuid():N}".Substring(0, 20);
        }

        private static string RandomBase32(int len)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[len];
            RandomNumberGenerator.Fill(bytes);

            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append(alphabet[bytes[i] % alphabet.Length]);

            return sb.ToString();
        }
    }
}