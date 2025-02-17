using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using System.ComponentModel.DataAnnotations; // За валидации

namespace SpoerStats2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResultsController : ControllerBase
    {
        private readonly ResultService _resultService;
        private readonly UserService _userService;

        public ResultsController(ResultService resultService, UserService userService)
        {
            _resultService = resultService;
            _userService = userService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Result>>> GetResults([Required] int requesterId)
        {
            if (requesterId <= 0) return BadRequest("Невалиден ID на заявителя.");

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");

                int roleId = user.RoleID;
                int clubId = user.ClubID;

                var results = roleId == 2
                    ? await _resultService.GetResultsForClub(clubId)
                    : await _resultService.GetResultsForUser(requesterId, roleId, clubId);

                return Ok(results);
            }
            catch
            {
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Result>> GetResult([Required] int id, [Required] int requesterId)
        {
            if (id <= 0 || requesterId <= 0) return BadRequest("Невалиден ID на резултат или заявител.");

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");

                var result = await _resultService.GetResultById(id, requesterId, user.RoleID, user.ClubID);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch
            {
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpPost]
        public async Task<ActionResult<Result>> PostResult([FromBody] Result result, [Required] int requesterId)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (requesterId <= 0) return BadRequest("Невалиден ID на заявителя.");
            if (result == null) return BadRequest("Липсва информация за резултата.");
            if (result.UserId <= 0) return BadRequest("Невалиден ID на потребителя.");
            if (result.ValueTime <= 0) return BadRequest("Резултатът трябва да е положителна стойност.");
            if (result.DisciplineId <= 0) return BadRequest("Невалиден ID на дисциплината.");

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");
                if (user.RoleID != 2) return Forbid("Само треньори могат да добавят резултати.");

                await _resultService.AddResult(result, requesterId, user.RoleID, user.ClubID);
                return CreatedAtAction(nameof(GetResult), new { id = result.Id, requesterId }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch
            {
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutResult(int id, [FromBody] Result result, [Required] int requesterId)
        {
            if (id != result.Id) return BadRequest("Несъответствие между ID в URL и ID на резултата.");
            if (requesterId <= 0) return BadRequest("Невалиден ID на заявителя.");
            if (result == null) return BadRequest("Липсва информация за резултата.");
            if (result.UserId <= 0) return BadRequest("Невалиден ID на потребителя.");
            if (result.ValueTime <= 0) return BadRequest("Резултатът трябва да е положителна стойност.");
            if (result.DisciplineId <= 0) return BadRequest("Невалиден ID на дисциплината.");

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");

                await _resultService.UpdateResult(result, requesterId, user.RoleID, user.ClubID);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch
            {
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteResult(int id, [Required] int requesterId)
        {
            if (id <= 0 || requesterId <= 0) return BadRequest("Невалиден ID на резултат или заявител.");

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");

                await _resultService.DeleteResult(id, requesterId, user.RoleID, user.ClubID);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch
            {
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpGet("by-user/{userId}/by-discipline/{disciplineId}")]
        public async Task<ActionResult<IEnumerable<Result>>> GetResultsByUserIdAndDisciplineId(int userId, int disciplineId, [Required] int requesterId)
        {
            if (userId <= 0 || disciplineId <= 0 || requesterId <= 0)
                return BadRequest("Невалидни входни данни.");

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");

                var results = await _resultService.GetResultsByUserIdAndDisciplineId(userId, disciplineId);
                return Ok(results);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch
            {
                return StatusCode(500, "Internal server error.");
            }
        }
    }
}
