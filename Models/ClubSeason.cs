namespace SpoerStats2.Models
{
    public class ClubSeason
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? CreatedByUserId { get; set; }
    }
}
