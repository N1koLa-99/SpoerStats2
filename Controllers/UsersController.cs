using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using SpoerStats2.Service;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpoerStats2.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(UserService userService, ILogger<UsersController> logger)
        {
            _userService = userService;
            _logger = logger;
        }

        private static bool IsCoachOrAdmin(int roleId) => roleId == 2 || roleId == 3;

        // ✅ минимална защита: не връщаме чувствителни неща
        private static User Sanitize(User u)
        {
            if (u == null) return u;
            u.Password = null;
            u.AccessCode = null;
            // по желание:
            // u.SwimrankingsMeetCache = null;
            return u;
        }

        // =========================
        // DTOs (granular updates)
        // =========================
        public sealed class UpdateFirstNameDto { public string FirstName { get; set; } = ""; }
        public sealed class UpdateLastNameDto { public string LastName { get; set; } = ""; }
        public sealed class UpdateGenderDto { public string Gender { get; set; } = ""; }
        public sealed class UpdateYearDto { public int YearOfBirth { get; set; } }

        public sealed class ChangePasswordDto
        {
            public string OldPassword { get; set; } = "";
            public string NewPassword { get; set; } = "";
        }

        public sealed class ChangeEmailDto
        {
            public string NewEmail { get; set; } = "";
            public string Password { get; set; } = "";
        }

        // =========================
        // BASIC GETs
        // =========================
        [HttpGet("all")]
        public async Task<ActionResult<IEnumerable<User>>> GetAllUsers()
        {
            var users = await _userService.GetAllUsers();
            return Ok(users.Select(Sanitize).ToList());
        }

        [HttpGet("approved")]
        public async Task<ActionResult<IEnumerable<User>>> GetApprovedUsers()
        {
            var users = await _userService.GetAllUsers();
            var acceptedUsers = users.Where(u => u.StatusID == 2).Select(Sanitize).ToList();
            return Ok(acceptedUsers);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<User>> GetUser(int id)
        {
            var user = await _userService.GetUserById(id);
            if (user == null) return NotFound();
            return Ok(Sanitize(user));
        }

        [HttpGet("me")]
        public async Task<ActionResult<User>> GetMe()
        {
            try
            {
                var userId = GetUserIdFromSession();
                var user = await _userService.GetUserById(userId);
                if (user == null) return NotFound();
                return Ok(Sanitize(user));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        // =========================
        // REGISTER
        // =========================
        [HttpPost]
        public async Task<ActionResult<User>> PostUser(User user)
        {
            await _userService.AddUser(user);

            if (user.Id <= 0)
                return BadRequest("Неуспешно създаване на потребител. ID е невалидно.");

            var userSession = new UserSession(user);

            var response = new
            {
                user = Sanitize(user),
                userTokenHash = userSession.UserTokenHash
            };

            return CreatedAtAction("GetUser", new { id = user.Id }, response);
        }

        // =========================
        // DELETE
        // =========================
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _userService.GetUserById(id);
            if (user == null) return NotFound();
            await _userService.DeleteUser(id);
            return NoContent();
        }

        // =========================
        // LOGIN
        // =========================
        [HttpPost("login")]
        public async Task<ActionResult> Login([FromBody] UserLogin loginData)
        {
            var user = await _userService.GetUserByEmailAndPassword(loginData.Email, loginData.Password);
            if (user == null) return Unauthorized();

            HttpContext.Session.SetInt32("uid", user.Id);

            var athleteDisplay = BuildAthleteDisplayName(user);
            HttpContext.Session.SetString("athleteDisplay", athleteDisplay);

            var result = new UserSession(user); // ✅ hash е сетнат в ctor
            return Ok(new { session = result, athleteDisplay });
        }

        public sealed class SwridLoginDto
        {
            public string Swrid { get; set; } = "";
        }

        [HttpPost("login-swrid")]
        public async Task<ActionResult> LoginBySwrid([FromBody] SwridLoginDto dto)
        {
            var user = await _userService.GetUserBySwridLogin(dto?.Swrid ?? "");
            if (user == null) return Unauthorized("Invalid SWRID.");

            HttpContext.Session.SetInt32("uid", user.Id);

            var athleteDisplay = BuildAthleteDisplayName(user);
            HttpContext.Session.SetString("athleteDisplay", athleteDisplay);

            var result = new UserSession(user); // ✅ hash е сетнат в ctor
            return Ok(new { session = result, athleteDisplay });
        }

        // =========================
        // GRANULAR UPDATES (ME)
        // =========================
        [HttpPost("me/first-name")]
        public async Task<IActionResult> UpdateFirstName([FromBody] UpdateFirstNameDto dto)
        {
            var uid = GetUserIdFromSession();
            await _userService.UpdateFirstNameForMe(uid, dto?.FirstName ?? "");
            return Ok(new { message = "FirstName updated." });
        }

        [HttpPost("me/last-name")]
        public async Task<IActionResult> UpdateLastName([FromBody] UpdateLastNameDto dto)
        {
            var uid = GetUserIdFromSession();
            await _userService.UpdateLastNameForMe(uid, dto?.LastName ?? "");
            return Ok(new { message = "LastName updated." });
        }

        [HttpPost("me/gender")]
        public async Task<IActionResult> UpdateGender([FromBody] UpdateGenderDto dto)
        {
            var uid = GetUserIdFromSession();
            await _userService.UpdateGenderForMe(uid, dto?.Gender ?? "");
            return Ok(new { message = "Gender updated." });
        }

        [HttpPost("me/year-of-birth")]
        public async Task<IActionResult> UpdateYearOfBirth([FromBody] UpdateYearDto dto)
        {
            var uid = GetUserIdFromSession();
            await _userService.UpdateYearOfBirthForMe(uid, dto?.YearOfBirth ?? 0);
            return Ok(new { message = "YearOfBirth updated." });
        }

        [HttpPost("me/change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var uid = GetUserIdFromSession();
            await _userService.ChangePasswordForMe(uid, dto?.OldPassword ?? "", dto?.NewPassword ?? "");
            return Ok(new { message = "Password updated." });
        }

        [HttpPost("me/change-email")]
        public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailDto dto)
        {
            var uid = GetUserIdFromSession();
            await _userService.ChangeEmailForMe(uid, dto?.NewEmail ?? "", dto?.Password ?? "");
            return Ok(new { message = "Email updated." });
        }

        // =========================
        // CLUB
        // =========================
        [HttpPost("{userId}/requestJoin/{clubId}")]
        public async Task<IActionResult> RequestJoinClub(int userId, int clubId)
        {
            try
            {
                var sessionId = GetUserIdFromSession();
                if (userId != sessionId) return Forbid("Може да пращаш заявка само от логнатия профил.");

                var before = await _userService.GetUserById(userId);
                if (before == null) return NotFound("User not found.");

                await _userService.UpdateUserClubStatus(userId, clubId);

                if (before.StatusID == 2 && before.ClubID != clubId)
                    return Ok(new { message = "Заявката за трансфер е изпратена успешно. Изчаква одобрение от новия клуб." });

                return Ok(new { message = "Заявката за присъединяване е изпратена успешно." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("club/{clubId}")]
        public async Task<ActionResult<IEnumerable<User>>> GetUsersByClubId(int clubId)
        {
            var users = await _userService.GetUsersByClubId(clubId);
            var acceptedUsers = users.Where(u => u.StatusID == 2).Select(Sanitize).ToList();

            if (!acceptedUsers.Any())
                return NotFound("Няма одобрени потребители в този клуб.");

            return Ok(acceptedUsers);
        }

        [HttpGet("pending/{clubId}")]
        public async Task<IActionResult> GetPendingUsers(int clubId)
        {
            var users = await _userService.GetPendingUsers(clubId);
            return Ok(users.Select(Sanitize).ToList());
        }

        [HttpPost("approve/{userId}")]
        public async Task<IActionResult> ApproveUser(int userId)
        {
            var ok = await _userService.ApproveUser(userId);
            return ok
                ? Ok(new { message = "User approved successfully." })
                : BadRequest(new { message = "Failed to approve user." });
        }

        [HttpPost("reject/{userId}")]
        public async Task<IActionResult> RejectUser(int userId)
        {
            var ok = await _userService.RejectUser(userId);
            return ok
                ? Ok(new { message = "User rejected successfully." })
                : BadRequest(new { message = "Failed to reject user." });
        }

        // =========================
        // PROFILE PICTURE
        // =========================
        [HttpPost("uploadProfilePicture/{userId}")]
        public async Task<IActionResult> UploadProfilePicture(int userId, IFormFile file)
        {
            // ✅ само за себе си (ако искаш coach/admin да могат да качват за атлет, махаш тази проверка)
            var sessionId = GetUserIdFromSession();
            if (userId != sessionId) return Forbid("Може да сменяш снимка само на логнатия профил.");

            try
            {
                var profileImageUrl = await _userService.UpdateUserProfilePicture(userId, file);
                return Ok(new { profileImage_url = profileImageUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile picture for user {userId}", userId);
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("profilePicture/{userId}")]
        public async Task<IActionResult> GetUserProfilePicture(int userId)
        {
            try
            {
                var result = await _userService.GetUserProfilePicture(userId);
                return result;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Profile picture not found for user {userId}", userId);
                return NotFound(new { message = ex.Message });
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "File not found for user {userId}", userId);
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving profile picture for user {userId}", userId);
                return StatusCode(500, new { message = "An error occurred while retrieving the profile picture." });
            }
        }

        // =========================
        // SEARCH / CHECK EMAIL
        // =========================
        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<User>>> SearchUsers([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Search query cannot be empty.");

            var users = await _userService.SearchUsersByName(query);
            if (users == null || !users.Any())
                return NotFound("No users found matching the query.");

            return Ok(users.Select(Sanitize).ToList());
        }

        [HttpGet("check-email")]
        public async Task<IActionResult> CheckEmailExists([FromQuery] string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return BadRequest("Email is required.");

            bool exists = await _userService.DoesEmailExist(email);
            return Ok(new { exists });
        }

        // =========================
        // COACH CREATE ATHLETE
        // =========================
        [HttpPost("coach/create-athlete")]
        public async Task<IActionResult> CoachCreateAthlete([FromBody] CoachCreateAthleteDto dto)
        {
            var coachId = GetUserIdFromSession();

            var result = await _userService.CreateAthleteByCoach(
                coachId,
                dto.FirstName,
                dto.LastName,
                dto.YearOfBirth,
                dto.Gender
            );

            result.Athlete = Sanitize(result.Athlete);
            return Ok(result);
        }

        // =========================
        // LOGIN WITH CODE
        // =========================
        [HttpPost("login-code")]
        public async Task<ActionResult> LoginByAccessCode([FromBody] AccessCodeLoginDto dto)
        {
            var code = (dto?.Code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code)) return BadRequest("Code is required.");

            var user = await _userService.GetUserByAccessCodeLogin(code);
            if (user == null) return Unauthorized("Invalid code.");

            HttpContext.Session.SetInt32("uid", user.Id);

            var athleteDisplay = BuildAthleteDisplayName(user);
            HttpContext.Session.SetString("athleteDisplay", athleteDisplay);

            var session = new UserSession(user);
            return Ok(new { session, athleteDisplay, mustSetCredentials = user.MustSetCredentials });
        }

        // =========================
        // SETUP CREDENTIALS
        // =========================
        [HttpPost("setup-credentials")]
        public async Task<IActionResult> SetupCredentials([FromBody] SetupCredentialsDto dto)
        {
            var uid = GetUserIdFromSession();
            await _userService.SetupCredentialsForMe(uid, dto.Email, dto.Password);
            return Ok(new { message = "Credentials set successfully." });
        }

        // =========================
        // helpers
        // =========================
        private int GetUserIdFromSession()
        {
            var uid = HttpContext.Session.GetInt32("uid");
            if (uid == null)
                throw new UnauthorizedAccessException("Няма активна сесия. Първо login.");

            return uid.Value;
        }

        private static string BuildAthleteDisplayName(User u)
        {
            var year = u.YearOfBirth > 0 ? u.YearOfBirth.ToString() : null;

            var ln = (u.LastName ?? "").Trim().ToUpperInvariant();
            var fn = (u.FirstName ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(year))
                return $"{ln}, {fn} - {year}";

            return $"{ln}, {fn}";
        }
        [HttpGet("coach/access-codes")]
        public async Task<IActionResult> GetCoachAccessCodes()
        {
            try
            {
                var coachId = GetUserIdFromSession();
                var rows = await _userService.GetCoachAthleteAccessCodes(coachId);
                return Ok(rows);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading coach access codes");
                return StatusCode(500, new { message = "Грешка при зареждане на кодовете за достъп." });
            }
        }
    }
}