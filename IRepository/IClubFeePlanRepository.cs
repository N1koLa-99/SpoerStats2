using SpoerStats2.Models;

namespace SpoerStats2.Repository
{
    public interface IClubFeePlanRepository
    {
        Task<int> CreateAsync(ClubFeePlan plan);
        Task<bool> UpdateAsync(ClubFeePlan plan);                 // редакция (вкл. цена/валидност)
        Task<bool> ArchiveAsync(int id, DateTime validTo);        // задава ValidTo
        Task<ClubFeePlan?> GetByIdAsync(int id);
        Task<IEnumerable<ClubFeePlan>> GetByClubAsync(int clubId, int? clubSeasonId = null);
        Task<ClubFeePlan?> GetActiveAsync(int clubId, int trainingsPerWeek, int? clubSeasonId = null, DateTime? onDate = null);
    }
}
