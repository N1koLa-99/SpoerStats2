namespace SpoerStats2.Models
{
    public class Chat
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public string Name { get; set; } = "";
        public bool IsGroup { get; set; } = true;
        public int CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? PhotoUrl { get; set; }
        public DateTime? PhotoUpdatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
    }
}
