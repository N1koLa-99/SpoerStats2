namespace SpoerStats2.Models
{
    public class SendChatMessageRequest
    {
        public int ChatId { get; set; }
        public int SenderId { get; set; } // ако не ползваш JWT
        public string? Content { get; set; }
        public IFormFileCollection? Files { get; set; }
    }
}
