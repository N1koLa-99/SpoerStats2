using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using System.Data.SqlClient;

namespace SpoerStats2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SeasonsController : ControllerBase
    {
        private readonly SeasonService _svc;
        private readonly ILogger<SeasonsController> _log;

        public SeasonsController(SeasonService svc, ILogger<SeasonsController> log)
        {
            _svc = svc;
            _log = log;
        }

        // GET: api/seasons/club/1
        [HttpGet("club/{clubId:int}")]
        public async Task<ActionResult<IEnumerable<ClubSeason>>> GetSeasonsByClub(int clubId)
            => Ok(await _svc.GetSeasonsByClubAsync(clubId));

        // GET: api/seasons/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ClubSeason>> GetById(int id)
        {
            var s = await _svc.GetSeasonByIdAsync(id);
            return s is null ? NotFound() : Ok(s);
        }

        // POST: api/seasons
        [HttpPost]
        public async Task<ActionResult<int>> Create([FromBody] ClubSeason s)
        {
            if (s.StartDate >= s.EndDate) return BadRequest("StartDate must be before EndDate.");
            // по подразбиране IsActive=true (можеш да го контролираш от UI)
            var id = await _svc.CreateSeasonAsync(s);
            return CreatedAtAction(nameof(GetById), new { id }, id);
        }

        // PUT: api/seasons/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ClubSeason s)
        {
            if (id != s.Id) return BadRequest("Id mismatch.");
            var ok = await _svc.UpdateSeasonAsync(s);
            return ok ? NoContent() : NotFound();
        }

        // PATCH: api/seasons/5/active?value=true
        [HttpPatch("{id:int}/active")]
        public async Task<IActionResult> SetActive(int id, [FromQuery] bool value = true)
        {
            var ok = await _svc.SetSeasonActiveAsync(id, value);
            return ok ? NoContent() : NotFound();
        }

        // GET: api/seasons/5/months
        [HttpGet("{clubSeasonId:int}/months")]
        public async Task<ActionResult<IEnumerable<SeasonBillingMonth>>> GetMonths(int clubSeasonId)
            => Ok(await _svc.GetSeasonMonthsAsync(clubSeasonId));

        // POST: api/seasons/5/months/generate?billable=true
        [HttpPost("{clubSeasonId:int}/months/generate")]
        public async Task<IActionResult> GenerateMonths(int clubSeasonId, [FromQuery] bool billable = true)
        {
            await _svc.GenerateSeasonMonthsAsync(clubSeasonId, billable);
            return NoContent();
        }

        // PATCH: api/seasons/months/123/billable?value=false  (по Id)
        [HttpPatch("months/{id:int}/billable")]
        public async Task<IActionResult> SetMonthBillableById(int id, [FromQuery] bool value)
        {
            var ok = await _svc.SetMonthBillableByIdAsync(id, value);
            return ok ? NoContent() : NotFound();
        }

        // PATCH: api/seasons/5/months/2025-12/billable?value=false  (по ключ)
        [HttpPatch("{clubSeasonId:int}/months/{yearMonth}/billable")]
        public async Task<IActionResult> SetMonthBillable(int clubSeasonId, string yearMonth, [FromQuery] bool value)
        {
            var ok = await _svc.SetMonthBillableAsync(clubSeasonId, yearMonth, value);
            return ok ? NoContent() : NotFound();
        }
        // DELETE: api/seasons/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var s = await _svc.GetSeasonByIdAsync(id);
            if (s is null) return NotFound();

            if (s.IsActive)
                return Conflict("Активен сезон не може да бъде изтрит. Деактивирай сезона и опитай отново.");

            try
            {
                var ok = await _svc.DeleteSeasonAsync(id);
                return ok ? NoContent() : NotFound();
            }
            catch (SqlException ex) when (ex.Number == 547) // FK violation
            {
                return Conflict("Сезонът има зависими записи (напр. ценови планове/месеци/други). Първо ги премахни или откачи.");
            }
        }

    }
}
