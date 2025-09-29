using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpoerStats2.Models;

namespace SpoerStats2.IRepository
{
   public interface IMessageRepository
    {
        Task<IEnumerable<TeamMessage>> GetMessagesByTeam(int teamId);
        Task<int> AddMessage(TeamMessage message);
        Task AddAttachment(MessageAttachment attachment);
        Task<bool> DeleteMessage(int messageId, int userId);
    }
}