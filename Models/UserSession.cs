using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SpoerStats2.Models
{
    public class UserSession
    {
        public UserSession(User dbUser)
        {
            Id = dbUser.Id;
            FirstName = dbUser.FirstName ?? "";
            LastName = dbUser.LastName ?? "";
            Email = dbUser.Email; // ✅ nullable
            Gender = dbUser.Gender ?? "";
            RoleID = dbUser.RoleID;
            ClubID = dbUser.ClubID;
            profileImage_url = dbUser.profileImage_url ?? "";
            YearOfBirth = dbUser.YearOfBirth;
            StatusID = dbUser.StatusID;

            // ✅ реално го сетваме
            UserTokenHash = GetUserTokenHash();
        }

        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";

        // ✅ да отговаря на User (nullable)
        public string? Email { get; set; }

        public string Gender { get; set; } = "";
        public int RoleID { get; set; }
        public int ClubID { get; set; }

        // ✅ вече се сетва в ctor
        public string UserTokenHash { get; private set; } = "";

        public string profileImage_url { get; set; } = "";
        public int YearOfBirth { get; set; }
        public int StatusID { get; set; }

        /// <summary>
        /// SHA256 hash (fingerprint). НЕ е защита срещу подмяна без secret.
        /// </summary>
        public string GetUserTokenHash()
        {
            var payload = GetCanonicalPayload();
            return ComputeSha256Base64(payload);
        }

        /// <summary>
        /// HMACSHA256 подпис (реална защита срещу "пипане"), ако secret е само на сървъра.
        /// </summary>
        public string GetUserTokenSignature(string secret)
        {
            var payload = GetCanonicalPayload();
            return ComputeHmacSha256Base64(payload, secret);
        }

        private string GetCanonicalPayload()
        {
            // ✅ разделители + trim + invariant числа => няма двусмислие
            return string.Join("|",
                Id.ToString(CultureInfo.InvariantCulture),
                (FirstName ?? "").Trim(),
                (LastName ?? "").Trim(),
                (Email ?? "").Trim(),
                (Gender ?? "").Trim(),
                RoleID.ToString(CultureInfo.InvariantCulture),
                ClubID.ToString(CultureInfo.InvariantCulture),
                (profileImage_url ?? "").Trim(),
                YearOfBirth.ToString(CultureInfo.InvariantCulture),
                StatusID.ToString(CultureInfo.InvariantCulture)
            );
        }

        private static string ComputeSha256Base64(string data)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(bytes);
        }

        private static string ComputeHmacSha256Base64(string data, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(bytes);
        }
    }
}