using System.Data.SqlClient;
using Dapper;
using SpoerStats2.Models;
using SpoerStats2.Repository;

namespace SpoerStats2.ClassRepository
{
    public class ScheduleSlotRepository : IScheduleSlotRepository
    {
        private readonly string _cs;
        public ScheduleSlotRepository(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

        public async Task<int> CreateAsync(ScheduleSlot s)
        {
            const string sql = @"
INSERT dbo.ScheduleSlots(GroupId, ClubSeasonId, Weekday, StartTime, EndTime, Location, Capacity)
VALUES(@GroupId, @ClubSeasonId, @Weekday, @StartTime, @EndTime, @Location, @Capacity);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            using var con = new SqlConnection(_cs);
            try
            {
                return await con.ExecuteScalarAsync<int>(sql, s);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) // UNIQUE violation
            {
                throw new InvalidOperationException("Слот с тези параметри вече съществува в групата/сезона.", ex);
            }
        }

        public async Task<bool> UpdateAsync(ScheduleSlot s)
        {
            const string sql = @"
UPDATE dbo.ScheduleSlots
SET GroupId=@GroupId, ClubSeasonId=@ClubSeasonId, Weekday=@Weekday,
    StartTime=@StartTime, EndTime=@EndTime, Location=@Location, Capacity=@Capacity
WHERE Id=@Id;";
            using var con = new SqlConnection(_cs);
            try
            {
                return (await con.ExecuteAsync(sql, s)) > 0;
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                throw new InvalidOperationException("Слот с тези параметри вече съществува в групата/сезона.", ex);
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            const string sql = @"DELETE FROM dbo.ScheduleSlots WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, new { id })) > 0;
        }

        public async Task<ScheduleSlot?> GetByIdAsync(int id)
        {
            const string sql = @"SELECT * FROM dbo.ScheduleSlots WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return await con.QuerySingleOrDefaultAsync<ScheduleSlot>(sql, new { id });
        }

        public async Task<IEnumerable<ScheduleSlot>> GetByGroupAsync(int groupId)
        {
            const string sql = @"SELECT * FROM dbo.ScheduleSlots WHERE GroupId=@groupId ORDER BY Weekday, StartTime;";
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<ScheduleSlot>(sql, new { groupId });
        }

        /// <summary>
        /// Връща слотовете за клуб, като:
        /// - ако clubSeasonId е NULL → всички слотове (без филтър по сезон)
        /// - ако clubSeasonId е зададен → връща слотовете за този сезон ИЛИ безсезонните (ClubSeasonId IS NULL)
        ///   за да не „изпадат“ стари/грешно въведени записи.
        /// </summary>
        public async Task<IEnumerable<ScheduleSlot>> GetByClubSeasonAsync(int clubId, int? clubSeasonId)
        {
            string sql;
            object param;

            if (clubSeasonId is null)
            {
                sql = @"
SELECT ss.*
FROM dbo.ScheduleSlots ss
JOIN dbo.TrainingGroups g ON g.Id = ss.GroupId
WHERE g.ClubId = @clubId
ORDER BY g.Name, ss.Weekday, ss.StartTime;";
                param = new { clubId };
            }
            else
            {
                // ТУК е исканата промяна:
                // ... WHERE g.ClubId=@clubId
                //   AND (ss.ClubSeasonId = @activeSeasonId OR ss.ClubSeasonId IS NULL)
                sql = @"
SELECT ss.*
FROM dbo.ScheduleSlots ss
JOIN dbo.TrainingGroups g ON g.Id = ss.GroupId
WHERE g.ClubId = @clubId
  AND (ss.ClubSeasonId = @clubSeasonId OR ss.ClubSeasonId IS NULL)
ORDER BY g.Name, ss.Weekday, ss.StartTime;";
                param = new { clubId, clubSeasonId };
            }

            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<ScheduleSlot>(sql, param);
        }
    }
}
