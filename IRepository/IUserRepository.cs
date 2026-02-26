using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpoerStats2.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpoerStats2.Repository
{
    public interface IUserRepository
    {
        Task<bool> DoesEmailExist(string email);

        Task<IEnumerable<User>> GetAllUsers();
        Task<User?> GetUserById(int id);
        Task<User?> GetUserByEmail(string email);

        Task<User?> GetUserByEmailAndPassword(string email, string password); // legacy

        Task AddUser(User user);

        // ❗ може да го оставиш, но вече няма да го ползваме за профилни промени
        Task UpdateUser(User user);

        Task DeleteUser(int id);

        Task<IEnumerable<User>> GetUsersByClubId(int clubId);

        Task<IEnumerable<User>> GetPendingUsersByClubId(int clubId);

        Task<bool> ApproveUserOrTransfer(int userId);
        Task<bool> RejectUserOrTransfer(int userId);

        Task UpdatePendingClub(int userId, int pendingClubId);
        Task ClearPendingClub(int userId);

        Task UpdateUserClubStatus(int userId, int clubId);

        Task UpdatePassword(int id, string password);

        Task<IEnumerable<User>> SearchUsersByName(string query);

        // Swimrankings legacy
        Task<User?> GetUserBySwimrankingsId(string swimId);
        Task UpdateSwimrankingsId(int userId, string swimId);
        Task<string?> GetSwimrankingsMeetCache(int userId);
        Task UpdateSwimrankingsMeetCache(int userId, string meetCache);

        // ✅ LENEX/SWRID
        Task<User?> GetUserBySwrid(string swrid);
        Task<bool> IsSwridTaken(string swrid);
        Task UpdateSwrid(int userId, string swrid);

        // profile picture
        Task<string> UpdateUserProfilePicture(int userId, IFormFile file);
        Task<IActionResult> GetUserProfilePicture(int userId);

        // access code
        Task<bool> IsAccessCodeTaken(string code);
        Task<User?> GetUserByAccessCode(string code);
        Task AddAthleteByCoach(User athlete);
        Task SetupCredentials(int userId, string email, string hashedPassword);

        // =========================
        // ✅ NEW: granular updates
        // =========================
        Task UpdateFirstName(int userId, string firstName, string firstNameNorm);
        Task UpdateLastName(int userId, string lastName, string lastNameNorm);
        Task UpdateGender(int userId, string gender);
        Task UpdateYearOfBirth(int userId, int yearOfBirth);

        Task UpdateEmail(int userId, string email);

        // (по желание) admin only
        Task SetUserClub(int userId, int clubId);
        Task<IEnumerable<User>> GetAthletesByClubId(int clubId);
    }

}