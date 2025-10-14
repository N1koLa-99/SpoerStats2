// IRepository/IPaymentRepository.cs
using SpoerStats2.Models;
using static SpoerStats2.Models.ExpectedDtos;

namespace SpoerStats2.Repository
{
    public interface IPaymentRepository
    {
        Task<Payment?> GetAsync(int id);
        Task<IEnumerable<Payment>> GetByUserAsync(int userId);
        Task<Payment?> GetByKeyAsync(int clubId, int userId, string yearMonth);
        Task<int> UpsertPaidAsync(int clubId, int userId, string yearMonth, decimal amountDue, string method, int? recordedByUserId);
        Task<bool> UpdateAmountsAsync(int id, decimal amountDue, decimal amountPaid, DateTime? paidOn, string? method);

        // Всички плащания за месец (сурови записи)
        Task<IEnumerable<Payment>> GetByMonthAsync(int clubId, string yearMonth);

        // Неплатили/Платили за месец (по Users)
        Task<IEnumerable<User>> GetUnpaidByMonthAsync(int clubId, string yearMonth);
        Task<IEnumerable<User>> GetPaidByMonthAsync(int clubId, string yearMonth);

        // Отчитане/Отмятане
        Task<bool> MarkUnpaidAsync(int clubId, int userId, string yearMonth);

        // Отчети (изгледи):
        Task<IEnumerable<VwClubMonthlyExpectedRevenue>> GetExpectedRevenueAsync(int clubId);
        Task<ExpectedByUserResult> GetExpectedByUserAsync(int clubId, string ym, int? seasonId);
    }
}
