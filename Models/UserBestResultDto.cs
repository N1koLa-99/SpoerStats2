namespace SpoerStats2.Models
{
    public class UserBestResultDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string UserFirstName { get; set; } = string.Empty;
        public string UserLastName { get; set; } = string.Empty;
        public int UserYearOfBirth { get; set; }

        public int DisciplineId { get; set; }
        public decimal ValueTime { get; set; }
        public DateTime ResultDate { get; set; }
        public int SwimmingPoolStandart { get; set; }
        public string Location { get; set; } = string.Empty;
    }
}
