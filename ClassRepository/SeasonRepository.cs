using System.Data;
using System.Data.SqlClient;
using Dapper;
using SpoerStats2.Models;
using SpoerStats2.Repository;

namespace SpoerStats2.ClassRepository
{
    public class SeasonRepository : ISeasonRepository
    {
        private readonly string _cs;
        public SeasonRepository(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

        public async Task<int> CreateSeasonAsync(ClubSeason s)
        {
            const string sql = @"
INSERT dbo.ClubSeasons(ClubId, Name, StartDate, EndDate, IsActive, CreatedByUserId)
VALUES(@ClubId, @Name, @StartDate, @EndDate, @IsActive, @CreatedByUserId);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            using var con = new SqlConnection(_cs);
            return await con.ExecuteScalarAsync<int>(sql, s);
        }

        public async Task<bool> UpdateSeasonAsync(ClubSeason s)
        {
            const string sql = @"
UPDATE dbo.ClubSeasons
SET Name=@Name, StartDate=@StartDate, EndDate=@EndDate, IsActive=@IsActive
WHERE Id=@Id;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, s)) > 0;
        }

        public async Task<bool> SetSeasonActiveAsync(int id, bool isActive)
        {
            const string sql = "UPDATE dbo.ClubSeasons SET IsActive=@isActive WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, new { id, isActive })) > 0;
        }

        public async Task<ClubSeason?> GetSeasonByIdAsync(int id)
        {
            const string sql = "SELECT * FROM dbo.ClubSeasons WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return await con.QuerySingleOrDefaultAsync<ClubSeason>(sql, new { id });
        }

        public async Task<IEnumerable<ClubSeason>> GetSeasonsByClubAsync(int clubId)
        {
            const string sql = "SELECT * FROM dbo.ClubSeasons WHERE ClubId=@clubId ORDER BY StartDate DESC;";
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<ClubSeason>(sql, new { clubId });
        }

        // ===== Billing Months =====

        public async Task<IEnumerable<SeasonBillingMonth>> GetSeasonMonthsAsync(int clubSeasonId)
        {
            const string sql = @"SELECT * FROM dbo.SeasonBillingMonths WHERE ClubSeasonId=@clubSeasonId ORDER BY YearMonth;";
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<SeasonBillingMonth>(sql, new { clubSeasonId });
        }

        public async Task<int> GenerateSeasonMonthsAsync(int clubSeasonId, bool markBillable = true)
        {
            using var con = new SqlConnection(_cs);
            return await con.ExecuteAsync(
                "dbo.Sp_GenerateSeasonBillingMonths",
                new { ClubSeasonId = clubSeasonId, MarkBillable = markBillable },
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task<bool> SetMonthBillableAsync(int id, bool isBillable)
        {
            const string sql = "UPDATE dbo.SeasonBillingMonths SET IsBillable=@isBillable WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, new { id, isBillable })) > 0;
        }

        public async Task<bool> SetMonthBillableAsync(int clubSeasonId, string yearMonth, bool isBillable)
        {
            const string sql = @"
UPDATE dbo.SeasonBillingMonths
SET IsBillable=@isBillable
WHERE ClubSeasonId=@clubSeasonId AND YearMonth=@yearMonth;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, new { clubSeasonId, yearMonth, isBillable })) > 0;
        }
        public async Task<bool> DeleteSeasonAsync(int clubSeasonId)
        {
            using var con = new SqlConnection(_cs);
            await con.OpenAsync();
            using var tx = con.BeginTransaction();

            // 0) exists / active check
            var isActive = await con.ExecuteScalarAsync<bool?>(
                "SELECT IsActive FROM dbo.ClubSeasons WHERE Id=@clubSeasonId;",
                new { clubSeasonId }, tx);
            if (isActive is null || isActive == true) { tx.Rollback(); return false; }

            // 1) ако има AthleteSelections към ScheduleSlots -> изтрий първо тях
            const string delSel = @"
        IF OBJECT_ID('dbo.AthleteSelections','U') IS NOT NULL
        BEGIN
            DELETE s
            FROM dbo.AthleteSelections s
            WHERE s.ScheduleSlotId IN (SELECT Id FROM dbo.ScheduleSlots WHERE ClubSeasonId=@clubSeasonId);
        END";
            await con.ExecuteAsync(delSel, new { clubSeasonId }, tx);

            // 2) ScheduleSlots (ако няма CASCADE)
            await con.ExecuteAsync("DELETE FROM dbo.ScheduleSlots WHERE ClubSeasonId=@clubSeasonId;",
                new { clubSeasonId }, tx);

            // 3) MemberBillingSettings (ако няма CASCADE)
            await con.ExecuteAsync("DELETE FROM dbo.MemberBillingSettings WHERE ClubSeasonId=@clubSeasonId;",
                new { clubSeasonId }, tx);

            // 4) SeasonBillingMonths (ако няма CASCADE)
            await con.ExecuteAsync("DELETE FROM dbo.SeasonBillingMonths WHERE ClubSeasonId=@clubSeasonId;",
                new { clubSeasonId }, tx);

            // 5) Цени за сезона
            await con.ExecuteAsync("DELETE FROM dbo.ClubFeePlans WHERE ClubSeasonId=@clubSeasonId;",
                new { clubSeasonId }, tx);

            // 6) Накрая – самия сезон
            var rows = await con.ExecuteAsync("DELETE FROM dbo.ClubSeasons WHERE Id=@clubSeasonId;",
                new { clubSeasonId }, tx);

            if (rows == 0) { tx.Rollback(); return false; }
            tx.Commit();
            return true;
        }



    }
}
