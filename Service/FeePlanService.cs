using SpoerStats2.Models;
using SpoerStats2.Repository;

public class FeePlanService
{
    private readonly IClubFeePlanRepository _repo;
    public FeePlanService(IClubFeePlanRepository repo) => _repo = repo;

    public Task<int> CreateAsync(ClubFeePlan p) => _repo.CreateAsync(p);
    public Task<bool> UpdateAsync(ClubFeePlan p) => _repo.UpdateAsync(p);
    public Task<bool> ArchiveAsync(int id, DateTime validTo) => _repo.ArchiveAsync(id, validTo);
    public Task<ClubFeePlan?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);
    public Task<IEnumerable<ClubFeePlan>> GetByClubAsync(int clubId, int? clubSeasonId = null) => _repo.GetByClubAsync(clubId, clubSeasonId);

    public Task<ClubFeePlan?> GetActiveAsync(int clubId, int trainingsPerWeek, int? clubSeasonId = null, DateTime? onDate = null)
        => _repo.GetActiveAsync(clubId, trainingsPerWeek, clubSeasonId, onDate);
}
