using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SpoerStats2.Models
{
    public class SendMessageRequest
    {
        [Required] public int TeamId { get; set; }
        [Required] public int SenderId { get; set; } // по-късно го вземи от JWT
        public string? Content { get; set; }
        public List<IFormFile>? Files { get; set; }
    }
}
