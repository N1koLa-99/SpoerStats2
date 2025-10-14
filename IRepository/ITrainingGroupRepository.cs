using SpoerStats2.Models;

namespace SpoerStats2.Repository
{
    public interface ITrainingGroupRepository
    {
        Task<int> CreateAsync(TrainingGroup g);
        Task<bool> UpdateAsync(TrainingGroup g);
        Task<bool> DeleteAsync(int id);                    // твърдо триене; слотовете падат по FK CASCADE
        Task<TrainingGroup?> GetByIdAsync(int id);
        Task<IEnumerable<TrainingGroup>> GetByClubAsync(int clubId);
    }
}
