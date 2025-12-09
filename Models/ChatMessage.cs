namespace SpoerStats2.Models
{
    public class ChatMessage
    {
        public long Id { get; set; }
        public int ChatId { get; set; }
        public int SenderId { get; set; }
        public string Content { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }      // <-- DateTimeOffset
        public List<ChatMessageAttachment> Attachments { get; set; } = new();
    }
}
