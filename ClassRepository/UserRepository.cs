using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using SpoerStats2.Models;
using SpoerStats2.Repository;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;

namespace SpoerStats2.ClassRepository
{
    public class UserRepository : IUserRepository
    {
        private readonly string _connectionString;

        public UserRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task<bool> DoesEmailExist(string email)
        {
            using var connection = new SqlConnection(_connectionString);

            // ✅ robust: trim + lower + ignore NULL
            var query = @"
                SELECT COUNT(1)
                FROM Users
                WHERE Email IS NOT NULL
                  AND LOWER(LTRIM(RTRIM(Email))) = @Email";

            var count = await connection.ExecuteScalarAsync<int>(query, new { Email = (email ?? "").Trim().ToLowerInvariant() });
            return count > 0;
        }

        public async Task<IEnumerable<User>> GetAllUsers()
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<User>("SELECT * FROM Users");
        }

        public async Task<User?> GetUserById(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id", new { Id = id });
        }

        public async Task<User?> GetUserByEmail(string email)
        {
            using var connection = new SqlConnection(_connectionString);

            // ✅ robust: trim + lower + ignore NULL
            var query = @"
                SELECT TOP 1 *
                FROM Users
                WHERE Email IS NOT NULL
                  AND LOWER(LTRIM(RTRIM(Email))) = @Email";

            return await connection.QuerySingleOrDefaultAsync<User>(
                query,
                new { Email = (email ?? "").Trim().ToLowerInvariant() }
            );
        }

