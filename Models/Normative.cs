namespace SpoerStats2.Models
{
    public class Normative
    {
        public int Id { get; set; }
        public int DisciplineId { get; set; }
        public string Gender { get; set; }
        public int MinAge { get; set; }
        public int MaxAge { get; set; }
        public decimal ValueStandart { get; set; }
        public int Points { get; set; }
        public int SwimmingPoolStandartId { get; set; }
        public DateTime MinYearOfBirth { get; set; }
        public DateTime MaxYearOfBirth { get; set; }
        public int MinYearOfBorn { get; set; }
        public int MaxYearOfBorn { get; set; }
    }
}
