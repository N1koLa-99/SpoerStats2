// Controllers/TrainingGroupsController.cs
using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;

namespace SpoerStats2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrainingGroupsController : ControllerBase
    {
        private readonly TrainingGroupService _svc;
        private readonly ILogger<TrainingGroupsController> _log;

        public TrainingGroupsController(TrainingGroupService svc, ILogger<TrainingGroupsController> log)
        {
            _svc = svc; _log = log;
        }

        // GET: api/traininggroups/club/1
        [HttpGet("club/{clubId:int}")]
        public async Task<ActionResult<IEnumerable<TrainingGroup>>> GetByClub(int clubId)
            => Ok(await _svc.GetByClubAsync(clubId));

        // GET: api/traininggroups/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<TrainingGroup>> Get(int id)
        {
            var g = await _svc.GetByIdAsync(id);
            return g is null ? NotFound() : Ok(g);
        }

        // POST: api/traininggroups
        [HttpPost]
        public async Task<ActionResult<int>> Create([FromBody] TrainingGroup g)
        {
            if (string.IsNullOrWhiteSpace(g.Name)) return BadRequest("Name is required.");
            var id = await _svc.CreateAsync(g);
            return CreatedAtAction(nameof(Get), new { id }, id);
        }

        // PUT: api/traininggroups/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] TrainingGroup g)
        {
            if (id != g.Id) return BadRequest("Id mismatch.");
            var ok = await _svc.UpdateAsync(g);
            return ok ? NoContent() : NotFound();
        }

        // DELETE: api/traininggroups/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var ok = await _svc.DeleteAsync(id);
            return ok ? NoContent() : NotFound();
        }
    }
}
