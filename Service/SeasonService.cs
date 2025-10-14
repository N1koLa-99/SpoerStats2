using SpoerStats2.Models;
using SpoerStats2.Repository;

public class SeasonService
{
    private readonly ISeasonRepository _repo;
    public SeasonService(ISeasonRepository repo) => _repo = repo;

    public Task<int> CreateSeasonAsync(ClubSeason s) => _repo.CreateSeasonAsync(s);
    public Task<bool> UpdateSeasonAsync(ClubSeason s) => _repo.UpdateSeasonAsync(s);
    public Task<bool> SetSeasonActiveAsync(int id, bool isActive) => _repo.SetSeasonActiveAsync(id, isActive);
    public Task<ClubSeason?> GetSeasonByIdAsync(int id) => _repo.GetSeasonByIdAsync(id);
    public Task<IEnumerable<ClubSeason>> GetSeasonsByClubAsync(int clubId) => _repo.GetSeasonsByClubAsync(clubId);

    public Task<IEnumerable<SeasonBillingMonth>> GetSeasonMonthsAsync(int clubSeasonId) => _repo.GetSeasonMonthsAsync(clubSeasonId);
    public Task<int> GenerateSeasonMonthsAsync(int clubSeasonId, bool markBillable) => _repo.GenerateSeasonMonthsAsync(clubSeasonId, markBillable);
    public Task<bool> SetMonthBillableByIdAsync(int id, bool isBillable) => _repo.SetMonthBillableAsync(id, isBillable);
    public Task<bool> SetMonthBillableAsync(int clubSeasonId, string yearMonth, bool isBillable) => _repo.SetMonthBillableAsync(clubSeasonId, yearMonth, isBillable);
    public Task<bool> DeleteSeasonAsync(int clubSeasonId) => _repo.DeleteSeasonAsync(clubSeasonId);

}
