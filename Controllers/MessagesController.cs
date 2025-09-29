using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;

namespace SpoerStats2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MessagesController : ControllerBase
    {
        private readonly MessageService _messageService;

        public MessagesController(MessageService messageService)
        {
            _messageService = messageService;
        }

        // Връща всички съобщения за даден отбор
        [HttpGet("{teamId}")]
        public async Task<ActionResult<IEnumerable<TeamMessage>>> GetMessages(int teamId)
        {
            var messages = await _messageService.GetMessagesByTeam(teamId);
            return Ok(messages);
        }

        // Създава ново съобщение
        [HttpPost]
        public async Task<ActionResult> SendMessage([FromBody] TeamMessage message)
        {
            var newId = await _messageService.SendMessage(message);
            return Ok(new { messageId = newId });
        }

        // Добавяне на прикачен файл към съобщение
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