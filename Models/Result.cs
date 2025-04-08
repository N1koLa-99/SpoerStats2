using System.ComponentModel.DataAnnotations;

namespace SpoerStats2.Models
{
    public class Result
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int DisciplineId { get; set; }

        [Range(0, 86400, ErrorMessage = "Времето трябва да бъде между 0 и 86400 секунди.")]
        public decimal ValueTime { get; set; }

        public DateTime ResultDate { get; set; }

        [Range(10, 50, ErrorMessage = "Стандартът на басейна трябва да бъде от 10 до 50 метра.")]
        public int SwimmingPoolStandart { get; set; } 

        [StringLength(255)]
        public string Location { get; set; } 
    }
}
