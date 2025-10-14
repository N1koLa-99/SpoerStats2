// ClassRepository/TrainingGroupRepository.cs
using System.Data.SqlClient;
using Dapper;
using SpoerStats2.Models;
using SpoerStats2.Repository;

namespace SpoerStats2.ClassRepository
{
    public class TrainingGroupRepository : ITrainingGroupRepository
    {
        private readonly string _cs;
        public TrainingGroupRepository(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

        public async Task<int> CreateAsync(TrainingGroup g)
        {
            const string sql = @"
INSERT dbo.TrainingGroups(ClubId, Name, Description)
VALUES(@ClubId, @Name, @Description);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
            using var con = new SqlConnection(_cs);
            return await con.ExecuteScalarAsync<int>(sql, g);
        }

        public async Task<bool> UpdateAsync(TrainingGroup g)
        {
            const string sql = @"UPDATE dbo.TrainingGroups SET Name=@Name, Description=@Description WHERE Id=@Id;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, g)) > 0;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            const string sql = @"DELETE FROM dbo.TrainingGroups WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, new { id })) > 0;
        }

        public async Task<TrainingGroup?> GetByIdAsync(int id)
        {
            const string sql = @"SELECT * FROM dbo.TrainingGroups WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return await con.QuerySingleOrDefaultAsync<TrainingGroup>(sql, new { id });
        }

        public async Task<IEnumerable<TrainingGroup>> GetByClubAsync(int clubId)
        {
            const string sql = @"SELECT * FROM dbo.TrainingGroups WHERE ClubId=@clubId ORDER BY Name;";
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<TrainingGroup>(sql, new { clubId });
        }
    }
}
