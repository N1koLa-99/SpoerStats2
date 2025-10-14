// Controllers/PaymentsController.cs
using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;

namespace SpoerStats2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly PaymentService _svc;
        private readonly ILogger<PaymentsController> _log;

        public PaymentsController(PaymentService svc, ILogger<PaymentsController> log)
        {
            _svc = svc;
            _log = log;
        }

        // GET: api/payments/5
        [HttpGet("{id:int}")]
        public async Task<ActionResult<Payment>> Get(int id)
        {
            var p = await _svc.GetAsync(id);
            return p is null ? NotFound() : Ok(p);
        }

        // GET: api/payments/user/114
        [HttpGet("user/{userId:int}")]
        public async Task<ActionResult<IEnumerable<Payment>>> GetByUser(int userId)
            => Ok(await _svc.GetByUserAsync(userId));

        // GET: api/payments/bykey?clubId=1&userId=114&ym=2025-10
        [HttpGet("bykey")]
        public async Task<ActionResult<Payment>> GetByKey(
            [FromQuery] int clubId,
            [FromQuery] int userId,
            [FromQuery(Name = "ym")] string yearMonth)
        {
            var p = await _svc.GetByKeyAsync(clubId, userId, yearMonth);
            return p is null ? NotFound() : Ok(p);
        }

        // POST: api/payments/mark-paid  (body: { clubId, userId, yearMonth, recordedByUserId })
        public record MarkPaidRequest(int ClubId, int UserId, string YearMonth, int? RecordedByUserId);

        [HttpPost("mark-paid")]
        public async Task<ActionResult<int>> MarkPaid([FromBody] MarkPaidRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.YearMonth))
                return BadRequest("YearMonth is required (YYYY-MM).");

            var id = await _svc.MarkPaidAsync(req.ClubId, req.UserId, req.YearMonth, req.RecordedByUserId);
            return Ok(id); // връщаме Id на реда (нов или обновен)
        }

        // PUT: api/payments/5/amounts  (админска корекция)
        public record AmountsRequest(decimal AmountDue, decimal AmountPaid, DateTime? PaidOn, string? Method);

        [HttpPut("{id:int}/amounts")]
        public async Task<IActionResult> UpdateAmounts(int id, [FromBody] AmountsRequest req)
        {
            var ok = await _svc.UpdateAmountsAsync(id, req.AmountDue, req.AmountPaid, req.PaidOn, req.Method);
            return ok ? NoContent() : NotFound();
        }

        // GET: api/payments/month/club/1?ym=2025-10
        [HttpGet("month/club/{clubId:int}")]
        public async Task<ActionResult<IEnumerable<Payment>>> GetByMonth(
            int clubId,
            [FromQuery(Name = "ym")] string yearMonth)
            => Ok(await _svc.GetByMonthAsync(clubId, yearMonth));

        // GET: api/payments/unpaid/club/1?ym=2025-10
        [HttpGet("unpaid/club/{clubId:int}")]
        public async Task<ActionResult<IEnumerable<User>>> GetUnpaid(
            int clubId,
            [FromQuery(Name = "ym")] string yearMonth)
            => Ok(await _svc.GetUnpaidByMonthAsync(clubId, yearMonth));

        // GET: api/payments/paid/club/1?ym=2025-10
        [HttpGet("paid/club/{clubId:int}")]
        public async Task<ActionResult<IEnumerable<User>>> GetPaid(
            int clubId,
            [FromQuery(Name = "ym")] string yearMonth)
            => Ok(await _svc.GetPaidByMonthAsync(clubId, yearMonth));

        // POST: api/payments/mark-unpaid  (body: { clubId, userId, yearMonth })
        public record MarkUnpaidRequest(int ClubId, int UserId, string YearMonth);

        [HttpPost("mark-unpaid")]
        public async Task<IActionResult> MarkUnpaid([FromBody] MarkUnpaidRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.YearMonth))
                return BadRequest("YearMonth is required (YYYY-MM).");

            var ok = await _svc.MarkUnpaidAsync(req.ClubId, req.UserId, req.YearMonth);
            return ok ? NoContent() : NotFound(); // няма ред за този ключ
        }

        // GET: api/payments/expected/club/1
        [HttpGet("expected/club/{clubId:int}")]
        public async Task<ActionResult<IEnumerable<VwClubMonthlyExpectedRevenue>>> GetExpected(int clubId)
            => Ok(await _svc.GetExpectedRevenueAsync(clubId));

        // GET: api/payments/expected-v2/club/1
        [HttpGet("expected-v2/club/{clubId:int}")]
        public async Task<ActionResult<IEnumerable<VwClubMonthlyExpectedRevenue>>> GetExpectedV2(int clubId)
            => Ok(await _svc.GetExpectedRevenueV2Async(clubId));

        // GET: api/payments/expected-v2/club/1/details?ym=2025-10
        [HttpGet("expected-v2/club/{clubId:int}/details")]
        public async Task<ActionResult<IEnumerable<VwExpectedPerUser>>> GetExpectedDetailsV2(
            int clubId,
            [FromQuery(Name = "ym")] string yearMonth)
            => Ok(await _svc.GetExpectedPerUserV2Async(clubId, yearMonth));

        // GET: api/payments/expected/by-user?clubId=1&ym=2025-10&seasonId=2
        [HttpGet("expected/by-user")]
        public async Task<IActionResult> ExpectedByUser(
            [FromQuery] int clubId,
            [FromQuery] string ym,              // "YYYY-MM"
            [FromQuery] int? seasonId = null)   // опционално
        {
            var res = await _svc.GetExpectedByUserAsync(clubId, ym, seasonId);
            return Ok(res);
        }
    }
}
