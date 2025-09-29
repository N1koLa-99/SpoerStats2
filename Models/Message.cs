using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SpoerStats2.Models
{
    public class TeamMessage
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public int SenderId { get; set; }
        public string? Content { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<MessageAttachment> Attachments { get; set; } = new();
    }

    public class MessageAttachment
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public string FileUrl { get; set; }
        public string FileType { get; set; }
    }
}