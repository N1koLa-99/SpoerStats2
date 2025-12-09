using SpoerStats2.Models;

namespace SpoerStats2.IRepository
{
    public interface IChatRepository
    {
        Task<(int Id, int RoleID, int ClubID)> GetUserBriefAsync(int userId);
        Task<int> CreateChatAsync(int clubId, string name, int creatorId, IEnumerable<int> memberIds);

        Task<bool> IsMemberAsync(int chatId, int userId);
        Task<bool> IsAdminAsync(int chatId, int userId);

        Task<long> AddMessageAsync(ChatMessage message);
        Task AddAttachmentAsync(ChatMessageAttachment attachment);

        Task<IEnumerable<object>> GetMyChatsAsync(int meUserId);
        Task<IEnumerable<ChatMessage>> GetMessagesAsync(int chatId, long? afterId, int take);
        Task MarkReadAsync(int chatId, int userId, DateTime atUtc);

        // Членове
        Task<IEnumerable<ChatMemberDto>> GetMembersAsync(int chatId);

        Task MergeMembersAsync(int chatId, int adminUserId, IEnumerable<int> addUserIds, IEnumerable<int> removeUserIds);
        Task LeaveAsync(int chatId, int userId);

        // Триене на съобщение
        Task<bool> DeleteMessageAsync(int chatId, long messageId, int requesterId);
    }
}
