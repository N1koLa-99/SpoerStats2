namespace SpoerStats2.Models
{
    public class MemberBillingSetting
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int UserId { get; set; }
        public int? ClubSeasonId { get; set; }  // NULL = глобално за всички сезони
        public decimal? SpecialMonthlyFee { get; set; } // override ако не е NULL
        public string? Notes { get; set; }
        // SeasonKey е computed в SQL – не го държим тук
    }
}
