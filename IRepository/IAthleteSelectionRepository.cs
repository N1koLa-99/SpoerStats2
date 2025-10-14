using SpoerStats2.Models;

namespace SpoerStats2.Repository
{
    public interface IAthleteSelectionRepository
    {
        Task<int> AddAsync(AthleteSlotSelection s);
        Task<bool> RemoveAsync(int id);
        Task<bool> RemoveByUserAndSlotAsync(int userId, int slotId);
        Task<IEnumerable<AthleteSlotSelection>> GetByUserAsync(int userId);
        Task<IEnumerable<AthleteSlotSelection>> GetBySlotAsync(int slotId);
        Task<int> CountBySlotAsync(int slotId);

        // помощни
        Task<int?> GetSlotClubIdAsync(int slotId);
        Task<int?> GetUserClubIdAsync(int userId);

        // НОВО: потребители, записани за слот
        Task<IEnumerable<User>> GetUsersBySlotAsync(int slotId);
    }
}
