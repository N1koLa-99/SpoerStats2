using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using SpoerStats2.Services;
using System.Data.SqlClient;

namespace SpoerStats2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatsController : ControllerBase
    {
        private readonly ChatService _svc;
        private readonly BlobStorageService _blob;

        public ChatsController(ChatService svc, BlobStorageService blob)
        {
            _svc = svc;
            _blob = blob;
        }

        // Помощна: чете Requester-Id от хедъра
        private bool TryGetRequesterId(out int requesterId)
        {
            requesterId = 0;
            var s = HttpContext.Request.Headers["Requester-Id"].ToString();
            return !string.IsNullOrWhiteSpace(s) && int.TryParse(s, out requesterId) && requesterId > 0;
        }

        // Създаване на групов чат (само RoleID 2/3)
        [HttpPost]
        public async Task<ActionResult> Create([FromBody] CreateChatRequest req)
        {
            if (req is null || string.IsNullOrWhiteSpace(req.Name))
                return BadRequest("Name is required.");

            if (!TryGetRequesterId(out var creatorId))
                return BadRequest("Липсва/невалиден Requester-Id.");

            try
            {
                var chatId = await _svc.CreateChatAsync(creatorId, req.Name, req.MemberUserIds ?? Enumerable.Empty<int>());
                return Ok(new { chatId });
            }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (Exception) { return StatusCode(500, "Вътрешна грешка при създаване на чат."); }
        }

        // Моите чатове
        [HttpGet("mine")]
        public async Task<ActionResult> Mine()
        {
            if (!TryGetRequesterId(out var me)) return BadRequest("Липсва/невалиден Requester-Id.");
            try
            {
                var rows = await _svc.GetMyChatsAsync(me);
                return Ok(rows);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }

        // Списък с участниците в чата
        [HttpGet("{chatId:int}/members")]
        public async Task<ActionResult> GetMembers(int chatId)
        {
            if (!TryGetRequesterId(out var me)) return BadRequest("Липсва/невалиден Requester-Id.");

            try
            {
                var isMember = await _svc.IsMemberAsync(chatId, me);
                if (!isMember) return Forbid("Не си член на този чат.");

                var members = await _svc.GetMembersAsync(chatId);
                return Ok(members);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Грешка при зареждане на участници: {ex.Message}");
            }
        }

        // Обединено добавяне/премахване (само админ)
        [HttpPost("{chatId:int}/members")]
        public async Task<ActionResult> MergeMembers(int chatId, [FromBody] AddMembersRequest req)
        {
            if (!TryGetRequesterId(out var me)) return BadRequest("Липсва/невалиден Requester-Id.");
            try
            {
                var isAdmin = await _svc.IsAdminAsync(chatId, me);
                if (!isAdmin) return Forbid("Само админ на чата може да променя членове.");

                await _svc.MergeMembersAsync(chatId, me, req?.AddUserIds ?? Enumerable.Empty<int>(), req?.RemoveUserIds ?? Enumerable.Empty<int>());
                return Ok();
            }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (Exception) { return StatusCode(500, "Вътрешна грешка при промяна на членове."); }
        }

        // Потребителят напуска сам
        [HttpDelete("{chatId:int}/members/me")]
        public async Task<ActionResult> Leave(int chatId)
        {
            if (!TryGetRequesterId(out var me)) return BadRequest("Липсва/невалиден Requester-Id.");
            try
            {
                var isMember = await _svc.IsMemberAsync(chatId, me);
                if (!isMember) return Forbid("Не си член на този чат.");
                await _svc.LeaveAsync(chatId, me);
                return Ok();
            }
            catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
            catch (Exception) { return StatusCode(500, "Вътрешна грешка при напускане на чат."); }
        }

        // Админ маха конкретен член (шорткът)
        [HttpDelete("{chatId:int}/members/{userId:int}")]
        public async Task<ActionResult> RemoveMember(int chatId, int userId)
        {
            if (!TryGetRequesterId(out var me)) return BadRequest("Липсва/невалиден Requester-Id.");
            try
            {
                var isAdmin = await _svc.IsAdminAsync(chatId, me);
                if (!isAdmin) return Forbid("Само админ на чата може да премахва членове.");
                await _svc.MergeMembersAsync(chatId, me, Array.Empty<int>(), new[] { userId });
                return Ok();
            }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (Exception) { return StatusCode(500, "Вътрешна грешка при премахване на член."); }
        }

        // Съобщения – списък
        [HttpGet("{chatId:int}/messages")]
        public async Task<ActionResult> GetMessages(int chatId, [FromQuery] long? afterId, [FromQuery] int take = 50)
        {
            if (!TryGetRequesterId(out var me))
                return BadRequest("Липсва/невалиден Requester-Id.");

            try
            {
                var isMember = await _svc.IsMemberAsync(chatId, me);
                if (!isMember)
                    return Forbid("Не си член на този чат.");

                take = (take > 0 && take <= 200) ? take : 50;

                var list = await _svc.GetMessagesAsync(chatId, afterId, take);
                return Ok(list);
            }
            catch (SqlException ex) when (ex.Number == -2)
            {
                return StatusCode(504, "SQL заявката изтече по време (timeout) при зареждане на съобщения.");
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, "Заявката към БД изтече по време (timeout) при зареждане на съобщения.");
            }
            catch (SqlException ex)
            {
                return StatusCode(500, $"SQL грешка при зареждане на съобщения: ({ex.Number}) {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Вътрешна грешка при зареждане на съобщения: {ex.Message}");
            }
        }

        // Изпращане на съобщение + файлове (multipart)
        [HttpPost("{chatId:int}/messages")]
        [RequestSizeLimit(60_000_000)]
        public async Task<ActionResult> SendMessage(int chatId, [FromForm] string? content, [FromForm] List<IFormFile>? files, CancellationToken ct)
        {
            if (!TryGetRequesterId(out var me)) return BadRequest("Липсва/невалиден Requester-Id.");

            var uploaded = new List<(string url, string ct)>();

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(60));

                if (files is { Count: > 0 })
                {
                    foreach (var f in files.Where(f => f?.Length > 0))
                    {
                        if (f.Length > 25 * 1024 * 1024) return BadRequest("Макс 25MB на файл.");
                        var ctType = string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType;

                        await using var stream = f.OpenReadStream();
                        var (url, storedCt) = await _blob.UploadAsync(stream, f.FileName, ctType, cts.Token);
                        uploaded.Add((url, storedCt));
                    }
                }

                var isMember = await _svc.IsMemberAsync(chatId, me);
                if (!isMember) return Forbid("Не си член на този чат.");

                var msgId = await _svc.SendMessageAsync(chatId, me, content);

                foreach (var (url, storedCt) in uploaded)
                    await _svc.AddAttachmentAsync(msgId, url, storedCt);

                return Ok(new { messageId = msgId });
            }
            catch (SqlException ex)
            {
                return StatusCode(500, $"SQL грешка при изпращане на съобщение: ({ex.Number}) {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return StatusCode(500, "Заявката изтече по време (timeout) при качване на файл/съобщение.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Вътрешна грешка при изпращане на съобщение: {ex.Message}");
            }
        }

        // Триене на съобщение
        [HttpDelete("{chatId:int}/messages/{messageId:long}")]
        public async Task<ActionResult> DeleteMessage(int chatId, long messageId)
        {
            if (!TryGetRequesterId(out var me)) return BadRequest("Липсва/невалиден Requester-Id.");

            try
            {
                var ok = await _svc.DeleteMessageAsync(chatId, messageId, me);
                if (!ok) return Forbid("Нямаш право да изтриеш това съобщение или то не съществува.");

                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Грешка при триене на съобщение: {ex.Message}");
            }
        }

        // Маркирай прочетени
        [HttpPost("{chatId:int}/read")]
        public async Task<ActionResult> MarkRead(int chatId, [FromBody] ReadRequest? dto)
        {
            if (!TryGetRequesterId(out var me)) return BadRequest("Липсва/невалиден Requester-Id.");
            try
            {
                var isMember = await _svc.IsMemberAsync(chatId, me);
                if (!isMember) return Forbid("Не си член на този чат.");

                await _svc.MarkReadAsync(chatId, me, dto?.AtUtc ?? DateTime.UtcNow);
                return Ok();
            }
            catch (SqlException ex)
            {
                return StatusCode(500, $"SQL грешка при MarkRead: ({ex.Number}) {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Вътрешна грешка при MarkRead: {ex.Message}");
            }
        }

        // (по избор) ъплоуд снимка на групата
        [HttpPost("{chatId:int}/photo")]
        public ActionResult UploadPhoto(int chatId, IFormFile file, CancellationToken ct)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, "Добави ChatPhotoService, ако искаш снимка на групата тук.");
        }
    }
}
