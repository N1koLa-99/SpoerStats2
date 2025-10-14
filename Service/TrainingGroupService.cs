// Service/TrainingGroupService.cs
using SpoerStats2.Models;
using SpoerStats2.Repository;

public class TrainingGroupService
{
    private readonly ITrainingGroupRepository _repo;
    public TrainingGroupService(ITrainingGroupRepository repo) => _repo = repo;

    public Task<int> CreateAsync(TrainingGroup g) => _repo.CreateAsync(g);
    public Task<bool> UpdateAsync(TrainingGroup g) => _repo.UpdateAsync(g);
    public Task<bool> DeleteAsync(int id) => _repo.DeleteAsync(id);
    public Task<TrainingGroup?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);
    public Task<IEnumerable<TrainingGroup>> GetByClubAsync(int clubId) => _repo.GetByClubAsync(clubId);
}
