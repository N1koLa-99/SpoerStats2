using System.ComponentModel.DataAnnotations;

namespace SpoerStats2.Models
{
    public class ResultSplit
    {
        public int Id { get; set; }
        public int ResultId { get; set; }

        [Range(1, 20000)]
        public int Distance { get; set; } // meters

        [Range(0, 86400)]
        public decimal ValueTime { get; set; } // seconds (cumulative)
    }
}