using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using SpoerStats2.Service;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpoerStats2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClubsController : ControllerBase
    {
        private readonly ClubService _clubService;
        private readonly UserService _userService;

        public ClubsController(ClubService clubService, UserService userService)
        {
            _clubService = clubService;
            _userService = userService;
        }

        private static bool IsAdmin(int roleId) => roleId == 3;

        // ✅ за IActionResult методи (PUT/DELETE)
        private IActionResult ForbiddenMsg(string message)
            => StatusCode(StatusCodes.Status403Forbidden, message);

        // ✅ за ActionResult<T> методи (POST/GET ако трябва)
        private ActionResult<T> ForbiddenMsg<T>(string message)
            => StatusCode(StatusCodes.Status403Forbidden, message);

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Club>>> GetClubs()
        {
            return Ok(await _clubService.GetAllClubs());
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Club>> GetClub(int id)
        {
            var club = await _clubService.GetClubById(id);

            if (club == null)
                return NotFound();

            return Ok(club);
        }

        [HttpPost]
        public async Task<ActionResult<Club>> PostClub(
            [FromBody] Club club,
            [FromHeader(Name = "Requester-Id")] int requesterId)
        {
            if (requesterId <= 0)
                return BadRequest("Невалиден Requester-Id.");

            if (club == null)
                return BadRequest("Липсват данни за клуба.");

            try
            {
                var requester = await _userService.GetUserById(requesterId);
                if (requester == null)
                    return NotFound("Потребителят не е намерен.");

                if (!IsAdmin(requester.RoleID))
                    return ForbiddenMsg<Club>("Само админ може да добавя клубове.");

                await _clubService.AddClub(club, requester.RoleID);
                return CreatedAtAction(nameof(GetClub), new { id = club.Id }, club);
            }
            catch (UnauthorizedAccessException ex) { return ForbiddenMsg<Club>(ex.Message); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch { return StatusCode(500, "Вътрешна грешка на сървъра."); }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> PutClub(
            int id,
            [FromBody] Club club,
            [FromHeader(Name = "Requester-Id")] int requesterId)
        {
            if (requesterId <= 0)
                return BadRequest("Невалиден Requester-Id.");

            if (club == null)
                return BadRequest("Липсват данни за клуба.");

            if (id != club.Id)
                return BadRequest("Несъответствие между ID в URL и ID на клуба.");

            try
            {
                var requester = await _userService.GetUserById(requesterId);
                if (requester == null)
                    return NotFound("Потребителят не е намерен.");

                if (!IsAdmin(requester.RoleID))
                    return ForbiddenMsg("Само админ може да редактира клубове.");

                await _clubService.UpdateClub(club, requester.RoleID);
                return NoContent();
            }
            catch (UnauthorizedAccessException ex) { return ForbiddenMsg(ex.Message); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch { return StatusCode(500, "Вътрешна грешка на сървъра."); }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteClub(
            int id,
            [FromHeader(Name = "Requester-Id")] int requesterId)
        {
            if (requesterId <= 0)
                return BadRequest("Невалиден Requester-Id.");

            try
            {
                var requester = await _userService.GetUserById(requesterId);
                if (requester == null)
                    return NotFound("Потребителят не е намерен.");

                if (!IsAdmin(requester.RoleID))
                    return ForbiddenMsg("Само админ може да трие клубове.");

                var club = await _clubService.GetClubById(id);
                if (club == null)
                    return NotFound();

                await _clubService.DeleteClub(id, requester.RoleID);
                return NoContent();
            }
            catch (UnauthorizedAccessException ex) { return ForbiddenMsg(ex.Message); }
            catch (ArgumentException ex) { return BadRequest(ex.Message); }
            catch { return StatusCode(500, "Вътрешна грешка на сървъра."); }
        }

    }
}