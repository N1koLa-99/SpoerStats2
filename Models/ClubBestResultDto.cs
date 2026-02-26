namespace SpoerStats2.Models
{
    public class ClubBestResultDto
    {
        public int ClubId { get; set; }
        public string? ClubName { get; set; }

        public int UserId { get; set; }
        public string? UserFirstName { get; set; }
        public string? UserLastName { get; set; }
        public int YearOfBirth { get; set; }

        public decimal ValueTime { get; set; }
        public DateTime ResultDate { get; set; }
        public string? Location { get; set; }
    }
}