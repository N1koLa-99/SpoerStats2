using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using System.ComponentModel.DataAnnotations;

namespace SpoerStats2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResultsController : ControllerBase
    {
        private readonly ResultService _resultService;
        private readonly UserService _userService;
        private readonly ClubService _clubService;

        public ResultsController(ResultService resultService, UserService userService, ClubService clubService)
        {
            _resultService = resultService;
            _userService = userService;
            _clubService = clubService;
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
        public async Task<ActionResult<Result>> PostResult([FromBody] Result result)
        {
            var requesterIdStr = HttpContext.Request.Headers["Requester-Id"].ToString();
            var roleIdStr = HttpContext.Request.Headers["Role-Id"].ToString();
            var clubIdStr = HttpContext.Request.Headers["Club-Id"].ToString();

            if (string.IsNullOrEmpty(requesterIdStr) ||
                string.IsNullOrEmpty(roleIdStr) ||
                string.IsNullOrEmpty(clubIdStr) ||
                !int.TryParse(requesterIdStr, out int requesterId) ||
                !int.TryParse(roleIdStr, out int roleId) ||
                !int.TryParse(clubIdStr, out int clubId))
            {
                return BadRequest("Невалидни данни в хедърите.");
            }

            if (result == null) return BadRequest("Липсва информация за резултата.");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");
                if (roleId != 2) return Forbid("Само треньори могат да добавят резултати.");

                await _resultService.AddResult(result, requesterId, roleId, clubId);
                return CreatedAtAction(nameof(GetResult), new { id = result.Id }, result);
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
        public async Task<IActionResult> PutResult(int id, [FromBody] Result result)
        {
            var requesterIdStr = HttpContext.Request.Headers["Requester-Id"].ToString();
            var roleIdStr = HttpContext.Request.Headers["Role-Id"].ToString();
            var clubIdStr = HttpContext.Request.Headers["Club-Id"].ToString();

            if (string.IsNullOrEmpty(requesterIdStr) ||
                string.IsNullOrEmpty(roleIdStr) ||
                string.IsNullOrEmpty(clubIdStr) ||
                !int.TryParse(requesterIdStr, out int requesterId) ||
                !int.TryParse(roleIdStr, out int roleId) ||
                !int.TryParse(clubIdStr, out int clubId))
            {
                return BadRequest("Невалидни данни в хедърите.");
            }

            if (id != result.Id) return BadRequest("Несъответствие между ID в URL и ID на резултата.");
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (result == null) return BadRequest("Липсва информация за резултата.");
            if (result.UserId <= 0) return BadRequest("Невалиден ID на потребителя.");
            if (result.ValueTime <= 0) return BadRequest("Резултатът трябва да е положителна стойност.");
            if (result.DisciplineId <= 0) return BadRequest("Невалиден ID на дисциплината.");

            try
            {
                var user = await _userService.GetUserById(requesterId);
                if (user == null) return NotFound("Потребителят не е намерен.");
                if (roleId != 2) return Forbid("Само треньори могат да редактират резултати.");

                await _resultService.UpdateResult(result, requesterId, roleId, clubId);
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
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }


        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteResult(int id)
        {
            var requesterIdStr = HttpContext.Request.Headers["Requester-Id"].ToString();
            var roleIdStr = HttpContext.Request.Headers["Role-Id"].ToString();
            var clubIdStr = HttpContext.Request.Headers["Club-Id"].ToString();

            if (string.IsNullOrEmpty(requesterIdStr) ||
                string.IsNullOrEmpty(roleIdStr) ||
                string.IsNullOrEmpty(clubIdStr) ||
                !int.TryParse(requesterIdStr, out int requesterId) ||
                !int.TryParse(roleIdStr, out int roleId) ||
                !int.TryParse(clubIdStr, out int clubId) ||
                id <= 0 || requesterId <= 0 || roleId <= 0 || clubId <= 0)
            {
                return BadRequest("Невалидни входни данни.");
            }

            try
            {
                var requester = await _userService.GetUserById(requesterId);
                if (requester == null)
                    return NotFound("Потребителят не е намерен.");

                var result = await _resultService.GetResultById(id, requesterId, roleId, clubId);
                if (result == null)
                    return NotFound("Резултатът не е намерен.");

                if (roleId != 2) // Само треньори (RoleID = 2) могат да трият резултати
                {
                    return Forbid("Само треньори могат да изтриват резултати.");
                }

                await _resultService.DeleteResult(id, requesterId, roleId, clubId);
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
        public async Task<ActionResult<IEnumerable<Result>>> GetResultsByUserIdAndDisciplineId(int userId, int disciplineId)
        {
            if (!HttpContext.Request.Headers.TryGetValue("Requester-Id", out var requesterIdStr) ||
                !int.TryParse(requesterIdStr, out int requesterId))
            {
                return BadRequest("Не :/ ");
            }

            if (userId <= 0 || disciplineId <= 0 || requesterId <= 0)
            {
                return BadRequest("Невалидни входни данни.");
            }

            try
            {
                var requester = await _userService.GetUserById(requesterId);
                if (requester == null)
                {
                    return NotFound("Потребителят не е намерен.");
                }

                var targetUser = await _userService.GetUserById(userId);
                if (targetUser == null)
                {
                    return NotFound("Търсеният потребител не е намерен.");
                }

                if (requester.ClubID != targetUser.ClubID)
                {
                    return Forbid("Нямате достъп до този потребител.");
                }

                if (requester.RoleID != 2 && requesterId != userId)
                {
                    return Forbid("Нямате права да виждате тези резултати.");
                }

                var results = await _resultService.GetResultsByUserIdAndDisciplineId(userId, disciplineId);
                if (results == null || !results.Any())
                {
                    return NotFound("Няма намерени резултати за този потребител и дисциплина.");
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }

        [HttpGet("by-club/{clubId}/by-discipline/{disciplineId}")]
        public async Task<ActionResult<IEnumerable<UserBestResultDto>>> GetBestResultsByDisciplineInClub(int clubId, int disciplineId)
        {
            if (disciplineId <= 0 || clubId <= 0)
            {
                return BadRequest("Невалидни входни данни.");
            }

            try
            {
                var clubUsers = await _userService.GetUsersByClubId(clubId);
                if (clubUsers == null || !clubUsers.Any())
                {
                    return NotFound("Няма потребители за този клуб.");
                }

                var allResults = new List<UserBestResultDto>();

                foreach (var user in clubUsers)
                {
                    var userResults = await _resultService.GetResultsByUserIdAndDisciplineId(user.Id, disciplineId);
                    var bestResult = userResults?
                        .OrderBy(r => r.ValueTime)
                        .FirstOrDefault();

                    if (bestResult != null)
                    {
                        var dto = new UserBestResultDto
                        {
                            Id = bestResult.Id,
                            UserId = user.Id,
                            UserFirstName = user.FirstName,
                            UserLastName = user.LastName,
                            UserYearOfBirth = user.YearOfBirth,
                            DisciplineId = bestResult.DisciplineId,
                            ValueTime = bestResult.ValueTime,
                            ResultDate = bestResult.ResultDate,
                            SwimmingPoolStandart = bestResult.SwimmingPoolStandart,
                            Location = bestResult.Location
                        };

                        allResults.Add(dto);
                    }
                }

                var sortedResults = allResults.OrderBy(r => r.ValueTime).ToList();
                return Ok(sortedResults);
            }
            catch (Exception)
            {
                return StatusCode(500, "Вътрешна грешка на сървъра.");
            }
        }
        [HttpGet("best-club-by-discipline/{disciplineId}/year/{yearOfBirth}")]
        public async Task<ActionResult<ClubBestResultByGroupDto>> GetBestClubByDisciplineAndAge(int disciplineId, int yearOfBirth)
        {
            if (disciplineId <= 0 || yearOfBirth <= 1900)
                return BadRequest("Невалидни данни.");

            try
            {
                var allUsers = await _userService.GetAllUsers();
                if (allUsers == null || !allUsers.Any())
                    return NotFound("Няма потребители в системата.");

                var ageGroups = new Dictionary<string, Func<int, bool>>
        {
            { "C", y => y >= 2011 && y <= 2025 },
            { "B", y => y >= 2008 && y <= 2010 },
            { "A", y => y >= 1925 && y <= 2007 }
        };

                var group = ageGroups.FirstOrDefault(g => g.Value(yearOfBirth));
                if (group.Key == null)
                    return NotFound("Няма възрастова група за тази година.");

                User bestUser = null;
                Result bestResult = null;

                foreach (var user in allUsers.Where(u => group.Value(u.YearOfBirth)))
                {
                    var results = await _resultService.GetResultsByUserIdAndDisciplineId(user.Id, disciplineId);
                    var userBest = results?.OrderBy(r => r.ValueTime).FirstOrDefault();

                    if (userBest != null && (bestResult == null || userBest.ValueTime < bestResult.ValueTime))
                    {
                        bestUser = user;
                        bestResult = userBest;
                    }
                }

                if (bestUser != null && bestResult != null)
                {
                    var club = await _clubService.GetClubById(bestUser.ClubID);

                    var dto = new ClubBestResultDto
                    {
                        ClubId = club.Id,
                        ClubName = club.Name,
                        UserId = bestUser.Id,
                        UserFirstName = bestUser.FirstName,
                        UserLastName = bestUser.LastName,
                        YearOfBirth = bestUser.YearOfBirth,
                        ValueTime = bestResult.ValueTime,
                        ResultDate = bestResult.ResultDate,
                        Location = bestResult.Location
                    };

                    return Ok(new ClubBestResultByGroupDto
                    {
                        AgeGroup = group.Key,
                        BestResult = dto
                    });
                }

                return NotFound("Няма резултати за тази възрастова група.");
            }
            catch (Exception)
            {
                return StatusCode(500, "Вътрешна грешка.");
            }
        }


    }
}