namespace SpoerStats2.Models
{
    public class ScheduleSlot
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public int? ClubSeasonId { get; set; }
        /// <summary>1=Пон … 7=Нед</summary>
        public byte Weekday { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string? Location { get; set; }
        public int? Capacity { get; set; }
        // SeasonKey е computed-колона в SQL; не е нужно тук
    }
}
