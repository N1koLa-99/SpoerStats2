using SpoerStats2.Models;
using SpoerStats2.IRepository;

public class MessageService
{
    private readonly IMessageRepository _messageRepository;

    public MessageService(IMessageRepository messageRepository)
    {
        _messageRepository = messageRepository;
    }

    public Task<IEnumerable<TeamMessage>> GetMessagesByTeam(int teamId)
        => _messageRepository.GetMessagesByTeam(teamId);

    public Task<int> SendMessage(TeamMessage message)
        => _messageRepository.AddMessage(message);

    public Task AddAttachment(MessageAttachment attachment)
        => _messageRepository.AddAttachment(attachment);

    public Task<bool> DeleteMessage(int messageId, int userId)
        => _messageRepository.DeleteMessage(messageId, userId);
}