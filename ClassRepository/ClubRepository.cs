using Dapper;
using Microsoft.Extensions.Configuration;
using SpoerStats2.Models;
using SpoerStats2.Repository;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpoerStats2.ClassRepository
{
    public class ClubRepository : IClubRepository
    {
        private readonly string _cs;

        public ClubRepository(IConfiguration cfg)
        {
            _cs = cfg.GetConnectionString("DefaultConnection");
        }

        public async Task<IEnumerable<Club>> GetAllClubs()
        {
            using var c = new SqlConnection(_cs);
            return await c.QueryAsync<Club>("SELECT * FROM Clubs");
        }

        public async Task<Club> GetClubById(int id)
        {
            using var c = new SqlConnection(_cs);
            return await c.QuerySingleOrDefaultAsync<Club>(
                "SELECT * FROM Clubs WHERE Id=@Id",
                new { Id = id });
        }

        public async Task AddClub(Club club)
        {
            using var c = new SqlConnection(_cs);
            var sql = @"INSERT INTO Clubs (Name, Location, Address, Code, Nation)
                        VALUES (@Name, @Location, @Address, @Code, @Nation)";
            await c.ExecuteAsync(sql, club);
        }

        public async Task UpdateClub(Club club)
        {
            using var c = new SqlConnection(_cs);
            var sql = @"UPDATE Clubs SET
                        Name=@Name, Location=@Location, Address=@Address, Code=@Code, Nation=@Nation
                        WHERE Id=@Id";
            await c.ExecuteAsync(sql, club);
        }

        public async Task DeleteClub(int id)
        {
            using var c = new SqlConnection(_cs);
            await c.ExecuteAsync("DELETE FROM Clubs WHERE Id=@Id", new { Id = id });
        }

        // ✅ STRICT match по code+nation (trim/case-insensitive + no random TOP 1)
        public async Task<Club?> GetClubByCodeNation(string code, string nation)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            nation = (nation ?? "").Trim().ToUpperInvariant();

            if (code.Length == 0 || nation.Length == 0)
                return null;

            using var c = new SqlConnection(_cs);

            // TOP 2 -> ако има дубликати, НЕ връщаме случаен клуб
            var sql = @"
SELECT TOP 2 *
FROM Clubs
WHERE UPPER(LTRIM(RTRIM(ISNULL(Code, '')))) = @Code
  AND UPPER(LTRIM(RTRIM(ISNULL(Nation, '')))) = @Nation
ORDER BY Id;";

            var rows = (await c.QueryAsync<Club>(sql, new { Code = code, Nation = nation })).ToList();

            if (rows.Count == 1)
                return rows[0];

            // 0 = няма match; >1 = двусмислено (лоши данни в DB)
            return null;
        }

        // ✅ SAFE loose match - не връща грешен клуб при двусмислие
        public async Task<Club?> FindClubByNameLoose(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;

            using var c = new SqlConnection(_cs);

            // 1) Exact (trim + lower), ако има точно един -> връщаме
            var exactSql = @"
SELECT TOP 2 *
FROM Clubs
WHERE LOWER(LTRIM(RTRIM(ISNULL(Name, '')))) = @Name
ORDER BY Id;";

            var exact = (await c.QueryAsync<Club>(exactSql, new
            {
                Name = name.Trim().ToLowerInvariant()
            })).ToList();

            if (exact.Count == 1)
                return exact[0];

            if (exact.Count > 1)
                return null; // двусмислено, не избираме произволен

            // 2) Вземаме всички и правим безопасен нормализиран match в паметта
            var all = (await c.QueryAsync<Club>(
                "SELECT * FROM Clubs WHERE Name IS NOT NULL AND LTRIM(RTRIM(Name)) <> ''"))
                .ToList();

            if (all.Count == 0) return null;

            var inputNorm = NormClub(name);
            if (string.IsNullOrWhiteSpace(inputNorm))
                return null;

            // оценка на сходство (прост и безопасен scoring)
            var scored = new List<(Club Club, int Score, string NormName)>();

            foreach (var club in all)
            {
                var clubName = (club.Name ?? "").Trim();
                if (clubName.Length == 0) continue;

                var clubNorm = NormClub(clubName);
                if (clubNorm.Length == 0) continue;

                int score = 0;

                // най-силен: exact normalized
                if (clubNorm == inputNorm)
                {
                    score = 300;
                }
                else
                {
                    // contains / startswith - по-слаби, но допустими
                    if (clubNorm.StartsWith(inputNorm, StringComparison.OrdinalIgnoreCase) ||
                        inputNorm.StartsWith(clubNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        score = Math.Max(score, 220);
                    }

                    if (clubNorm.Contains(inputNorm, StringComparison.OrdinalIgnoreCase) ||
                        inputNorm.Contains(clubNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        score = Math.Max(score, 180);
                    }

                    // token overlap (допълнителна защита)
                    var inputTokens = SplitTokens(inputNorm);
                    var clubTokens = SplitTokens(clubNorm);

                    if (inputTokens.Count > 0 && clubTokens.Count > 0)
                    {
                        var common = inputTokens.Intersect(clubTokens, StringComparer.OrdinalIgnoreCase).Count();
                        if (common > 0)
                        {
                            var tokenScore = common * 20;
                            score = Math.Max(score, tokenScore);
                        }
                    }
                }

                if (score > 0)
                    scored.Add((club, score, clubNorm));
            }

            if (scored.Count == 0)
                return null;

            // взимаме най-добрите
            var bestScore = scored.Max(x => x.Score);
            var best = scored
                .Where(x => x.Score == bestScore)
                .ToList();

            // ако има точно един - ок
            if (best.Count == 1)
                return best[0].Club;

            // ако има повече от един със същия score:
            // опит за допълнително стесняване - най-къс normalized name (по-точен match)
            var minLen = best.Min(x => x.NormName.Length);
            var shortest = best.Where(x => x.NormName.Length == minLen).ToList();

            if (shortest.Count == 1)
                return shortest[0].Club;

            // още е двусмислено -> НЕ връщаме случаен клуб
            return null;
        }

        // =========================
        // Helpers (same normalization philosophy as LENEX import)
        // =========================

        private static HashSet<string> SplitTokens(string normalized)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(normalized))
                return set;

            // normalized name is already alnum only, but guard anyway
            var sb = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            foreach (var part in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length >= 2)
                    set.Add(part);
            }

            return set;
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

    }
}