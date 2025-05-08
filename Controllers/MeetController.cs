using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using System.Xml.Linq;
using SpoerStats2.Models; // ако ще ползваш модел

[ApiController]
[Route("api/[controller]")]
public class MeetController : ControllerBase
{
    [HttpPost("upload")]
    public async Task<IActionResult> UploadZip([FromForm] IFormFile zipFile)
    {
        if (zipFile == null || zipFile.Length == 0)
            return BadRequest("Файлът е невалиден.");

        string extractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(extractPath);

        using (var zipStream = zipFile.OpenReadStream())
        using (var archive = new ZipArchive(zipStream))
        {
            archive.ExtractToDirectory(extractPath);
        }

        var xmlFile = Directory.GetFiles(extractPath, "*.xml").FirstOrDefault();
        if (xmlFile == null)
            return BadRequest("XML файлът не бе намерен в архива.");

        var xml = XDocument.Load(xmlFile);

        var events = xml.Descendants("EVENT")
            .Select(e => new EventModel
            {
                EventId = (int)e.Attribute("eventid"),
                Gender = (string)e.Attribute("gender"),
                Distance = (int?)e.Element("SWIMSTYLE")?.Attribute("distance") ?? 0,
                Stroke = (string)e.Element("SWIMSTYLE")?.Attribute("stroke") ?? "N/A"
            })
            .ToList();

        return Ok(events);
    }
}
