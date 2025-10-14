// ClassRepository/PaymentRepository.cs
using System.Data.SqlClient;
using Dapper;
using SpoerStats2.Models;
using SpoerStats2.Repository;
using static SpoerStats2.Models.ExpectedDtos;

namespace SpoerStats2.ClassRepository
{
    public class PaymentRepository : IPaymentRepository
    {
        private readonly string _cs;
        public PaymentRepository(IConfiguration cfg) => _cs = cfg.GetConnectionString("DefaultConnection");

        /* ========= CRUD / Плащания ========= */

        public async Task<Payment?> GetAsync(int id)
        {
            using var con = new SqlConnection(_cs);
            return await con.QuerySingleOrDefaultAsync<Payment>(
                "SELECT * FROM dbo.Payments WHERE Id=@id", new { id });
        }

        public async Task<IEnumerable<Payment>> GetByUserAsync(int userId)
        {
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<Payment>(
                "SELECT * FROM dbo.Payments WHERE UserId=@userId ORDER BY YearMonth", new { userId });
        }

        public async Task<Payment?> GetByKeyAsync(int clubId, int userId, string yearMonth)
        {
            using var con = new SqlConnection(_cs);
            return await con.QuerySingleOrDefaultAsync<Payment>(
                @"SELECT * 
                  FROM dbo.Payments 
                  WHERE ClubId=@clubId AND UserId=@userId AND YearMonth=@yearMonth",
                new { clubId, userId, yearMonth });
        }

        public async Task<int> UpsertPaidAsync(int clubId, int userId, string yearMonth, decimal amountDue, string method, int? recordedByUserId)
        {
            // Винаги връщаме реалното Id (и при UPDATE, и при INSERT) чрез OUTPUT inserted.Id
            const string sql = @"
MERGE dbo.Payments AS t
USING (SELECT @clubId ClubId, @userId UserId, @yearMonth YearMonth) s
  ON (t.ClubId=s.ClubId AND t.UserId=s.UserId AND t.YearMonth=s.YearMonth)
WHEN MATCHED THEN
  UPDATE SET AmountDue=@amountDue, AmountPaid=@amountDue, PaidOn=SYSUTCDATETIME(), Method=@method, RecordedByUserId=@rec
WHEN NOT MATCHED THEN
  INSERT(ClubId,UserId,YearMonth,AmountDue,AmountPaid,PaidOn,Method,RecordedByUserId)
  VALUES(@clubId,@userId,@yearMonth,@amountDue,@amountDue,SYSUTCDATETIME(),@method,@rec)
OUTPUT inserted.Id;";
            using var con = new SqlConnection(_cs);
            return await con.ExecuteScalarAsync<int>(sql, new
            {
                clubId,
                userId,
                yearMonth,
                amountDue,
                method,
                rec = recordedByUserId
            });
        }

        public async Task<bool> UpdateAmountsAsync(int id, decimal amountDue, decimal amountPaid, DateTime? paidOn, string? method)
        {
            const string sql = @"
UPDATE dbo.Payments
SET AmountDue=@amountDue, AmountPaid=@amountPaid, PaidOn=@paidOn, Method=@method
WHERE Id=@id;";
            using var con = new SqlConnection(_cs);
            return (await con.ExecuteAsync(sql, new { id, amountDue, amountPaid, paidOn, method })) > 0;
        }

        public async Task<IEnumerable<Payment>> GetByMonthAsync(int clubId, string yearMonth)
        {
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<Payment>(
                @"SELECT * 
                  FROM dbo.Payments 
                  WHERE ClubId=@clubId AND YearMonth=@yearMonth 
                  ORDER BY UserId",
                new { clubId, yearMonth });
        }

        // Неплатили: няма запис за месеца ИЛИ няма PaidOn ИЛИ AmountPaid < AmountDue
        public async Task<IEnumerable<User>> GetUnpaidByMonthAsync(int clubId, string yearMonth)
        {
            const string sql = @"
SELECT u.*
FROM dbo.Users u
JOIN dbo.v_CurrentMonthlyDue d 
  ON d.UserId = u.Id AND d.ClubId = u.ClubID
LEFT JOIN dbo.Payments p 
  ON p.ClubId = u.ClubID AND p.UserId = u.Id AND p.YearMonth = @yearMonth
WHERE u.ClubID = @clubId 
  AND u.RoleID = 1
  AND (
        p.Id IS NULL
        OR p.PaidOn IS NULL
        OR ISNULL(p.AmountPaid, 0) < ISNULL(p.AmountDue, 0)
      );";
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<User>(sql, new { clubId, yearMonth });
        }

        // Платили: има запис за месеца, PaidOn != NULL и AmountPaid >= AmountDue
        public async Task<IEnumerable<User>> GetPaidByMonthAsync(int clubId, string yearMonth)
        {
            const string sql = @"
SELECT u.*
FROM dbo.Users u
JOIN dbo.Payments p
  ON p.ClubId = u.ClubID AND p.UserId = u.Id AND p.YearMonth = @yearMonth
WHERE u.ClubID = @clubId 
  AND u.RoleID = 1
  AND p.PaidOn IS NOT NULL
  AND ISNULL(p.AmountPaid, 0) >= ISNULL(p.AmountDue, 0);";
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<User>(sql, new { clubId, yearMonth });
        }

