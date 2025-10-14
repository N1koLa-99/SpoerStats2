// ClassRepository/AthleteSelectionRepository.cs
using System.Data.SqlClient;
using Dapper;
using SpoerStats2.Models;
using SpoerStats2.Repository;

namespace SpoerStats2.ClassRepository
{
    public class AthleteSelectionRepository : IAthleteSelectionRepository
    {
        private readonly string _cs;
        public AthleteSelectionRepository(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

        public async Task<int> AddAsync(AthleteSlotSelection s)
        {
            const string sql = @"
INSERT dbo.AthleteSlotSelections(ClubId, UserId, SlotId)
VALUES(@ClubId, @UserId, @SlotId);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            using var con = new SqlConnection(_cs);
            try
            {
                return await con.ExecuteScalarAsync<int>(sql, s);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627) // UNIQUE(UserId,SlotId)
            {
                throw new InvalidOperationException("Същият слот вече е избран от този състезател.", ex);
            }
        }

        public async Task<bool> RemoveAsync(int id)
        {
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync("DELETE dbo.AthleteSlotSelections WHERE Id=@id", new { id })) > 0;
        }

        public async Task<bool> RemoveByUserAndSlotAsync(int userId, int slotId)
        {
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(
                "DELETE dbo.AthleteSlotSelections WHERE UserId=@userId AND SlotId=@slotId",
                new { userId, slotId })) > 0;
        }

        public async Task<IEnumerable<AthleteSlotSelection>> GetByUserAsync(int userId)
        {
            using var con = new SqlConnection(_cs);
            const string sql = "SELECT * FROM dbo.AthleteSlotSelections WHERE UserId=@userId ORDER BY SelectedAt;";
            return await con.QueryAsync<AthleteSlotSelection>(sql, new { userId });
        }

        public async Task<IEnumerable<AthleteSlotSelection>> GetBySlotAsync(int slotId)
        {
            using var con = new SqlConnection(_cs);
            const string sql = "SELECT * FROM dbo.AthleteSlotSelections WHERE SlotId=@slotId ORDER BY SelectedAt;";
            return await con.QueryAsync<AthleteSlotSelection>(sql, new { slotId });
        }

        public async Task<int> CountBySlotAsync(int slotId)
        {
            using var con = new SqlConnection(_cs);
            return await con.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.AthleteSlotSelections WHERE SlotId=@slotId", new { slotId });
        }

        public async Task<int?> GetSlotClubIdAsync(int slotId)
        {
            const string sql = @"
SELECT g.ClubId
FROM dbo.ScheduleSlots ss
JOIN dbo.TrainingGroups g ON g.Id = ss.GroupId
WHERE ss.Id=@slotId;";
            using var con = new SqlConnection(_cs);
            return await con.ExecuteScalarAsync<int?>(sql, new { slotId });
        }

        public async Task<int?> GetUserClubIdAsync(int userId)
        {
            using var con = new SqlConnection(_cs);
            return await con.ExecuteScalarAsync<int?>(
                "SELECT ClubID FROM dbo.Users WHERE Id=@userId", new { userId });
        }
        public async Task<IEnumerable<User>> GetUsersBySlotAsync(int slotId)
        {
            const string sql = @"
SELECT u.Id, u.FirstName, u.LastName, u.ClubID, u.Email, u.Gender, u.YearOfBirth, u.RoleID, u.StatusID, u.ProfileImage_url
FROM dbo.AthleteSlotSelections ass
JOIN dbo.Users u ON u.Id = ass.UserId
WHERE ass.SlotId = @slotId
ORDER BY u.LastName, u.FirstName;";
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<User>(sql, new { slotId });
        }
    }
}
