namespace SpoerStats2.Models
{
    public class SeasonBillingMonth
    {
        public int Id { get; set; }
        public int ClubSeasonId { get; set; }
        // 'YYYY-MM' от БД – държим го като string за лесна употреба в UI
        public string YearMonth { get; set; } = string.Empty;
        public bool IsBillable { get; set; }
    }
}
