namespace SpoerStats2.Models
{
    public class VwCurrentMonthlyDue
    {
        public int UserId { get; set; }
        public int ClubId { get; set; }
        public decimal? MonthlyFee { get; set; }   // може да е NULL
        public string FeeSource { get; set; } = ""; // PLAN/SPECIAL
        public int TrainingsPerWeek { get; set; }
    }
}
