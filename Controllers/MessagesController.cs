using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using SpoerStats2.Services; // за BlobStorageService
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpoerStats2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MessagesController : ControllerBase
    {
        private readonly MessageService _messageService;
        private readonly BlobStorageService _blob;

        // Лимити и позволени типове (можеш да ги изнесеш в appsettings по-късно)
        private const long MaxFileSize = 10L * 1024 * 1024; // 10 MB на файл
        private static readonly HashSet<string> AllowedContentTypes = new()
        {
            "image/jpeg","image/png","image/webp","image/gif","application/pdf"
        };

        public MessagesController(MessageService messageService, BlobStorageService blob)
        {
            _messageService = messageService;
            _blob = blob;
        }

        // Връща всички съобщения за даден отбор
        [HttpGet("{teamId}")]
        public async Task<ActionResult<IEnumerable<TeamMessage>>> GetMessages(int teamId)
        {
            var messages = await _messageService.GetMessagesByTeam(teamId);
            return Ok(messages);
        }

        // НОВО: Създава съобщение + качва файлове (multipart/form-data)
        [HttpPost("send")]
        [RequestSizeLimit(60_000_000)] // ~60MB общо на заявката
        public async Task<ActionResult> Send([FromForm] SendMessageRequest form, CancellationToken ct)
        {
            // TODO: в прод да взимаш SenderId от JWT, не от формата
            var msgId = await _messageService.SendMessage(new TeamMessage
            {
                TeamId = form.TeamId,
                SenderId = form.SenderId,
                Content = form.Content
            });

            if (form.Files is { Count: > 0 })
            {
                foreach (var file in form.Files.Where(f => f?.Length > 0))
                {
                    if (file.Length > MaxFileSize)
                        return BadRequest($"Файлът {file.FileName} надвишава 10 MB.");

                    var ctType = string.IsNullOrWhiteSpace(file.ContentType)
                        ? "application/octet-stream"
                        : file.ContentType;

                    if (!AllowedContentTypes.Contains(ctType))
                        return BadRequest($"Неподдържан тип: {ctType}. Разрешени: изображения и PDF.");

                    await using var stream = file.OpenReadStream();
                    var (url, storedCt) = await _blob.UploadAsync(stream, file.FileName, ctType, ct);

                    await _messageService.AddAttachment(new MessageAttachment
                    {
                        MessageId = msgId,
                        FileUrl = url,
                        FileType = storedCt
                    });
                }
            }

            return Ok(new { messageId = msgId });
        }

        // (По избор) Старият JSON endpoint може да остане за back-compat
        [HttpPost]
        public async Task<ActionResult> SendMessage([FromBody] TeamMessage message)
        {
            var newId = await _messageService.SendMessage(message);
            return Ok(new { messageId = newId });
        }

        // (По избор) ако държиш да добавяш прикачени по-късно чрез JSON URL
        [HttpPost("{messageId}/attachments")]
        public async Task<ActionResult> AddAttachment(int messageId, [FromBody] MessageAttachment attachment)
        {
            attachment.MessageId = messageId;
            await _messageService.AddAttachment(attachment);
            return Ok();
        }

        // Изтриване на съобщение (само от създателя му)
        [HttpDelete("{messageId}/user/{userId}")]
        public async Task<ActionResult> DeleteMessage(int messageId, int userId)
        {
            var success = await _messageService.DeleteMessage(messageId, userId);
            if (!success) return Forbid("Нямате право да изтриете това съобщение!");
            return Ok("Съобщението е изтрито.");
        }
    }
}
