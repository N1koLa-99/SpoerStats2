using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using SpoerStats2.Service;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpoerStats2.Controllers
{
    public sealed class LenexUploadDto
    {
        [Required]
        public IFormFile File { get; set; } = default!;
    }

    [Route("api/[controller]")]
    [ApiController]
    public class ResultsController : ControllerBase
    {
        private readonly ResultService _resultService;
        private readonly UserService _userService;
        private readonly LenexImportService _lenexImportService;

        public ResultsController(
            ResultService resultService,
            UserService userService,
            LenexImportService lenexImportService)
        {
            _resultService = resultService;
            _userService = userService;
            _lenexImportService = lenexImportService;
        }

        private static bool IsCoachOrAdmin(int roleId) => roleId == 2 || roleId == 3;

        private ActionResult<T> ForbiddenMsg<T>(string message)
            => StatusCode(StatusCodes.Status403Forbidden, message);

        private IActionResult ForbiddenMsg(string message)
            => StatusCode(StatusCodes.Status403Forbidden, message);

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Result>>> GetResults(
            [FromQuery] int requesterId = 0,
            [FromHeader(Name = "Requester-Id")] int requesterIdHeader = 0)
        {
            var rid = requesterId > 0 ? requesterId : requesterIdHeader;
            if (rid <= 0) return BadRequest("Невалиден ID на заявителя.");

            try
            {
                var user = await _userService.GetUserById(rid);
                if (user == null) return NotFound("Потребителят не е намерен.");

                var results = await _resultService.GetResultsForUser(rid, user.RoleID, user.ClubID);
                return Ok(results);
            }
            catch
            {
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }

        // ✅ :int за по-точен route matching
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Result>> GetResult(
            [Required] int id,
            [FromQuery] int requesterId = 0,
            [FromHeader(Name = "Requester-Id")] int requesterIdHeader = 0)
        {
            var rid = requesterId > 0 ? requesterId : requesterIdHeader;
            if (id <= 0 || rid <= 0) return BadRequest("Невалиден ID на резултат или заявител.");

            try
            {
                var user = await _userService.GetUserById(rid);
                if (user == null) return NotFound("Потребителят не е намерен.");

                var result = await _resultService.GetResultById(id, rid, user.RoleID, user.ClubID);
                return Ok(result);
            }
            catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
            catch (UnauthorizedAccessException ex) { return ForbiddenMsg<Result>(ex.Message); }
            catch { return StatusCode(500, "Вътрешна грешка на сървъра."); }
        }

        [HttpPost]
        public async Task<ActionResult<Result>> PostResult(
            [FromBody] Result result,
            [FromHeader(Name = "Requester-Id")] int requesterId)
        {
            if (requesterId <= 0)
                return BadRequest("Невалиден Requester-Id.");

            if (result == null) return BadRequest("Липсва информация за резултата.");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");

                if (!IsCoachOrAdmin(user.RoleID))
                    return ForbiddenMsg<Result>("Само треньори и администратори могат да добавят резултати.");

                await _resultService.AddResult(result, requesterId, user.RoleID, user.ClubID);
                return CreatedAtAction(nameof(GetResult), new { id = result.Id, requesterId }, result);
            }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch (UnauthorizedAccessException ex) { return ForbiddenMsg<Result>(ex.Message); }
            catch { return StatusCode(500, "Вътрешна грешка на сървъра."); }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutResult(
            int id,
            [FromBody] Result result,
            [FromHeader(Name = "Requester-Id")] int requesterId)
        {
            if (requesterId <= 0)
                return BadRequest("Невалиден Requester-Id.");

            if (result == null) return BadRequest("Липсва информация за резултата.");
            if (id != result.Id) return BadRequest("Несъответствие между ID в URL и ID на резултата.");
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (result.UserId <= 0) return BadRequest("Невалиден ID на потребителя.");
            if (result.ValueTime <= 0 || result.ValueTime > 86400) return BadRequest("Резултатът трябва да е положителна стойност (0..86400).");
            if (result.DisciplineId <= 0) return BadRequest("Невалиден ID на дисциплината.");

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");

                if (!IsCoachOrAdmin(user.RoleID))
                    return ForbiddenMsg("Само треньори и администратори могат да редактират резултати.");

                await _resultService.UpdateResult(result, requesterId, user.RoleID, user.ClubID);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
            catch (UnauthorizedAccessException ex) { return ForbiddenMsg(ex.Message); }
            catch { return StatusCode(500, "Вътрешна грешка на сървъра."); }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteResult(
            int id,
            [FromHeader(Name = "Requester-Id")] int requesterId)
        {
            if (id <= 0 || requesterId <= 0)
                return BadRequest("Невалидни входни данни.");

            try
            {
                var requester = await _userService.GetUserById(requesterId);
                if (requester == null) return NotFound("Потребителят не е намерен.");

                if (!IsCoachOrAdmin(requester.RoleID))
                    return ForbiddenMsg("Само треньори и администратори могат да изтриват резултати.");

                await _resultService.DeleteResult(id, requesterId, requester.RoleID, requester.ClubID);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
            catch (UnauthorizedAccessException ex) { return ForbiddenMsg(ex.Message); }
            catch { return StatusCode(500, "Вътрешна грешка на сървъра."); }
        }

        [HttpGet("by-user/{userId:int}/by-discipline/{disciplineId:int}")]
        public async Task<ActionResult<IEnumerable<Result>>> GetResultsByUserAndDiscipline(
            int userId,
            int disciplineId,
            [FromQuery] int requesterId = 0,
            [FromHeader(Name = "Requester-Id")] int requesterIdHeader = 0)
        {
            var rid = requesterId > 0 ? requesterId : requesterIdHeader;

            if (userId <= 0 || disciplineId <= 0)
                return BadRequest("Невалиден userId или disciplineId.");

            if (rid <= 0)
                return BadRequest("Невалиден ID на заявителя.");

            try
            {
                var requester = await _userService.GetUserById(rid);
                if (requester == null)
                    return NotFound("Потребителят не е намерен.");

                var targetUser = await _userService.GetUserById(userId);
                if (targetUser == null)
                    return NotFound("Атлетът не е намерен.");

                // Достъп:
                // Admin (3) -> всички
                // Coach (2) -> само атлети от неговия клуб
                // Athlete (1) -> само себе си
                if (requester.RoleID == 2 && requester.ClubID != targetUser.ClubID)
                    return StatusCode(StatusCodes.Status403Forbidden, "Нямате достъп до този атлет.");

                if (requester.RoleID != 2 && requester.RoleID != 3 && requester.Id != userId)
                    return StatusCode(StatusCodes.Status403Forbidden, "Нямате достъп до този атлет.");

                var results = await _resultService.GetResultsByUserIdAndDisciplineId(userId, disciplineId);

                // по дата за диаграма (старо -> ново); фронтът пак си сортира
                return Ok(results.OrderBy(r => r.ResultDate));
            }
            catch
            {
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }

        // ✅ НОВО: всички резултати за клуб + дисциплина (за coach таблицата)
        [HttpGet("by-club/{clubId:int}/by-discipline/{disciplineId:int}")]
        public async Task<ActionResult<IEnumerable<Result>>> GetResultsByClubAndDiscipline(
            int clubId,
            int disciplineId,
            [FromQuery] int requesterId = 0,
            [FromHeader(Name = "Requester-Id")] int requesterIdHeader = 0)
        {
            var rid = requesterId > 0 ? requesterId : requesterIdHeader;

            if (clubId <= 0 || disciplineId <= 0)
                return BadRequest("Невалиден clubId или disciplineId.");

            if (rid <= 0)
                return BadRequest("Невалиден ID на заявителя.");

            try
            {
                var requester = await _userService.GetUserById(rid);
                if (requester == null)
                    return NotFound("Потребителят не е намерен.");

                // Coach -> само своя клуб, Admin -> всички
                if (requester.RoleID == 2 && requester.ClubID != clubId)
                    return StatusCode(StatusCodes.Status403Forbidden, "Нямате достъп до резултатите на друг клуб.");

                // Ако искаш да ограничиш атлет (role=1), разкоментирай:
                // if (requester.RoleID == 1)
                //    return StatusCode(StatusCodes.Status403Forbidden, "Нямате достъп до този ресурс.");

                var rows = await _resultService.GetResultsByClubAndDiscipline(
                    clubId,
                    disciplineId,
                    rid,
                    requester.RoleID,
                    requester.ClubID);

                // Удобно за UI; фронтът пак си групира/сортира
                return Ok(rows
                    .OrderBy(r => r.ValueTime)
                    .ThenByDescending(r => r.ResultDate));
            }
            catch (UnauthorizedAccessException ex)
            {
                return ForbiddenMsg<IEnumerable<Result>>(ex.Message);
            }
            catch
            {
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }

        // ✅ Старият "best" endpoint е преместен на /best
        [HttpGet("by-club/{clubId:int}/by-discipline/{disciplineId:int}/best")]
        public async Task<ActionResult<IEnumerable<ClubBestResultDto>>> GetBestResultsByClubAndDiscipline(
            int clubId,
            int disciplineId,
            [FromQuery] int requesterId = 0,
            [FromHeader(Name = "Requester-Id")] int requesterIdHeader = 0)
        {
            var rid = requesterId > 0 ? requesterId : requesterIdHeader;

            if (clubId <= 0 || disciplineId <= 0)
                return BadRequest("Невалиден clubId или disciplineId.");

            if (rid <= 0)
                return BadRequest("Невалиден ID на заявителя.");

            try
            {
                var requester = await _userService.GetUserById(rid);
                if (requester == null)
                    return NotFound("Потребителят не е намерен.");

                if (requester.RoleID == 2 && requester.ClubID != clubId)
                    return StatusCode(StatusCodes.Status403Forbidden, "Нямате достъп до резултатите на друг клуб.");

                var rows = await _resultService.GetClubBestResultsByDiscipline(
                    clubId,
                    disciplineId,
                    rid,
                    requester.RoleID,
                    requester.ClubID);

                return Ok(rows
                    .OrderBy(x => x.ValueTime)
                    .ThenBy(x => x.UserFirstName)
                    .ThenBy(x => x.UserLastName));
            }
            catch (UnauthorizedAccessException ex)
            {
                return ForbiddenMsg<IEnumerable<ClubBestResultDto>>(ex.Message);
            }
            catch
            {
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }

        [HttpPost("import-lenex")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportLenex(
            [FromForm] LenexUploadDto dto,
            [FromHeader(Name = "Requester-Id")] int requesterId)
        {
            var file = dto?.File;

            if (requesterId <= 0)
                return BadRequest("Невалиден Requester-Id.");

            if (file == null || file.Length == 0)
                return BadRequest("Липсва файл.");

            var ext = Path.GetExtension(file.FileName);
            var allowedExts = new[] { ".xml", ".lenex", ".lef" };

            var extAllowed =
                !string.IsNullOrWhiteSpace(ext) &&
                allowedExts.Contains(ext, StringComparer.OrdinalIgnoreCase);

            if (!extAllowed)
            {
                var head = await ReadFileHeadAsync(file, 4096);
                if (head.IndexOf("<LENEX", StringComparison.OrdinalIgnoreCase) < 0)
                    return BadRequest("Файлът трябва да е LENEX XML (.xml/.lenex/.lef) или да съдържа <LENEX>.");
            }

            try
            {
                var requester = await _userService.GetUserById(requesterId);
                if (requester == null) return NotFound("Потребителят не е намерен.");

                if (!IsCoachOrAdmin(requester.RoleID))
                    return ForbiddenMsg("Само треньор/админ може да импортира LENEX.");

                // Admin -> всички клубове
                if (requester.RoleID == 3)
                {
                    var repAll = await _lenexImportService.ImportForAllClubs(file);
                    return Ok(repAll);
                }

                // Coach -> само неговия клуб
                var rep = await _lenexImportService.ImportForClub(file, requester.ClubID);
                return Ok(rep);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        private static async Task<string> ReadFileHeadAsync(IFormFile file, int maxChars)
        {
            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);

            var buffer = new char[maxChars];
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);

            if (stream.CanSeek) stream.Position = 0;

            return read > 0 ? new string(buffer, 0, read) : string.Empty;
        }

        // ✅ :int
        [HttpGet("{id:int}/splits")]
        public async Task<ActionResult<IEnumerable<ResultSplit>>> GetResultSplits(
            int id,
            [FromQuery] int requesterId = 0,
            [FromHeader(Name = "Requester-Id")] int requesterIdHeader = 0)
        {
            var rid = requesterId > 0 ? requesterId : requesterIdHeader;
            if (id <= 0 || rid <= 0) return BadRequest("Невалидни входни данни.");

            try
            {
                var user = await _userService.GetUserById(rid);
                if (user == null) return NotFound("Потребителят не е намерен.");

                var splits = await _resultService.GetSplitsForResult(id, rid, user.RoleID, user.ClubID);
                return Ok(splits);
            }
            catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
            catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, ex.Message); }
            catch { return StatusCode(500, "Вътрешна грешка на сървъра."); }
        }
        [HttpGet("platform-club-leaderboard-by-discipline/{disciplineId:int}")]
        public async Task<ActionResult<IEnumerable<ClubBestResultDto>>> GetPlatformClubLeaderboardByDiscipline(
     int disciplineId,
     [FromQuery] int requesterId = 0,
     [FromHeader(Name = "Requester-Id")] int requesterIdHeader = 0)
        {
            var rid = requesterId > 0 ? requesterId : requesterIdHeader;

            if (disciplineId <= 0)
                return BadRequest("Невалиден disciplineId.");

            if (rid <= 0)
                return BadRequest("Невалиден ID на заявителя.");

            try
            {
                var requester = await _userService.GetUserById(rid);
                if (requester == null)
                    return NotFound("Потребителят не е намерен.");

                var rows = await _resultService.GetPlatformClubLeadersByDiscipline(
                    disciplineId,
                    rid,
                    requester.RoleID,
                    requester.ClubID);

                // Връщаме празен масив вместо 404 -> по-лесен frontend
                return Ok(rows ?? Enumerable.Empty<ClubBestResultDto>());
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch
            {
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }
    }
}