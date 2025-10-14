namespace SpoerStats2.Models
{
    public class Payment
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int UserId { get; set; }
        /// <summary>'YYYY-MM'</summary>
        public string YearMonth { get; set; } = string.Empty;
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
        public bool IsPaid { get; set; }        // computed в БД, но Dapper ще го мапне
        public DateTime? PaidOn { get; set; }
        public string? Method { get; set; }     // 'CASH' / 'ONLINE'
        public string? ExternalPaymentId { get; set; }
        public int? PayerUserId { get; set; }
        public int? RecordedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
