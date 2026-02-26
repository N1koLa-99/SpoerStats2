using System.Collections.Generic;

namespace SpoerStats2.Models
{
    public class LenexImportReport
    {
        public int Inserted { get; set; }               // резултати
        public int InsertedAthletes { get; set; }       // нови атлети
        public int UpdatedSWRIDs { get; set; }          // update на Swrid върху съществуващи
        public int Skipped_NotInApp { get; set; }       // вече няма да е проблем, но го оставям (ако не можем да създадем)
        public int Skipped_ClubMismatch { get; set; }
        public int Skipped_ClubNotFound { get; set; }
        public int Skipped_AmbiguousUser { get; set; }
        public int Skipped_NoTime { get; set; }
        public int Skipped_UnknownDiscipline { get; set; }
        public int Skipped_Duplicates { get; set; }

        public List<string> UnknownDisciplines { get; set; } = new();
        public List<string> SkippedAthletesSample { get; set; } = new();
        public List<string> ClubNotFoundSample { get; set; } = new();
    }
}