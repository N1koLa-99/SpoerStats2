namespace SpoerStats2.Models
{
    public class ExpectedDtos
    {
        public record ExpectedUserRow(int UserId, string FullName, int TrainingsPerWeek, decimal Fee);
        public record ExpectedByUserResult(string YearMonth, int? SeasonId, int Count, decimal Total, IEnumerable<ExpectedUserRow> Rows);
    }
}
