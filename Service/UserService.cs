using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using SpoerStats2.Repository;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SpoerStats2.Service
{
    public class UserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IResultRepository _resultRepository;
        private readonly PasswordHasher<User> _passwordHasher;
        private readonly SwimrankingsService _swimrankingsService;

        public UserService(
            IUserRepository userRepository,
            IResultRepository resultRepository,
            SwimrankingsService swimrankingsService)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _resultRepository = resultRepository ?? throw new ArgumentNullException(nameof(resultRepository));
            _swimrankingsService = swimrankingsService ?? throw new ArgumentNullException(nameof(swimrankingsService));
            _passwordHasher = new PasswordHasher<User>();
        }

        private static bool IsCoachOrAdmin(int roleId) => roleId == 2 || roleId == 3;

        private static string NormalizeEmail(string? email)
            => (email ?? "").Trim().ToLowerInvariant();

        // =========================
        // BASIC
        // =========================
        public async Task<User?> GetUserByEmailAndPassword(string email, string password)
        {
            email = NormalizeEmail(email);
            password = (password ?? "").Trim();

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return null;

            var user = await _userRepository.GetUserByEmail(email);
            if (user == null) return null;

            // ✅ важно: при coach-created athlete Password е NULL
            if (string.IsNullOrWhiteSpace(user.Password)) return null;

            var vr = _passwordHasher.VerifyHashedPassword(user, user.Password, password);
            if (vr != PasswordVerificationResult.Success) return null;

            try
            {
                await SyncSwimrankingsOnLogin(user.Id);
                user = await _userRepository.GetUserById(user.Id);
            }
            catch { }

            return user;
        }

        // ✅ LOGIN по SWRID (без парола)
        public async Task<User?> GetUserBySwridLogin(string swrid)
        {
            swrid = (swrid ?? "").Trim();
            if (string.IsNullOrWhiteSpace(swrid)) return null;

            var user = await _userRepository.GetUserBySwrid(swrid);
            if (user == null) return null;

            // само одобрени атлети
            if (user.RoleID != 1) return null;
            if (user.StatusID != 2) return null;

            return user;
        }

        public Task<User?> GetUserById(int id) => _userRepository.GetUserById(id);
        public Task<IEnumerable<User>> GetAllUsers() => _userRepository.GetAllUsers();
        public Task<IEnumerable<User>> GetUsersByClubId(int clubId) => _userRepository.GetUsersByClubId(clubId);

        public async Task AddUser(User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            user.Email = NormalizeEmail(user.Email);
            if (string.IsNullOrWhiteSpace(user.Email))
                throw new ArgumentException("Email is required.");

            if (await _userRepository.DoesEmailExist(user.Email))
                throw new ArgumentException("Този имейл вече съществува.");

            if (string.IsNullOrWhiteSpace(user.Password))
                throw new ArgumentException("Password is required.");

            // регистрация = pending
            user.StatusID = 1;

            // нормализирани имена
            user.FirstNameNorm = Norm(user.FirstName);
            user.LastNameNorm = Norm(user.LastName);

            user.Password = _passwordHasher.HashPassword(user, user.Password);
            await _userRepository.AddUser(user);
        }

        public async Task UpdateUser(User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            user.FirstNameNorm = Norm(user.FirstName);
            user.LastNameNorm = Norm(user.LastName);

            await _userRepository.UpdateUser(user);
        }

        public Task DeleteUser(int id) => _userRepository.DeleteUser(id);

        public Task<IEnumerable<User>> SearchUsersByName(string query) => _userRepository.SearchUsersByName(query);

        // ✅ pending за треньора (регистрации + трансфери към клуба)
        public Task<IEnumerable<User>> GetPendingUsers(int clubId) => _userRepository.GetPendingUsersByClubId(clubId);

        // ✅ approve (регистрация или трансфер)
        public Task<bool> ApproveUser(int userId) => _userRepository.ApproveUserOrTransfer(userId);

        // ✅ reject (регистрация или трансфер)
        public Task<bool> RejectUser(int userId) => _userRepository.RejectUserOrTransfer(userId);

        // ✅ заявка за смяна на клуб / join
        public async Task UpdateUserClubStatus(int userId, int clubId)
        {
            var user = await _userRepository.GetUserById(userId);
            if (user == null) throw new ArgumentException("User not found.");

            // ако е одобрен -> трансфер заявка
            if (user.StatusID == 2)
            {
                // ако избере текущия клуб -> чистим pending (ако има)
                if (user.ClubID == clubId)
                {
                    await _userRepository.ClearPendingClub(userId);
                    return;
                }

                await _userRepository.UpdatePendingClub(userId, clubId);
                return;
            }

            // ако НЕ е одобрен -> нормална pending заявка към клуб
            await _userRepository.UpdateUserClubStatus(userId, clubId);
        }

        public Task<bool> DoesEmailExist(string email) => _userRepository.DoesEmailExist(NormalizeEmail(email));

        public async Task UpdatePassword(int userId, string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password is required.");

            var user = await _userRepository.GetUserById(userId);
            if (user == null) throw new ArgumentException("User not found.");

            var hashed = _passwordHasher.HashPassword(user, password);
            await _userRepository.UpdatePassword(userId, hashed);
        }

        public Task<string> UpdateUserProfilePicture(int userId, IFormFile file) =>
            _userRepository.UpdateUserProfilePicture(userId, file);

        public Task<IActionResult> GetUserProfilePicture(int userId) =>
            _userRepository.GetUserProfilePicture(userId);

        // ==========================================
        // SWIMRANKINGS SYNC
        // ==========================================
        public Task<int> SyncSwimrankingsOnLogin(int userId)
            => SyncSwimrankingsInternal(userId, force: false);

        public Task<int> SyncSwimrankingsOnLoginForce(int userId)
            => SyncSwimrankingsInternal(userId, force: true);

        private async Task<int> SyncSwimrankingsInternal(int userId, bool force)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            var cacheRaw = force ? null : await _userRepository.GetSwimrankingsMeetCache(userId);
            var cachedMeetIds = ParseMeetCache(cacheRaw);

            var existingAll = await _resultRepository.GetResultsByUserId(userId);
            var existingKeys = new HashSet<string>(
                existingAll.Select(SwimrankingsService.ResultKey),
                StringComparer.OrdinalIgnoreCase
            );

            var (scraped, processedMeetIds) =
                await _swimrankingsService.ImportAthleteAllResultsNewMeets(userId, cachedMeetIds, cts.Token);

            if (processedMeetIds == null || processedMeetIds.Count == 0)
                return 0;

            var toInsert = (scraped ?? new List<Result>())
                .GroupBy(SwimrankingsService.ResultKey)
                .Select(g => g.First())
                .Where(r => !existingKeys.Contains(SwimrankingsService.ResultKey(r)))
                .ToList();

            if (toInsert.Count > 0)
                await _resultRepository.AddResultsBulk(toInsert);

            foreach (var mid in processedMeetIds)
                cachedMeetIds.Add(mid);

            var newCache = string.Join(",", cachedMeetIds.OrderBy(x => x));
            await _userRepository.UpdateSwimrankingsMeetCache(userId, newCache);

            return toInsert.Count;
        }

        private static HashSet<string> ParseMeetCache(string? raw)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(raw))
                return set;

            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var v = part.Trim();
                if (v.Length > 0) set.Add(v);
            }

            return set;
        }

        // ✅ Нормализатор
        private static string Norm(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return "";

            s = TransliterateBgToLatin(s);
            s = RemoveDiacritics(s);
            s = s.ToUpperInvariant();

            var chars = s.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-').ToArray();
            var cleaned = new string(chars);

            while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
            return cleaned.Replace("-", " ").Trim();
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);

            foreach (var c in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string TransliterateBgToLatin(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var sb = new StringBuilder(input.Length * 2);

            foreach (var ch in input)
            {
                sb.Append(ch switch
                {
                    'А' => "A",
                    'а' => "a",
                    'Б' => "B",
                    'б' => "b",
                    'В' => "V",
                    'в' => "v",
                    'Г' => "G",
                    'г' => "g",
                    'Д' => "D",
                    'д' => "d",
                    'Е' => "E",
                    'е' => "e",
                    'Ж' => "Zh",
                    'ж' => "zh",
                    'З' => "Z",
                    'з' => "z",
                    'И' => "I",
                    'и' => "i",
                    'Й' => "Y",
                    'й' => "y",
                    'К' => "K",
                    'к' => "k",
                    'Л' => "L",
                    'л' => "l",
                    'М' => "M",
                    'м' => "m",
                    'Н' => "N",
                    'н' => "n",
                    'О' => "O",
                    'о' => "o",
                    'П' => "P",
                    'п' => "p",
                    'Р' => "R",
                    'р' => "r",
                    'С' => "S",
                    'с' => "s",
                    'Т' => "T",
                    'т' => "t",
                    'У' => "U",
                    'у' => "u",
                    'Ф' => "F",
                    'ф' => "f",
                    'Х' => "H",
                    'х' => "h",
                    'Ц' => "Ts",
                    'ц' => "ts",
                    'Ч' => "Ch",
                    'ч' => "ch",
                    'Ш' => "Sh",
                    'ш' => "sh",
                    'Щ' => "Sht",
                    'щ' => "sht",
                    'Ъ' => "A",
                    'ъ' => "a",
                    'Ь' => "",
                    'ь' => "",
                    'Ю' => "Yu",
                    'ю' => "yu",
                    'Я' => "Ya",
                    'я' => "ya",
                    _ => ch.ToString()
                });
            }

            return sb.ToString();
        }

        // =========================
        // ACCESS CODE FLOW
        // =========================
        private async Task<string> GenerateUniqueAccessCode()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            while (true)
            {
                var bytes = new byte[10];
                RandomNumberGenerator.Fill(bytes);

                var chars = bytes.Select(b => alphabet[b % alphabet.Length]).ToArray();
                var code = new string(chars); // 10 chars

                if (!await _userRepository.IsAccessCodeTaken(code))
                    return code;
            }
        }

        public sealed class CoachCreateAthleteResult
        {
            public User Athlete { get; set; } = new User();
            public string AccessCode { get; set; } = "";
        }

        public async Task<CoachCreateAthleteResult> CreateAthleteByCoach(
            int coachId, string firstName, string lastName, int yearOfBirth, string gender)
        {
            var coach = await _userRepository.GetUserById(coachId);
            if (coach == null) throw new UnauthorizedAccessException("Няма активна сесия.");
            if (!IsCoachOrAdmin(coach.RoleID)) throw new UnauthorizedAccessException("Само треньор/админ.");

            var code = await GenerateUniqueAccessCode();

            var athlete = new User
            {
                FirstName = firstName?.Trim() ?? "",
                LastName = lastName?.Trim() ?? "",
                YearOfBirth = yearOfBirth,
                Gender = gender ?? "",
                RoleID = 1,
                ClubID = coach.ClubID,         // ✅ клубът идва от треньора
                StatusID = 2,                  // ✅ директно approved
                profileImage_url = "",
                AccessCode = code,
                MustSetCredentials = true
            };

            athlete.FirstNameNorm = Norm(athlete.FirstName);
            athlete.LastNameNorm = Norm(athlete.LastName);

            await _userRepository.AddAthleteByCoach(athlete);

            return new CoachCreateAthleteResult { Athlete = athlete, AccessCode = code };
        }

        public async Task SetupCredentialsForMe(int userId, string email, string password)
        {
            email = NormalizeEmail(email);
            password = (password ?? "").Trim();

            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required.");
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password is required.");

            if (await _userRepository.DoesEmailExist(email))
                throw new ArgumentException("Този имейл вече съществува.");

            var user = await _userRepository.GetUserById(userId);
            if (user == null) throw new ArgumentException("User not found.");

            if (user.RoleID != 1) throw new ArgumentException("Само атлет може да прави това.");
            if (!user.MustSetCredentials) throw new ArgumentException("Профилът вече е активиран.");

            // ✅ доп. защита - да няма странни състояния
            if (!string.IsNullOrWhiteSpace(user.Email) || !string.IsNullOrWhiteSpace(user.Password))
                throw new ArgumentException("Профилът вече има креденшъли.");

            var hashed = _passwordHasher.HashPassword(user, password);
            await _userRepository.SetupCredentials(userId, email, hashed);
        }

        public async Task<User?> GetUserByAccessCodeLogin(string code)
        {
            code = (code ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code)) return null;

            var user = await _userRepository.GetUserByAccessCode(code);
            if (user == null) return null;

            if (user.RoleID != 1) return null;
            if (user.StatusID != 2) return null;
            if (!user.MustSetCredentials) return null; // кодът е само за първи вход

            return user;
        }
        // ✅ отделно: FirstName
        public async Task UpdateFirstNameForMe(int userId, string firstName)
        {
            var user = await _userRepository.GetUserById(userId) ?? throw new ArgumentException("User not found.");
            firstName = (firstName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("FirstName is required.");

            var norm = Norm(firstName);
            await _userRepository.UpdateFirstName(userId, firstName, norm);
        }

        // ✅ отделно: LastName
        public async Task UpdateLastNameForMe(int userId, string lastName)
        {
            var user = await _userRepository.GetUserById(userId) ?? throw new ArgumentException("User not found.");
            lastName = (lastName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("LastName is required.");

            var norm = Norm(lastName);
            await _userRepository.UpdateLastName(userId, lastName, norm);
        }

        // ✅ отделно: Gender
        public async Task UpdateGenderForMe(int userId, string gender)
        {
            var user = await _userRepository.GetUserById(userId) ?? throw new ArgumentException("User not found.");
            gender = (gender ?? "").Trim();
            if (string.IsNullOrWhiteSpace(gender)) throw new ArgumentException("Gender is required.");

            await _userRepository.UpdateGender(userId, gender);
        }

        // ✅ отделно: YearOfBirth
        public async Task UpdateYearOfBirthForMe(int userId, int yearOfBirth)
        {
            var user = await _userRepository.GetUserById(userId) ?? throw new ArgumentException("User not found.");
            if (yearOfBirth < 1900 || yearOfBirth > DateTime.UtcNow.Year) throw new ArgumentException("Invalid YearOfBirth.");

            await _userRepository.UpdateYearOfBirth(userId, yearOfBirth);
        }

        // ✅ отделно: Change Password (със стара парола)
        public async Task ChangePasswordForMe(int userId, string oldPassword, string newPassword)
        {
            oldPassword = (oldPassword ?? "").Trim();
            newPassword = (newPassword ?? "").Trim();

            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("Password is required.");

            var user = await _userRepository.GetUserById(userId);
            if (user == null) throw new ArgumentException("User not found.");

            if (string.IsNullOrWhiteSpace(user.Password))
                throw new ArgumentException("Профилът няма парола (setup-credentials).");

            var vr = _passwordHasher.VerifyHashedPassword(user, user.Password, oldPassword);
            if (vr != PasswordVerificationResult.Success)
                throw new ArgumentException("Грешна стара парола.");

            var hashed = _passwordHasher.HashPassword(user, newPassword);
            await _userRepository.UpdatePassword(userId, hashed);
        }

        // ✅ отделно: Change Email (с парола)
        public async Task ChangeEmailForMe(int userId, string newEmail, string password)
        {
            newEmail = NormalizeEmail(newEmail);
            password = (password ?? "").Trim();

            if (string.IsNullOrWhiteSpace(newEmail))
                throw new ArgumentException("Email is required.");

            if (await _userRepository.DoesEmailExist(newEmail))
                throw new ArgumentException("Този имейл вече съществува.");

            var user = await _userRepository.GetUserById(userId);
            if (user == null) throw new ArgumentException("User not found.");

            if (string.IsNullOrWhiteSpace(user.Password))
                throw new ArgumentException("Профилът няма парола (setup-credentials).");

            var vr = _passwordHasher.VerifyHashedPassword(user, user.Password, password);
            if (vr != PasswordVerificationResult.Success)
                throw new ArgumentException("Грешна парола.");

            await _userRepository.UpdateEmail(userId, newEmail);
        }
        public sealed class CoachAthleteAccessCodeRow
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public int YearOfBirth { get; set; }
            public string profileImage_url { get; set; } = "";

            // ✅ В това поле ще върнем AccessCode ИЛИ Swrid (fallback)
            public string? AccessCode { get; set; }

            public string? Swrid { get; set; }   // optional, ако искаш да го имаш отделно
            public bool MustSetCredentials { get; set; }
        }

        public async Task<IEnumerable<CoachAthleteAccessCodeRow>> GetCoachAthleteAccessCodes(int coachId)
        {
            var coach = await _userRepository.GetUserById(coachId);
            if (coach == null) throw new UnauthorizedAccessException("Няма активна сесия.");
            if (!IsCoachOrAdmin(coach.RoleID)) throw new UnauthorizedAccessException("Само треньор/админ.");

            var athletes = await _userRepository.GetAthletesByClubId(coach.ClubID);

            return athletes.Select(a => new CoachAthleteAccessCodeRow
            {
                Id = a.Id,
                FirstName = a.FirstName ?? "",
                LastName = a.LastName ?? "",
                YearOfBirth = a.YearOfBirth,
                profileImage_url = a.profileImage_url ?? "",

                // ✅ ако няма access code, показваме SWRID
                AccessCode = !string.IsNullOrWhiteSpace(a.AccessCode)
                    ? a.AccessCode
                    : (string.IsNullOrWhiteSpace(a.Swrid) ? null : a.Swrid),

                Swrid = a.Swrid,
                MustSetCredentials = a.MustSetCredentials
            }).ToList();
        }
    }
}