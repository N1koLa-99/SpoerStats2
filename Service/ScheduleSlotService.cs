// Service/ScheduleSlotService.cs
using SpoerStats2.Models;
using SpoerStats2.Repository;

public class ScheduleSlotService
{
    private readonly IScheduleSlotRepository _repo;
    public ScheduleSlotService(IScheduleSlotRepository repo) => _repo = repo;

    public Task<int> CreateAsync(ScheduleSlot s)
    {
        if (s.Weekday < 1 || s.Weekday > 7) throw new ArgumentOutOfRangeException(nameof(s.Weekday), "1..7");
        if (s.StartTime >= s.EndTime) throw new ArgumentException("StartTime must be < EndTime");
        return _repo.CreateAsync(s);
    }

    public Task<bool> UpdateAsync(ScheduleSlot s)
    {
        if (s.Weekday < 1 || s.Weekday > 7) throw new ArgumentOutOfRangeException(nameof(s.Weekday), "1..7");
        if (s.StartTime >= s.EndTime) throw new ArgumentException("StartTime must be < EndTime");
        return _repo.UpdateAsync(s);
    }

    public Task<bool> DeleteAsync(int id) => _repo.DeleteAsync(id);
    public Task<ScheduleSlot?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);
    public Task<IEnumerable<ScheduleSlot>> GetByGroupAsync(int groupId) => _repo.GetByGroupAsync(groupId);
    public Task<IEnumerable<ScheduleSlot>> GetByClubSeasonAsync(int clubId, int? seasonId) => _repo.GetByClubSeasonAsync(clubId, seasonId);
}