        // Отмятане като неплатен: не трием реда — зануляваме AmountPaid и PaidOn
        public async Task<bool> MarkUnpaidAsync(int clubId, int userId, string yearMonth)
        {
            const string sql = @"
UPDATE dbo.Payments
SET AmountPaid = 0,
    PaidOn = NULL,
    Method = NULL
WHERE ClubId = @clubId AND UserId = @userId AND YearMonth = @yearMonth;";
            using var con = new SqlConnection(_cs);
            var rows = await con.ExecuteAsync(sql, new { clubId, userId, yearMonth });
            return rows > 0;
        }

        public async Task<IEnumerable<VwClubMonthlyExpectedRevenue>> GetExpectedRevenueAsync(int clubId)
        {
            using var con = new SqlConnection(_cs);
            return await con.QueryAsync<VwClubMonthlyExpectedRevenue>(
                @"SELECT * 
                  FROM dbo.v_ClubMonthlyExpectedRevenue 
                  WHERE ClubId=@clubId 
                  ORDER BY YearMonth",
                new { clubId });
        }

        /* ========= НОВО: очаквани такси по потребител (по избрани слотове) =========
           - броим DISTINCT Weekday от ScheduleSlots за избраните от потребителя SlotId
           - ако подадеш seasonId → филтрираме по ss.ClubSeasonId
           - план за таксата: първо сезонен (ако има), иначе общ (ClubSeasonId IS NULL)
           - параметър ym се използва само за валидност на плановете (ValidFrom/ValidTo)
        */
        public async Task<ExpectedByUserResult> GetExpectedByUserAsync(int clubId, string ym, int? seasonId)
        {
            var firstOfMonth = DateTime.Parse($"{ym}-01");

            using var con = new SqlConnection(_cs);
            await con.OpenAsync();

            // 1) Слотове/седмица на потребител
            const string selSql = @"
SELECT ass.UserId, ss.Id AS SlotId
FROM dbo.AthleteSlotSelections ass
JOIN dbo.ScheduleSlots  ss ON ss.Id = ass.SlotId
JOIN dbo.TrainingGroups g  ON g.Id = ss.GroupId
WHERE g.ClubId = @clubId
  AND (@seasonId IS NULL OR ss.ClubSeasonId = @seasonId);";

            var pairs = (await con.QueryAsync<(int UserId, int SlotId)>(
                selSql, new { clubId, seasonId })).ToList();

            if (pairs.Count == 0)
                return new ExpectedByUserResult(ym, seasonId, 0, 0m, Enumerable.Empty<ExpectedUserRow>());

            var tpwByUser = pairs
                .GroupBy(p => p.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.SlotId).Distinct().Count());

            var userIds = tpwByUser.Keys.ToArray();

            // 2) Имена
            const string usersSql = @"SELECT Id, FirstName, LastName
                              FROM dbo.Users
                              WHERE ClubID=@clubId AND Id IN @userIds;";
            var users = (await con.QueryAsync<(int Id, string FirstName, string LastName)>(
                            usersSql, new { clubId, userIds }))
                        .ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}".Trim());

            // 3) Валидни планове
            const string plansSql = @"
SELECT TrainingsPerWeek, MonthlyFee, ValidFrom, ValidTo, ClubSeasonId
FROM dbo.ClubFeePlans
WHERE ClubId = @clubId
  AND ValidFrom <= @firstOfMonth
  AND (ValidTo IS NULL OR ValidTo >= @firstOfMonth)
  AND (@seasonId IS NULL OR ClubSeasonId = @seasonId OR ClubSeasonId IS NULL);";

            var plans = (await con.QueryAsync<(int TPW, decimal Fee, DateTime ValidFrom, DateTime? ValidTo, int? ClubSeasonId)>(
                plansSql, new { clubId, firstOfMonth, seasonId }))
                .Select(p => (TPW: p.TPW, Fee: p.Fee, ValidFrom: p.ValidFrom, ClubSeasonId: p.ClubSeasonId))
                .ToList();

            decimal ResolveFee(int tpw)
            {
                if (seasonId.HasValue)
                {
                    var seasonal = plans
                        .Where(p => p.TPW == tpw && p.ClubSeasonId == seasonId.Value)
                        .OrderByDescending(p => p.ValidFrom)
                        .FirstOrDefault();

                    if (!seasonal.Equals(default((int, decimal, DateTime, int?))))
                        return seasonal.Fee;
                }

                var generic = plans
                    .Where(p => p.TPW == tpw && p.ClubSeasonId == null)
                    .OrderByDescending(p => p.ValidFrom)
                    .FirstOrDefault();

                return generic.Equals(default((int, decimal, DateTime, int?))) ? 0m : generic.Fee;
            }

            // 4) Редове
            var rows = tpwByUser.Select(kv =>
            {
                var id = kv.Key;
                var tpw = kv.Value;
                return new ExpectedUserRow(
                    id,
                    users.TryGetValue(id, out var full) ? full : $"#{id}",
                    tpw,
                    ResolveFee(tpw)
                );
            })
            .OrderBy(r => r.FullName)
            .ToList();

            var total = rows.Sum(r => r.Fee);

            return new ExpectedByUserResult(
                YearMonth: ym,
                SeasonId: seasonId,
                Count: rows.Count,
                Total: total,
                Rows: rows
            );
        }
    }
}
