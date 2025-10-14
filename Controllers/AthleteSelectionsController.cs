// Controllers/AthleteSelectionsController.cs
using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;

namespace SpoerStats2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AthleteSelectionsController : ControllerBase
    {
        private readonly AthleteSelectionService _svc;
        private readonly ILogger<AthleteSelectionsController> _log;

        public AthleteSelectionsController(AthleteSelectionService svc, ILogger<AthleteSelectionsController> log)
        { _svc = svc; _log = log; }

        // --- НОВО: кой е записан за конкретен слот ---
        public record AttendeeDto(int UserId, string FirstName, string LastName);

        // GET: api/athleteselections/slot/5
        [HttpGet("slot/{slotId:int}")]
        public async Task<ActionResult<IEnumerable<AttendeeDto>>> GetBySlot(int slotId)
        {
            var users = await _svc.GetUsersBySlotAsync(slotId);
            // Очаква се сервисът да върне колекция с UserId, FirstName, LastName.
            // Ако нямаш този метод, добави го в AthleteSelectionService и/или репозитория.
            var dto = users.Select(u => new AttendeeDto(u.Id, u.FirstName ?? "", u.LastName ?? ""));
            return Ok(dto);
        }

        // GET: api/athleteselections/user/114
        [HttpGet("user/{userId:int}")]
        public async Task<ActionResult<IEnumerable<AthleteSlotSelection>>> GetForUser(int userId)
            => Ok(await _svc.GetByUserAsync(userId));

        // POST: api/athleteselections (body: { userId, slotId })
        [HttpPost]
        public async Task<ActionResult<int>> Add([FromBody] AthleteSlotSelection dto)
        {
            try
            {
                var id = await _svc.AddAsync(dto.UserId, dto.SlotId);
                return CreatedAtAction(nameof(GetForUser), new { userId = dto.UserId }, id);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // DELETE: api/athleteselections/bykey?userId=114&slotId=2
        [HttpDelete("bykey")]
        public async Task<IActionResult> DeleteByKey([FromQuery] int userId, [FromQuery] int slotId)
        {
            var ok = await _svc.RemoveByUserAndSlotAsync(userId, slotId);
            return ok ? NoContent() : NotFound();
        }

        // PUT: api/athleteselections/user/114/replace
        public class ReplaceRequest { public int[] SlotIds { get; set; } = Array.Empty<int>(); }

        [HttpPut("user/{userId:int}/replace")]
        public async Task<IActionResult> Replace(int userId, [FromBody] ReplaceRequest req)
        {
            try
            {
                await _svc.ReplaceUserSelectionsAsync(userId, req.SlotIds);
                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET: api/athleteselections/monthly-fee?clubId=1&userId=114
        [HttpGet("monthly-fee")]
        public async Task<ActionResult<VwCurrentMonthlyDue>> GetMonthlyFee([FromQuery] int clubId, [FromQuery] int userId)
        {
            var row = await _svc.GetCurrentMonthlyDueAsync(clubId, userId);
            if (row == null) return NotFound();
            return Ok(row);
        }

        // GET: api/athleteselections/without?clubId=1
        [HttpGet("without")]
        public async Task<ActionResult<IEnumerable<User>>> GetWithoutSelections([FromQuery] int clubId)
            => Ok(await _svc.GetAthletesWithoutSelectionsAsync(clubId));
    }
}
