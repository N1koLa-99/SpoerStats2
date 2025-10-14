namespace SpoerStats2.Models
{
    public class AthleteLicense
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int UserId { get; set; }
        public string LicenseNo { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? AttachmentUrl { get; set; }
        public string? Notes { get; set; }
    }
}
