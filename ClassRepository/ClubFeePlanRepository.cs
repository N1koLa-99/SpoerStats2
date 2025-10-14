using System.Data.SqlClient;
using Dapper;
using SpoerStats2.Models;
using SpoerStats2.Repository;

namespace SpoerStats2.ClassRepository
{
    public class ClubFeePlanRepository : IClubFeePlanRepository
    {
        private readonly string _cs;
        public ClubFeePlanRepository(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

        public async Task<int> CreateAsync(ClubFeePlan p)
        {
            const string sql = @"
INSERT dbo.ClubFeePlans(ClubId, ClubSeasonId, TrainingsPerWeek, MonthlyFee, ValidFrom, ValidTo)
VALUES(@ClubId, @ClubSeasonId, @TrainingsPerWeek, @MonthlyFee, @ValidFrom, @ValidTo);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            using var con = new SqlConnection(_cs);
            return await con.ExecuteScalarAsync<int>(sql, p);
        }

        public async Task<bool> UpdateAsync(ClubFeePlan p)
        {
            const string sql = @"
UPDATE dbo.ClubFeePlans
SET ClubId=@ClubId,
    ClubSeasonId=@ClubSeasonId,
    TrainingsPerWeek=@TrainingsPerWeek,
    MonthlyFee=@MonthlyFee,
    ValidFrom=@ValidFrom,
    ValidTo=@ValidTo
WHERE Id=@Id;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, p)) > 0;
        }

        public async Task<bool> ArchiveAsync(int id, DateTime validTo)
        {
            const string sql = "UPDATE dbo.ClubFeePlans SET ValidTo=@validTo WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, new { id, validTo })) > 0;
        }

        public async Task<ClubFeePlan?> GetByIdAsync(int id)
        {
            const string sql = "SELECT * FROM dbo.ClubFeePlans WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return await con.QuerySingleOrDefaultAsync<ClubFeePlan>(sql, new { id });
        }

        public async Task<IEnumerable<ClubFeePlan>> GetByClubAsync(int clubId, int? clubSeasonId = null)
        {
            const string sql = @"
SELECT * FROM dbo.ClubFeePlans
WHERE ClubId=@clubId
  AND (@clubSeasonId IS NULL OR ClubSeasonId=@clubSeasonId)
ORDER BY TrainingsPerWeek, ValidFrom DESC;";
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<ClubFeePlan>(sql, new { clubId, clubSeasonId });
        }

        public async Task<ClubFeePlan?> GetActiveAsync(int clubId, int trainingsPerWeek, int? clubSeasonId = null, DateTime? onDate = null)
        {
            // Active = ValidFrom <= onDate AND (ValidTo IS NULL OR ValidTo >= onDate).
            // Предпочитаме запис за конкретния сезон; ако няма – запис с ClubSeasonId IS NULL.
            var date = (onDate ?? DateTime.UtcNow).Date;
            const string sql = @"
WITH fee AS (
    SELECT fp.*,
           ROW_NUMBER() OVER(
               PARTITION BY fp.ClubId, ISNULL(fp.ClubSeasonId,0), fp.TrainingsPerWeek
               ORDER BY fp.ValidFrom DESC
           ) AS rn
    FROM dbo.ClubFeePlans fp
    WHERE fp.ClubId=@clubId
      AND fp.TrainingsPerWeek=@tpw
      AND fp.ValidFrom <= @date
      AND (fp.ValidTo IS NULL OR fp.ValidTo >= @date)
)
SELECT TOP 1 *
FROM fee
WHERE rn=1
  AND ISNULL(ClubSeasonId,0) = ISNULL(@clubSeasonId,0)
UNION ALL
SELECT TOP 1 *
FROM fee
WHERE rn=1
  AND @clubSeasonId IS NOT NULL         -- ако не намерим за конкретен сезон, падни към NULL-сезон
  AND ClubSeasonId IS NULL
  AND NOT EXISTS (
        SELECT 1 FROM fee f2
        WHERE f2.rn=1 AND ISNULL(f2.ClubSeasonId,0)=ISNULL(@clubSeasonId,0)
  );";
            using var con = new SqlConnection(_cs);
            return (await con.QueryAsync<ClubFeePlan>(sql, new { clubId, tpw = trainingsPerWeek, clubSeasonId, date }))
                   .FirstOrDefault();
        }
    }
}
