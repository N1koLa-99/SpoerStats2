// ExportService.cs
using ClosedXML.Excel;
using SpoerStats2.Models;
using System.Collections.Generic;
using System.IO;

namespace SpoerStats2.Service
{
    public class ExportService
    {
        public byte[] ExportResultsToExcel(IEnumerable<Result> results)
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Results");

                worksheet.Cell(1, 1).Value = "User ID";
                worksheet.Cell(1, 2).Value = "Дисциплина ID";
                worksheet.Cell(1, 3).Value = "Време (сек)";
                worksheet.Cell(1, 4).Value = "Дата";
                worksheet.Cell(1, 5).Value = "Басейн";
                worksheet.Cell(1, 6).Value = "Локация";

                var headerRow = worksheet.Range("A1:F1");
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

                var row = 2;
                foreach (var res in results)
                {
                    worksheet.Cell(row, 1).Value = res.UserId;
                    worksheet.Cell(row, 2).Value = res.DisciplineId;
                    worksheet.Cell(row, 3).Value = res.ValueTime;
                    worksheet.Cell(row, 4).Value = res.ResultDate;
                    worksheet.Cell(row, 5).Value = res.SwimmingPoolStandart;
                    worksheet.Cell(row, 6).Value = res.Location;
                    row++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }
    }
}
