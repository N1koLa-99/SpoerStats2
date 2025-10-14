using SpoerStats2.Models;

namespace SpoerStats2.Repository
{
    public interface IScheduleSlotRepository
    {
        Task<int> CreateAsync(ScheduleSlot s);
        Task<bool> UpdateAsync(ScheduleSlot s);
        Task<bool> DeleteAsync(int id);

        Task<ScheduleSlot?> GetByIdAsync(int id);
        Task<IEnumerable<ScheduleSlot>> GetByGroupAsync(int groupId);
        Task<IEnumerable<ScheduleSlot>> GetByClubSeasonAsync(int clubId, int? clubSeasonId); // seasonId NULL = общи
    }
}
