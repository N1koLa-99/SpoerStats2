using System.Security.Cryptography;
using System.Text;

namespace SpoerStats2.Service
{
    public sealed class AdminKeyValidator
    {
        private readonly string _key;

        public AdminKeyValidator(string key) => _key = key ?? "";

        public bool IsValid(string? provided)
        {
            if (string.IsNullOrWhiteSpace(_key) || string.IsNullOrWhiteSpace(provided))
                return false;

            var a = Encoding.UTF8.GetBytes(_key.Trim());
            var b = Encoding.UTF8.GetBytes(provided.Trim());

            if (a.Length != b.Length) return false;

            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
