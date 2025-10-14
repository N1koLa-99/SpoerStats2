namespace SpoerStats2.Models
{
    public class VwClubMonthlyExpectedRevenue
    {
        public int ClubSeasonId { get; set; }
        public int ClubId { get; set; }
        public string YearMonth { get; set; } = "";
        public int AthletesCount { get; set; }
        public decimal ExpectedAmount { get; set; }
    }
}
