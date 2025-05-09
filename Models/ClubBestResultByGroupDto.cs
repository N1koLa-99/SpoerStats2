namespace SpoerStats2.Models
{
    public class ClubBestResultByGroupDto
    {
        public string AgeGroup { get; set; } // "A", "B", "C"
        public ClubBestResultDto BestResult { get; set; }
    }
}
