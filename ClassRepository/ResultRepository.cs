using Dapper;
using Microsoft.Extensions.Configuration;
using SpoerStats2.Models;
using SpoerStats2.Repository;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace SpoerStats2.ClassRepository
{
    public class ResultRepository : IResultRepository
    {
        private readonly string _connectionString;

        public ResultRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<IEnumerable<Result>> GetAllResults()
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Result>("SELECT * FROM Results");
        }

        public async Task<Result> GetResultById(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<Result>(
                "SELECT * FROM Results WHERE Id = @Id", new { Id = id });
        }

        public async Task<IEnumerable<Result>> GetResultsByUserId(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Result>(
                "SELECT * FROM Results WHERE UserId = @UserId", new { UserId = userId });
        }

        public async Task<IEnumerable<Result>> GetResultsByUserIds(IEnumerable<int> userIds)
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<Result>(
                "SELECT * FROM Results WHERE UserId IN @UserIds", new { UserIds = userIds });
        }

        // ✅ НОВО: за /api/Results/by-club/{clubId}/by-discipline/{disciplineId}
        public async Task<IEnumerable<Result>> GetResultsByClubAndDiscipline(int clubId, int disciplineId)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
SELECT r.*
FROM Results r
INNER JOIN Users u ON u.Id = r.UserId
WHERE u.ClubID = @ClubId
  AND r.DisciplineId = @DisciplineId
ORDER BY r.ValueTime ASC, r.ResultDate DESC;";

            return await connection.QueryAsync<Result>(sql, new
            {
                ClubId = clubId,
                DisciplineId = disciplineId
            });
        }

        public async Task AddResult(Result result)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = @"INSERT INTO Results
                          (UserId, DisciplineId, ValueTime, ResultDate, SwimmingPoolStandart, Location, Source, SourceKey)
                          VALUES
                          (@UserId, @DisciplineId, @ValueTime, @ResultDate, @SwimmingPoolStandart, @Location, @Source, @SourceKey)";
            await connection.ExecuteAsync(query, result);
        }

        public async Task<int> AddResultReturnId(Result result)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
INSERT INTO Results (UserId, DisciplineId, ValueTime, ResultDate, SwimmingPoolStandart, Location, Source, SourceKey)
OUTPUT INSERTED.Id
VALUES (@UserId, @DisciplineId, @ValueTime, @ResultDate, @SwimmingPoolStandart, @Location, @Source, @SourceKey);";

            return await connection.ExecuteScalarAsync<int>(sql, result);
        }

        public async Task UpdateResult(Result result)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = @"UPDATE Results SET
                          UserId = @UserId,
                          DisciplineId = @DisciplineId,
                          ValueTime = @ValueTime,
                          ResultDate = @ResultDate,
                          SwimmingPoolStandart = @SwimmingPoolStandart,
                          Location = @Location,
                          Source = @Source,
                          SourceKey = @SourceKey
                          WHERE Id = @Id";
            await connection.ExecuteAsync(query, result);
        }

        public async Task DeleteResult(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync("DELETE FROM Results WHERE Id = @Id", new { Id = id });
        }

        public async Task AddResultsBulk(IEnumerable<Result> results)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            var query = @"INSERT INTO Results
                          (UserId, DisciplineId, ValueTime, ResultDate, SwimmingPoolStandart, Location, Source, SourceKey)
                          VALUES
                          (@UserId, @DisciplineId, @ValueTime, @ResultDate, @SwimmingPoolStandart, @Location, @Source, @SourceKey)";

            await connection.ExecuteAsync(query, results, transaction);
            transaction.Commit();
        }

        public async Task<IEnumerable<string>> GetExistingSourceKeys(string source, IEnumerable<int> userIds)
        {
            var ids = (userIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0) return Enumerable.Empty<string>();

            using var connection = new SqlConnection(_connectionString);
            var sql = @"SELECT SourceKey
                        FROM Results
                        WHERE Source = @Source
                          AND SourceKey IS NOT NULL
                          AND UserId IN @UserIds";

            return await connection.QueryAsync<string>(sql, new { Source = source, UserIds = ids });
        }

        public async Task<IEnumerable<ResultSplit>> GetSplitsByResultId(int resultId)
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<ResultSplit>(
                "SELECT * FROM ResultSplits WHERE ResultId = @ResultId ORDER BY Distance",
                new { ResultId = resultId });
        }

        public async Task AddSplitsBulk(IEnumerable<ResultSplit> splits)
        {
            var list = splits?.ToList() ?? new List<ResultSplit>();
            if (list.Count == 0) return;

            using var connection = new SqlConnection(_connectionString);

            var sql = @"
INSERT INTO ResultSplits (ResultId, Distance, ValueTime)
VALUES (@ResultId, @Distance, @ValueTime);";

            await connection.ExecuteAsync(sql, list);
        }
        public async Task<IEnumerable<ClubBestResultDto>> GetClubBestResultsByDiscipline(int clubId, int disciplineId)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
