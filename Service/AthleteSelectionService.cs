// Service/AthleteSelectionService.cs
using System.Data;
using System.Data.SqlClient;
using Dapper;
using SpoerStats2.Models;
using SpoerStats2.Repository;

public class AthleteSelectionService
{
    private readonly IAthleteSelectionRepository _repo;
    private readonly string _cs;

    public AthleteSelectionService(IAthleteSelectionRepository repo, IConfiguration cfg)
    {
        _repo = repo;
        _cs = cfg.GetConnectionString("DefaultConnection");
    }

    public async Task<int> AddAsync(int userId, int slotId)
    {
        // 1) клубен контрол
        var userClub = await _repo.GetUserClubIdAsync(userId)
            ?? throw new InvalidOperationException("Потребителят не е валиден.");
        var slotClub = await _repo.GetSlotClubIdAsync(slotId)
            ?? throw new InvalidOperationException("Слотът не е валиден.");
        if (userClub != slotClub)
            throw new InvalidOperationException("Слотът не принадлежи на клуба на състезателя.");

        // 2) капацитет (ако има)
        // вземи Capacity и текущ брой
        const string sql = @"SELECT Capacity FROM dbo.ScheduleSlots WHERE Id=@slotId;";
        using var con = new SqlConnection(_cs);
        var capacity = await con.ExecuteScalarAsync<int?>(sql, new { slotId });
        if (capacity.HasValue && capacity.Value > 0)
        {
            var taken = await _repo.CountBySlotAsync(slotId);
            if (taken >= capacity.Value)
                throw new InvalidOperationException("Слотът е запълнен.");
        }

        // 3) insert
        var s = new AthleteSlotSelection { ClubId = userClub, UserId = userId, SlotId = slotId };
        return await _repo.AddAsync(s);
    }

    public Task<bool> RemoveAsync(int id) => _repo.RemoveAsync(id);
    public Task<bool> RemoveByUserAndSlotAsync(int userId, int slotId) => _repo.RemoveByUserAndSlotAsync(userId, slotId);
    public Task<IEnumerable<AthleteSlotSelection>> GetByUserAsync(int userId) => _repo.GetByUserAsync(userId);
    public Task<IEnumerable<AthleteSlotSelection>> GetBySlotAsync(int slotId) => _repo.GetBySlotAsync(slotId);

    /// <summary>Заменя всички избори на потребителя с подадения набор от слотове (транзакционно).</summary>
    public async Task ReplaceUserSelectionsAsync(int userId, IEnumerable<int> slotIds)
    {
        var ids = slotIds?.Distinct().ToArray() ?? Array.Empty<int>();
        // транзакция: триене -> вкарване
        using var con = new SqlConnection(_cs);
        await con.OpenAsync();
        using var tx = con.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            await con.ExecuteAsync("DELETE dbo.AthleteSlotSelections WHERE UserId=@userId", new { userId }, tx);

            foreach (var slotId in ids)
            {
                // ползвай AddAsync логиката за проверки
                // но тук сме в транзакция, затова директно минимална проверка + insert

                // клубен контрол
                var userClub = await con.ExecuteScalarAsync<int?>("SELECT ClubID FROM dbo.Users WHERE Id=@userId", new { userId }, tx);
                var slotClub = await con.ExecuteScalarAsync<int?>(
                    @"SELECT g.ClubId FROM dbo.ScheduleSlots ss JOIN dbo.TrainingGroups g ON g.Id=ss.GroupId WHERE ss.Id=@slotId",
                    new { slotId }, tx);

                if (userClub == null || slotClub == null || userClub != slotClub)
                    throw new InvalidOperationException("Невалидна комбинация състезател/слот.");

                // capacity
                var cap = await con.ExecuteScalarAsync<int?>("SELECT Capacity FROM dbo.ScheduleSlots WHERE Id=@slotId", new { slotId }, tx);
                if (cap.HasValue && cap.Value > 0)
                {
                    var taken = await con.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.AthleteSlotSelections WHERE SlotId=@slotId", new { slotId }, tx);
                    if (taken >= cap.Value)
                        throw new InvalidOperationException("Слотът е запълнен.");
                }

                await con.ExecuteAsync(
                    @"INSERT dbo.AthleteSlotSelections(ClubId,UserId,SlotId) VALUES(@ClubId,@UserId,@SlotId)",
                    new { ClubId = userClub, UserId = userId, SlotId = slotId }, tx);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
    public Task<IEnumerable<User>> GetUsersBySlotAsync(int slotId)
        => _repo.GetUsersBySlotAsync(slotId);



    // ---- Reports helpers ----

    public async Task<VwCurrentMonthlyDue?> GetCurrentMonthlyDueAsync(int clubId, int userId)
    {
        using var con = new SqlConnection(_cs);
        const string sql = @"SELECT TOP 1 * FROM dbo.v_CurrentMonthlyDue WHERE ClubId=@clubId AND UserId=@userId;";
        return await con.QuerySingleOrDefaultAsync<VwCurrentMonthlyDue>(sql, new { clubId, userId });
    }

    public async Task<IEnumerable<VwAthleteTrainingsPerWeek>> GetTrainingsPerWeekAsync(int clubId)
    {
        using var con = new SqlConnection(_cs);
        const string sql = @"SELECT * FROM dbo.v_AthleteTrainingsPerWeek WHERE ClubId=@clubId;";
        return await con.QueryAsync<VwAthleteTrainingsPerWeek>(sql, new { clubId });
    }

    public async Task<IEnumerable<User>> GetAthletesWithoutSelectionsAsync(int clubId)
    {
        using var con = new SqlConnection(_cs);
        const string sql = @"SELECT u.* FROM dbo.v_AthletesWithoutSelections v JOIN dbo.Users u ON u.Id=v.UserId WHERE v.ClubId=@clubId;";
        return await con.QueryAsync<User>(sql, new { clubId });
    }
}
