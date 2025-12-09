namespace SpoerStats2.Models
{
    public class ChatMember
    {
        public int ChatId { get; set; }
        public int UserId { get; set; }
        public bool IsAdmin { get; set; }
        public DateTime JoinedAt { get; set; }
    }
}
