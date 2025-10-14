namespace SpoerStats2.Models
{
    public class AthleteSlotSelection
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int UserId { get; set; }
        public int SlotId { get; set; }
        public DateTime SelectedAt { get; set; }
    }
}
