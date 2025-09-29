using System.Collections.Generic;

namespace SpoerStats2.Models
{
    public class MediaDto
    {
        public string Url { get; set; }
        public string MediaType { get; set; } // "image","video","file"
    }

    public class SendMessageDto
    {
        public string MessageText { get; set; }
        public List<MediaDto> Media { get; set; }
    }
}