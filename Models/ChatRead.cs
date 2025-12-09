namespace SpoerStats2.Models
{
    public class ChatRead
    {
        public int ChatId { get; set; }
        public int UserId { get; set; }
        public DateTime LastReadAt { get; set; }
    }
}
