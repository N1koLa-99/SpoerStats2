namespace SpoerStats2.Models
{
    public class TrainingGroup
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
