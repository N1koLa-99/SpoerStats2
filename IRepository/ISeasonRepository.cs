using SpoerStats2.Models;

namespace SpoerStats2.Repository
{
    public interface ISeasonRepository
    {
        Task<int> CreateSeasonAsync(ClubSeason season);
        Task<bool> UpdateSeasonAsync(ClubSeason season);
        Task<bool> SetSeasonActiveAsync(int clubSeasonId, bool isActive);
        Task<ClubSeason?> GetSeasonByIdAsync(int id);
        Task<IEnumerable<ClubSeason>> GetSeasonsByClubAsync(int clubId);

        // Billing months
        Task<IEnumerable<SeasonBillingMonth>> GetSeasonMonthsAsync(int clubSeasonId);
        Task<int> GenerateSeasonMonthsAsync(int clubSeasonId, bool markBillable = true);
        Task<bool> SetMonthBillableAsync(int id, bool isBillable);
        Task<bool> SetMonthBillableAsync(int clubSeasonId, string yearMonth, bool isBillable);

        // NEW: Delete
        Task<bool> DeleteSeasonAsync(int clubSeasonId); // blocks if active
    }
}