;WITH ranked AS
(
    SELECT
        u.ClubID AS ClubId,
        c.Name AS ClubName,
        r.UserId,
        u.FirstName AS UserFirstName,
        u.LastName AS UserLastName,
        u.YearOfBirth AS YearOfBirth,
        CAST(r.ValueTime AS decimal(10,2)) AS ValueTime,
        r.ResultDate,
        r.Location,
        ROW_NUMBER() OVER (
            PARTITION BY r.UserId
            ORDER BY r.ValueTime ASC, r.ResultDate DESC, r.Id DESC
        ) AS rn
    FROM Results r
    INNER JOIN Users u ON u.Id = r.UserId
    LEFT JOIN Clubs c ON c.Id = u.ClubID
    WHERE u.ClubID = @ClubId
      AND r.DisciplineId = @DisciplineId
)
SELECT
    ClubId,
    ClubName,
    UserId,
    UserFirstName,
    UserLastName,
    YearOfBirth,
    ValueTime,
    ResultDate,
    Location
FROM ranked
WHERE rn = 1
ORDER BY ValueTime ASC, ResultDate DESC, UserLastName ASC, UserFirstName ASC;";

            return await connection.QueryAsync<ClubBestResultDto>(sql, new
            {
                ClubId = clubId,
                DisciplineId = disciplineId
            });
        }
        public async Task<IEnumerable<ClubBestResultDto>> GetPlatformClubLeadersByDiscipline(int disciplineId)
        {
            using var connection = new SqlConnection(_connectionString);

            // disciplineId == 18 -> по-голямо е по-добре (метри)
            // иначе -> по-малко е по-добре (време)
            var bestOrder = disciplineId == 18
                ? "CAST(r.ValueTime AS decimal(10,2)) DESC, r.ResultDate DESC, r.Id DESC"
                : "CAST(r.ValueTime AS decimal(10,2)) ASC, r.ResultDate DESC, r.Id DESC";

            var finalOrder = disciplineId == 18
                ? "ValueTime DESC, ResultDate DESC, ClubName ASC"
                : "ValueTime ASC, ResultDate DESC, ClubName ASC";

            var sql = $@"
;WITH ranked AS
(
    SELECT
        u.ClubID AS ClubId,
        c.Name AS ClubName,
        r.UserId,
        u.FirstName AS UserFirstName,
        u.LastName AS UserLastName,
        u.YearOfBirth AS YearOfBirth,
        CAST(r.ValueTime AS decimal(10,2)) AS ValueTime,
        r.ResultDate,
        r.Location,
        ROW_NUMBER() OVER (
            PARTITION BY u.ClubID
            ORDER BY {bestOrder}
        ) AS rn
    FROM Results r
    INNER JOIN Users u ON u.Id = r.UserId
    LEFT JOIN Clubs c ON c.Id = u.ClubID
    WHERE r.DisciplineId = @DisciplineId
      AND ISNULL(u.ClubID, 0) > 0
)
SELECT
    ClubId,
    ClubName,
    UserId,
    UserFirstName,
    UserLastName,
    YearOfBirth,
    ValueTime,
    ResultDate,
    Location
FROM ranked
WHERE rn = 1
ORDER BY {finalOrder};";

            return await connection.QueryAsync<ClubBestResultDto>(sql, new
            {
                DisciplineId = disciplineId
            });
        }
    }
}