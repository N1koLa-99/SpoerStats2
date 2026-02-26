using System;

namespace SpoerStats2.Models
{
    public class User
    {
        public int Id { get; set; }

        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";

        public string? Email { get; set; }          // ✅ nullable
        public string? Password { get; set; }       // ✅ nullable

        public string Gender { get; set; } = "";

        public int RoleID { get; set; }             // 1 athlete, 2 coach, 3 admin
        public int ClubID { get; set; }
        public int? PendingClubID { get; set; }

        public string profileImage_url { get; set; } = "";

        public int YearOfBirth { get; set; }
        public int StatusID { get; set; }

        public string? SwimrankingsId { get; set; }
        public string? SwimrankingsMeetCache { get; set; }

        public string? Swrid { get; set; }

        public string? FirstNameNorm { get; set; }
        public string? LastNameNorm { get; set; }

        // ✅ NEW
        public string? AccessCode { get; set; }
        public bool MustSetCredentials { get; set; }
    }
}