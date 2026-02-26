namespace SpoerStats2.Models
{
    public sealed class CoachCreateAthleteDto
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public int YearOfBirth { get; set; }
        public string Gender { get; set; } = "";
    }

    public sealed class AccessCodeLoginDto
    {
        public string Code { get; set; } = "";
    }

    public sealed class SetupCredentialsDto
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
