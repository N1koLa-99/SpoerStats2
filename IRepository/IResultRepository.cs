using SpoerStats2.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpoerStats2.Repository
{
    public interface IResultRepository
    {
        Task<IEnumerable<Result>> GetAllResults();
        Task<Result> GetResultById(int id);
        Task<IEnumerable<Result>> GetResultsByUserId(int userId);
        Task<IEnumerable<Result>> GetResultsByUserIds(IEnumerable<int> userIds);

        // старият (raw results) - остави го ако се ползва другаде
        Task<IEnumerable<Result>> GetResultsByClubAndDiscipline(int clubId, int disciplineId);

        // ✅ НОВО: leaderboard за "Най-добри в клуба" (1 ред на атлет)
        Task<IEnumerable<ClubBestResultDto>> GetClubBestResultsByDiscipline(int clubId, int disciplineId);

        Task AddResult(Result result);
        Task<int> AddResultReturnId(Result result);
        Task UpdateResult(Result result);
        Task DeleteResult(int id);
        Task AddResultsBulk(IEnumerable<Result> results);
        Task<IEnumerable<string>> GetExistingSourceKeys(string source, IEnumerable<int> userIds);

        Task<IEnumerable<ResultSplit>> GetSplitsByResultId(int resultId);
        Task AddSplitsBulk(IEnumerable<ResultSplit> splits);
        Task<IEnumerable<ClubBestResultDto>> GetPlatformClubLeadersByDiscipline(int disciplineId);
    }
}