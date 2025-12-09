using SpoerStats2.IRepository;
using SpoerStats2.Models;

namespace SpoerStats2.Services
{
    public class ChatService
    {
        private readonly IChatRepository _repo;
        public ChatService(IChatRepository repo) => _repo = repo;

        public Task<(int Id, int RoleID, int ClubID)> GetUserBriefAsync(int userId)
            => _repo.GetUserBriefAsync(userId);

        public async Task<int> CreateChatAsync(int creatorUserId, string name, IEnumerable<int> memberIds)
        {
            var (id, role, club) = await _repo.GetUserBriefAsync(creatorUserId);
            if (!(role == 2 || role == 3))
                throw new UnauthorizedAccessException("Само RoleID 2/3 могат да създават чатове.");

            return await _repo.CreateChatAsync(club, name, creatorUserId, memberIds ?? Array.Empty<int>());
        }

        public Task<bool> IsMemberAsync(int chatId, int userId) => _repo.IsMemberAsync(chatId, userId);
        public Task<bool> IsAdminAsync(int chatId, int userId) => _repo.IsAdminAsync(chatId, userId);

        public Task<long> SendMessageAsync(int chatId, int senderId, string? content)
            => _repo.AddMessageAsync(new ChatMessage { ChatId = chatId, SenderId = senderId, Content = content ?? "" });

        public Task AddAttachmentAsync(long messageId, string url, string contentType)
            => _repo.AddAttachmentAsync(new ChatMessageAttachment { ChatMessageId = messageId, FileUrl = url, FileType = contentType });

        public Task<IEnumerable<object>> GetMyChatsAsync(int meUserId) => _repo.GetMyChatsAsync(meUserId);
        public Task<IEnumerable<ChatMessage>> GetMessagesAsync(int chatId, long? afterId, int take) => _repo.GetMessagesAsync(chatId, afterId, take);
        public Task MarkReadAsync(int chatId, int userId, DateTime atUtc) => _repo.MarkReadAsync(chatId, userId, atUtc);

        public Task<IEnumerable<ChatMemberDto>> GetMembersAsync(int chatId)
            => _repo.GetMembersAsync(chatId);

        public Task MergeMembersAsync(int chatId, int adminUserId, IEnumerable<int> addUserIds, IEnumerable<int> removeUserIds)
            => _repo.MergeMembersAsync(chatId, adminUserId, addUserIds ?? Array.Empty<int>(), removeUserIds ?? Array.Empty<int>());

        public Task LeaveAsync(int chatId, int userId) => _repo.LeaveAsync(chatId, userId);

        public Task<bool> DeleteMessageAsync(int chatId, long messageId, int requesterId)
            => _repo.DeleteMessageAsync(chatId, messageId, requesterId);
    }
}
