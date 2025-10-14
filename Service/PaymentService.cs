// Service/PaymentService.cs
using SpoerStats2.Models;
using SpoerStats2.Repository;
using System.Data.SqlClient;
using Dapper;
using static SpoerStats2.Models.ExpectedDtos;

public class PaymentService
{
    private readonly IPaymentRepository _repo;
    private readonly string _cs;

    public PaymentService(IPaymentRepository repo, IConfiguration cfg)
    {
        _repo = repo;
        _cs = cfg.GetConnectionString("DefaultConnection");
    }

    public Task<Payment?> GetAsync(int id) => _repo.GetAsync(id);
    public Task<IEnumerable<Payment>> GetByUserAsync(int userId) => _repo.GetByUserAsync(userId);
    public Task<Payment?> GetByKeyAsync(int clubId, int userId, string yearMonth) => _repo.GetByKeyAsync(clubId, userId, yearMonth);

    /// <summary>Отбелязва „платено“: AmountPaid=AmountDue, PaidOn=now.</summary>
    public async Task<int> MarkPaidAsync(int clubId, int userId, string yearMonth, int? recordedByUserId = null)
    {
        // вземи дължимото от изгледа (вкл. SPECIAL)
        using var con = new SqlConnection(_cs);
        var amountDue = await con.ExecuteScalarAsync<decimal?>(
            "SELECT MonthlyFee FROM dbo.v_CurrentMonthlyDue WHERE ClubId=@clubId AND UserId=@userId",
            new { clubId, userId }) ?? 0m;

        return await _repo.UpsertPaidAsync(clubId, userId, yearMonth, amountDue, "CASH", recordedByUserId);
    }

    public Task<bool> UpdateAmountsAsync(int id, decimal due, decimal paid, DateTime? paidOn, string? method)
        => _repo.UpdateAmountsAsync(id, due, paid, paidOn, method);

    public Task<IEnumerable<Payment>> GetByMonthAsync(int clubId, string yearMonth) => _repo.GetByMonthAsync(clubId, yearMonth);
    public Task<IEnumerable<User>> GetUnpaidByMonthAsync(int clubId, string yearMonth) => _repo.GetUnpaidByMonthAsync(clubId, yearMonth);
    public Task<IEnumerable<User>> GetPaidByMonthAsync(int clubId, string yearMonth) => _repo.GetPaidByMonthAsync(clubId, yearMonth);

    public Task<bool> MarkUnpaidAsync(int clubId, int userId, string yearMonth)
        => _repo.MarkUnpaidAsync(clubId, userId, yearMonth);

    public Task<IEnumerable<VwClubMonthlyExpectedRevenue>> GetExpectedRevenueAsync(int clubId) => _repo.GetExpectedRevenueAsync(clubId);

    public async Task<IEnumerable<VwClubMonthlyExpectedRevenue>> GetExpectedRevenueV2Async(int clubId)
    {
        using var con = new SqlConnection(_cs);
        const string sql = @"
SELECT ClubSeasonId, ClubId, YearMonth, AthletesCount, ExpectedAmount
FROM dbo.v_ClubMonthlyExpectedRevenue_v2
WHERE ClubId = @clubId
ORDER BY YearMonth;";
        return await con.QueryAsync<VwClubMonthlyExpectedRevenue>(sql, new { clubId });
    }

    public async Task<IEnumerable<VwExpectedPerUser>> GetExpectedPerUserV2Async(int clubId, string yearMonth)
    {
        using var con = new SqlConnection(_cs);
        const string sql = @"
SELECT ClubId, ClubSeasonId, YearMonth, UserId, TrainingsPerWeek, MonthlyFee
FROM dbo.v_ExpectedPerUser_v2
WHERE ClubId=@clubId AND YearMonth=@yearMonth
ORDER BY MonthlyFee DESC, UserId;";
        return await con.QueryAsync<VwExpectedPerUser>(sql, new { clubId, yearMonth });
    }

    public Task<ExpectedByUserResult> GetExpectedByUserAsync(int clubId, string ym, int? seasonId)
        => _repo.GetExpectedByUserAsync(clubId, ym, seasonId);
}
