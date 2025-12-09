namespace SpoerStats2.Models
{
    public class AddMembersRequest
    {
        public List<int> AddUserIds { get; set; } = new();
        public List<int> RemoveUserIds { get; set; } = new();
    }
}
