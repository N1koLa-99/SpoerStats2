using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;

namespace SpoerStats2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeePlansController : ControllerBase
    {
        private readonly FeePlanService _svc;
        private readonly ILogger<FeePlansController> _log;

        public FeePlansController(FeePlanService svc, ILogger<FeePlansController> log)
        {
            _svc = svc;
            _log = log;
        }

        // GET: api/feeplans/club/1?seasonId=5
        [HttpGet("club/{clubId:int}")]
        public async Task<ActionResult<IEnumerable<ClubFeePlan>>> GetByClub(int clubId, [FromQuery] int? seasonId = null)
            => Ok(await _svc.GetByClubAsync(clubId, seasonId));

        // GET: api/feeplans/active?clubId=1&tpw=2&seasonId=5&on=2025-10-15
        [HttpGet("active")]
        public async Task<ActionResult<ClubFeePlan?>> GetActive([FromQuery] int clubId, [FromQuery] int tpw,
                                                                [FromQuery] int? seasonId = null, [FromQuery] DateTime? on = null)
        {
            if (tpw < 1 || tpw > 7) return BadRequest("tpw must be 1..7.");
            var item = await _svc.GetActiveAsync(clubId, tpw, seasonId, on);
            return item is null ? NotFound() : Ok(item);
        }

        // GET: api/feeplans/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ClubFeePlan>> GetById(int id)
        {
            var p = await _svc.GetByIdAsync(id);
            return p is null ? NotFound() : Ok(p);
        }

        // POST: api/feeplans
        [HttpPost]
        public async Task<ActionResult<int>> Create([FromBody] ClubFeePlan p)
        {
            if (p.TrainingsPerWeek < 1 || p.TrainingsPerWeek > 7) return BadRequest("TrainingsPerWeek must be 1..7.");
            if (p.ValidTo.HasValue && p.ValidTo < p.ValidFrom) return BadRequest("ValidTo must be >= ValidFrom.");
            var id = await _svc.CreateAsync(p);
            return CreatedAtAction(nameof(GetById), new { id }, id);
        }

        // PUT: api/feeplans/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ClubFeePlan p)
        {
            if (id != p.Id) return BadRequest("Id mismatch.");
            if (p.TrainingsPerWeek < 1 || p.TrainingsPerWeek > 7) return BadRequest("TrainingsPerWeek must be 1..7.");
            if (p.ValidTo.HasValue && p.ValidTo < p.ValidFrom) return BadRequest("ValidTo must be >= ValidFrom.");
            var ok = await _svc.UpdateAsync(p);
            return ok ? NoContent() : NotFound();
        }

        // PATCH: api/feeplans/5/archive?validTo=2026-06-30
        [HttpPatch("{id:int}/archive")]
        public async Task<IActionResult> Archive(int id, [FromQuery] DateTime validTo)
        {
            var ok = await _svc.ArchiveAsync(id, validTo);
            return ok ? NoContent() : NotFound();
        }
    }
}
