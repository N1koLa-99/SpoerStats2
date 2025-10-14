namespace SpoerStats2.Models
{
    public class VwAthleteLicense
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int UserId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string LicenseNo { get; set; } = "";
        public DateTime IssuedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? Notes { get; set; }
    }
}
