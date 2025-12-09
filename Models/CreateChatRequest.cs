namespace SpoerStats2.Models
{
    public class CreateChatRequest
    {
        public string Name { get; set; } = "";
        public List<int> MemberUserIds { get; set; } = new();

        // DEV fallback: ако няма JWT, клиентът подава създател.
        public int? CreatorId { get; set; }
    }
}
