namespace SpoerStats2.Models
{
    public class ChatMemberDto
    {
        public int ChatId { get; set; }
        public int UserId { get; set; }

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";

        public bool IsAdmin { get; set; }

        public DateTime JoinedAt { get; set; }
        public DateTime? LastReadAt { get; set; }
    }
}