        public async Task<User?> GetUserByEmailAndPassword(string email, string password)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "SELECT * FROM Users WHERE Email = @Email AND Password = @Password";
            return await connection.QuerySingleOrDefaultAsync<User>(query, new { Email = email, Password = password });
        }

        public async Task AddUser(User user)
        {
            using var connection = new SqlConnection(_connectionString);

            var query =
                @"INSERT INTO Users
                  (FirstName, LastName, Email, Password, Gender, RoleID, ClubID, PendingClubID, profileImage_url, YearOfBirth, StatusID,
                   SwimrankingsId, SwimrankingsMeetCache, Swrid, FirstNameNorm, LastNameNorm)
                  OUTPUT INSERTED.Id
                  VALUES
                  (@FirstName, @LastName, @Email, @Password, @Gender, @RoleID, @ClubID, @PendingClubID, @profileImage_url, @YearOfBirth, @StatusID,
                   @SwimrankingsId, @SwimrankingsMeetCache, @Swrid, @FirstNameNorm, @LastNameNorm)";

            user.Id = await connection.QuerySingleAsync<int>(query, new
            {
                user.FirstName,
                user.LastName,
                user.Email,
                user.Password,
                user.Gender,
                user.RoleID,
                user.ClubID,
                user.PendingClubID,
                user.profileImage_url,
                user.YearOfBirth,
                user.StatusID,
                user.SwimrankingsId,
                user.SwimrankingsMeetCache,
                user.Swrid,
                user.FirstNameNorm,
                user.LastNameNorm
            });
        }

        public async Task<IEnumerable<User>> GetUsersByClubId(int clubId)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "SELECT * FROM Users WHERE ClubID = @ClubID";
            return await connection.QueryAsync<User>(query, new { ClubID = clubId });
        }

        public async Task<IEnumerable<User>> GetPendingUsersByClubId(int clubId)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = @"
                SELECT * FROM Users
                WHERE (ClubID = @ClubID AND StatusID = 1)
                   OR (PendingClubID = @ClubID)";
            return await connection.QueryAsync<User>(query, new { ClubID = clubId });
        }

        public async Task<bool> ApproveUserOrTransfer(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = @"
                UPDATE Users
                SET
                    ClubID = COALESCE(PendingClubID, ClubID),
                    PendingClubID = NULL,
                    StatusID = 2
                WHERE Id = @UserId";
            var rows = await connection.ExecuteAsync(query, new { UserId = userId });
            return rows > 0;
        }

        public async Task<bool> RejectUserOrTransfer(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = @"
                UPDATE Users
                SET
                    PendingClubID = NULL,
                    StatusID = CASE WHEN StatusID = 1 THEN 3 ELSE StatusID END
                WHERE Id = @UserId";
            var rows = await connection.ExecuteAsync(query, new { UserId = userId });
            return rows > 0;
        }

        public async Task UpdatePendingClub(int userId, int pendingClubId)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "UPDATE Users SET PendingClubID = @PendingClubID WHERE Id = @UserId";
            await connection.ExecuteAsync(query, new { PendingClubID = pendingClubId, UserId = userId });
        }

        public async Task ClearPendingClub(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "UPDATE Users SET PendingClubID = NULL WHERE Id = @UserId";
            await connection.ExecuteAsync(query, new { UserId = userId });
        }

        public async Task UpdateUserClubStatus(int userId, int clubId)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "UPDATE Users SET ClubID = @ClubID, StatusID = 1, PendingClubID = NULL WHERE Id = @UserId";
            await connection.ExecuteAsync(query, new { ClubID = clubId, UserId = userId });
        }

        public async Task UpdateUser(User user)
        {
            using var connection = new SqlConnection(_connectionString);

            var query =
                @"UPDATE Users SET
                    FirstName = @FirstName,
                    LastName = @LastName,
                    Email = @Email,
                    Gender = @Gender,
                    profileImage_url = @profileImage_url,
                    Password = @Password,
                    YearOfBirth = @YearOfBirth,
                    StatusID = @StatusID,
                    RoleID = @RoleID,
                    ClubID = @ClubID,
                    PendingClubID = @PendingClubID,
                    SwimrankingsId = @SwimrankingsId,
                    SwimrankingsMeetCache = @SwimrankingsMeetCache,
                    Swrid = @Swrid,
                    FirstNameNorm = @FirstNameNorm,
                    LastNameNorm = @LastNameNorm
                  WHERE Id = @Id";

            await connection.ExecuteAsync(query, new
            {
                user.FirstName,
                user.LastName,
                user.Email,
                user.Gender,
                user.profileImage_url,
                user.Password,
                user.YearOfBirth,
                user.StatusID,
                user.RoleID,
                user.ClubID,
                user.PendingClubID,
                user.SwimrankingsId,
                user.SwimrankingsMeetCache,
                user.Swrid,
                user.FirstNameNorm,
                user.LastNameNorm,
                user.Id
            });
        }

        public async Task DeleteUser(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "DELETE FROM Users WHERE Id = @Id";
            await connection.ExecuteAsync(query, new { Id = id });
        }

        public async Task UpdatePassword(int id, string password)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "UPDATE Users SET Password = @Password WHERE Id = @Id";
            await connection.ExecuteAsync(query, new { Password = password, Id = id });
        }

        public async Task<IEnumerable<User>> SearchUsersByName(string query)
        {
            using var connection = new SqlConnection(_connectionString);
            var sqlQuery = "SELECT * FROM Users WHERE FirstName LIKE @Query OR LastName LIKE @Query";
            return await connection.QueryAsync<User>(sqlQuery, new { Query = $"%{query}%" });
        }

        public async Task<User?> GetUserBySwimrankingsId(string swimId)
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE SwimrankingsId = @SwimId", new { SwimId = swimId });
        }

        public async Task UpdateSwimrankingsId(int userId, string swimId)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "UPDATE Users SET SwimrankingsId = @SwimId WHERE Id = @Id";
            await connection.ExecuteAsync(query, new { SwimId = swimId, Id = userId });
        }

        public async Task<string?> GetSwimrankingsMeetCache(int userId)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "SELECT SwimrankingsMeetCache FROM Users WHERE Id = @Id";
            return await connection.ExecuteScalarAsync<string?>(query, new { Id = userId });
        }

        public async Task UpdateSwimrankingsMeetCache(int userId, string meetCache)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = "UPDATE Users SET SwimrankingsMeetCache = @Cache WHERE Id = @Id";
            await connection.ExecuteAsync(query, new { Cache = meetCache, Id = userId });
        }

        public async Task<User?> GetUserBySwrid(string swrid)
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Swrid = @Swrid", new { Swrid = swrid });
        }

        public async Task<bool> IsSwridTaken(string swrid)
        {
            using var connection = new SqlConnection(_connectionString);
            var n = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Users WHERE Swrid = @Swrid", new { Swrid = swrid });
            return n > 0;
        }

        public async Task UpdateSwrid(int userId, string swrid)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync(
                "UPDATE Users SET Swrid = @Swrid WHERE Id = @Id",
                new { Swrid = swrid, Id = userId });
        }

        public async Task<string> UpdateUserProfilePicture(int userId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("Файлът е празен.");

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".jpg";

            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "profile-pictures");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"{userId}_{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(uploadsDir, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var url = $"/profile-pictures/{fileName}";

            using (var connection = new SqlConnection(_connectionString))
            {
                var q = "UPDATE Users SET profileImage_url = @Url WHERE Id = @Id";
                await connection.ExecuteAsync(q, new { Url = url, Id = userId });
            }

            return url;
        }

        public async Task<IActionResult> GetUserProfilePicture(int userId)
        {
            string? url;

            using (var connection = new SqlConnection(_connectionString))
            {
                url = await connection.ExecuteScalarAsync<string?>(
                    "SELECT profileImage_url FROM Users WHERE Id = @Id", new { Id = userId });
            }

            if (string.IsNullOrWhiteSpace(url))
                return new NotFoundResult();

            var rel = url.StartsWith("/") ? url.Substring(1) : url;
            var fullPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                rel.Replace("/", Path.DirectorySeparatorChar.ToString()));

            if (!System.IO.File.Exists(fullPath))
                return new NotFoundResult();

            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fullPath, out var contentType))
                contentType = "application/octet-stream";

            return new PhysicalFileResult(fullPath, contentType);
        }

        public async Task<bool> IsAccessCodeTaken(string code)
        {
            using var connection = new SqlConnection(_connectionString);
            var n = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Users WHERE AccessCode = @Code",
                new { Code = code }
            );
            return n > 0;
        }

        public async Task<User?> GetUserByAccessCode(string code)
        {
            using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE AccessCode = @Code",
                new { Code = code }
            );
        }

        public async Task AddAthleteByCoach(User athlete)
        {
            using var connection = new SqlConnection(_connectionString);

            var query = @"
                INSERT INTO Users
                (FirstName, LastName, Email, Password, Gender, RoleID, ClubID, PendingClubID, profileImage_url,
                 YearOfBirth, StatusID, AccessCode, MustSetCredentials, FirstNameNorm, LastNameNorm)
                OUTPUT INSERTED.Id
                VALUES
                (@FirstName, @LastName, NULL, NULL, @Gender, @RoleID, @ClubID, NULL, @profileImage_url,
                 @YearOfBirth, @StatusID, @AccessCode, @MustSetCredentials, @FirstNameNorm, @LastNameNorm)";

            athlete.Id = await connection.QuerySingleAsync<int>(query, new
            {
                athlete.FirstName,
                athlete.LastName,
                athlete.Gender,
                athlete.RoleID,
                athlete.ClubID,
                athlete.profileImage_url,
                athlete.YearOfBirth,
                athlete.StatusID,
                athlete.AccessCode,
                athlete.MustSetCredentials,
                athlete.FirstNameNorm,
                athlete.LastNameNorm
            });
        }

        public async Task SetupCredentials(int userId, string email, string hashedPassword)
        {
            using var connection = new SqlConnection(_connectionString);

            var q = @"
                UPDATE Users
                SET Email = @Email,
                    Password = @Password,
                    MustSetCredentials = 0,
                    AccessCode = NULL
                WHERE Id = @Id";

            await connection.ExecuteAsync(q, new { Email = (email ?? "").Trim().ToLowerInvariant(), Password = hashedPassword, Id = userId });
        }
        public async Task UpdateFirstName(int userId, string firstName, string firstNameNorm)
        {
            using var connection = new SqlConnection(_connectionString);
            var q = "UPDATE Users SET FirstName=@FirstName, FirstNameNorm=@FirstNameNorm WHERE Id=@Id";
            await connection.ExecuteAsync(q, new { FirstName = firstName, FirstNameNorm = firstNameNorm, Id = userId });
        }

        public async Task UpdateLastName(int userId, string lastName, string lastNameNorm)
        {
            using var connection = new SqlConnection(_connectionString);
            var q = "UPDATE Users SET LastName=@LastName, LastNameNorm=@LastNameNorm WHERE Id=@Id";
            await connection.ExecuteAsync(q, new { LastName = lastName, LastNameNorm = lastNameNorm, Id = userId });
        }

        public async Task UpdateGender(int userId, string gender)
        {
            using var connection = new SqlConnection(_connectionString);
            var q = "UPDATE Users SET Gender=@Gender WHERE Id=@Id";
            await connection.ExecuteAsync(q, new { Gender = gender, Id = userId });
        }

        public async Task UpdateYearOfBirth(int userId, int yearOfBirth)
        {
            using var connection = new SqlConnection(_connectionString);
            var q = "UPDATE Users SET YearOfBirth=@YearOfBirth WHERE Id=@Id";
            await connection.ExecuteAsync(q, new { YearOfBirth = yearOfBirth, Id = userId });
        }

        public async Task UpdateEmail(int userId, string email)
        {
            using var connection = new SqlConnection(_connectionString);
            var q = "UPDATE Users SET Email=@Email WHERE Id=@Id";
            await connection.ExecuteAsync(q, new { Email = (email ?? "").Trim().ToLowerInvariant(), Id = userId });
        }

        // ❗ admin-only “директно местене” (ако искаш)
        public async Task SetUserClub(int userId, int clubId)
        {
            using var connection = new SqlConnection(_connectionString);
            var q = "UPDATE Users SET ClubID=@ClubID WHERE Id=@Id";
            await connection.ExecuteAsync(q, new { ClubID = clubId, Id = userId });
        }
        public async Task<IEnumerable<Result>> GetResultsByClubAndDiscipline(int clubId, int disciplineId)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
SELECT r.*
FROM Results r
INNER JOIN Users u ON u.Id = r.UserId
WHERE u.ClubID = @ClubId
  AND r.DisciplineId = @DisciplineId
ORDER BY r.ValueTime ASC, r.ResultDate DESC;";

            return await connection.QueryAsync<Result>(sql, new
            {
                ClubId = clubId,
                DisciplineId = disciplineId
            });
        }
        public async Task<IEnumerable<User>> GetAthletesByClubId(int clubId)
        {
            using var connection = new SqlConnection(_connectionString);

            var q = @"
    SELECT
        Id,
        FirstName,
        LastName,
        YearOfBirth,
        profileImage_url,
        AccessCode,
        Swrid,             
        MustSetCredentials,
        RoleID,
        ClubID,
        StatusID
    FROM Users
    WHERE ClubID = @ClubID
      AND RoleID = 1         -- athlete
      AND StatusID = 2       -- approved
    ORDER BY LastName, FirstName, YearOfBirth";

            return await connection.QueryAsync<User>(q, new { ClubID = clubId });
        }
    }
}