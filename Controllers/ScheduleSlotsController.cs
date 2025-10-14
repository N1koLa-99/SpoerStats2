// Controllers/ScheduleSlotsController.cs
using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;

namespace SpoerStats2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScheduleSlotsController : ControllerBase
    {
        private readonly ScheduleSlotService _svc;
        private readonly ILogger<ScheduleSlotsController> _log;

        public ScheduleSlotsController(ScheduleSlotService svc, ILogger<ScheduleSlotsController> log)
        { _svc = svc; _log = log; }

        // GET: api/scheduleslots/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ScheduleSlot>> Get(int id)
        {
            var s = await _svc.GetByIdAsync(id);
            return s is null ? NotFound() : Ok(s);
        }

        // GET: api/scheduleslots/group/10
        [HttpGet("group/{groupId:int}")]
        public async Task<ActionResult<IEnumerable<ScheduleSlot>>> GetByGroup(int groupId)
            => Ok(await _svc.GetByGroupAsync(groupId));

        // GET: api/scheduleslots/club/1?seasonId=5
        [HttpGet("club/{clubId:int}")]
        public async Task<ActionResult<IEnumerable<ScheduleSlot>>> GetByClubSeason(int clubId, [FromQuery] int? seasonId = null)
            => Ok(await _svc.GetByClubSeasonAsync(clubId, seasonId));

        // POST: api/scheduleslots
        [HttpPost]
        public async Task<ActionResult<int>> Create([FromBody] ScheduleSlot s)
        {
            try
            {
                var id = await _svc.CreateAsync(s);
                return CreatedAtAction(nameof(Get), new { id }, id);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // PUT: api/scheduleslots/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ScheduleSlot s)
        {
            if (id != s.Id) return BadRequest("Id mismatch.");
            try
            {
                var ok = await _svc.UpdateAsync(s);
                return ok ? NoContent() : NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // DELETE: api/scheduleslots/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var ok = await _svc.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }
    }
}
