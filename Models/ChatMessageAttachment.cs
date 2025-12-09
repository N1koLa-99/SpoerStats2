namespace SpoerStats2.Models
{
    public class ChatMessageAttachment
    {
        public long Id { get; set; }
        public long ChatMessageId { get; set; }
        public string FileUrl { get; set; } = "";
        public string FileType { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }      // <-- DateTimeOffset
    }
}
