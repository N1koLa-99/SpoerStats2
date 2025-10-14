namespace SpoerStats2.Models
{
    public class ClubFeePlan
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int? ClubSeasonId { get; set; }  // може да е NULL
        public byte TrainingsPerWeek { get; set; } // 1..7
        public decimal MonthlyFee { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
