namespace SpoerStats2.Models
{
    public class VwExpectedPerUser
    {
        public int ClubId { get; set; }
        public int ClubSeasonId { get; set; }
        public string YearMonth { get; set; } = "";
        public int UserId { get; set; }
        public int TrainingsPerWeek { get; set; }
        public decimal MonthlyFee { get; set; }
    }
}
