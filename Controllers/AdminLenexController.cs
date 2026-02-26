using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpoerStats2.Service;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpoerStats2.Models;

namespace SpoerStats2.Controllers
{
    [Route("api/admin")]
    [ApiController]
    public sealed class AdminLenexController : ControllerBase
    {
        private readonly LenexImportService _lenexImportService;
        private readonly IWebHostEnvironment _env;
        private readonly AdminImportOptions _opts;
        private readonly AdminKeyValidator _keyValidator;
        private readonly ILogger<AdminLenexController> _logger;

        public AdminLenexController(
            LenexImportService lenexImportService,
            IWebHostEnvironment env,
            IOptions<AdminImportOptions> opts,
            AdminKeyValidator keyValidator,
            ILogger<AdminLenexController> logger)
        {
            _lenexImportService = lenexImportService;
            _env = env;
            _opts = opts.Value;
            _keyValidator = keyValidator;
            _logger = logger;
        }

        private bool AdminImportAllowed()
        {
            return _env.IsDevelopment() || _opts.Enabled;
        }

        [HttpPost("import-lenex")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(100_000_000)]
        public async Task<IActionResult> ImportLenexAdmin(
            [FromForm] LenexUploadDto dto,
            [FromHeader(Name = "X-Admin-Key")] string adminKey)
        {
            var rid = HttpContext.TraceIdentifier;

            _logger.LogInformation("[{Rid}] Admin import called. Allowed={Allowed} EnvDev={Dev} Enabled={Enabled}",
                rid, AdminImportAllowed(), _env.IsDevelopment(), _opts.Enabled);

            if (!AdminImportAllowed())
                return NotFound();

            if (!_keyValidator.IsValid(adminKey))
            {
                _logger.LogWarning("[{Rid}] Admin import rejected: bad key.", rid);
                return Unauthorized("Bad admin key.");
            }

            var file = dto?.File;
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("[{Rid}] Admin import rejected: missing/empty file.", rid);
                return BadRequest("Липсва файл.");
            }

            _logger.LogInformation("[{Rid}] File received: {Name} len={Len} ct={CT}",
                rid, file.FileName, file.Length, file.ContentType);

            var ext = Path.GetExtension(file.FileName);
            var allowedExts = new[] { ".xml", ".lenex", ".lef" };

            var extAllowed =
                !string.IsNullOrWhiteSpace(ext) &&
                allowedExts.Contains(ext, StringComparer.OrdinalIgnoreCase);

            if (!extAllowed)
            {
                var head = await ReadFileHeadAsync(file, 4096);
                _logger.LogInformation("[{Rid}] File head (first 300 chars): {Head}",
                    rid, head.Length > 300 ? head.Substring(0, 300) : head);

                if (head.IndexOf("<LENEX", StringComparison.OrdinalIgnoreCase) < 0)
                    return BadRequest("Файлът трябва да е LENEX XML (.xml/.lenex/.lef) или да съдържа <LENEX>.");
            }

            try
            {
                var repAll = await _lenexImportService.ImportForAllClubs(file);

                _logger.LogInformation("[{Rid}] Admin import finished. Inserted={Inserted} Athletes={Ath} UpdatedSWRIDs={Upd} SkipNotInApp={Skip} Dup={Dup} UnknownDisc={Unk}",
                    rid, repAll.Inserted, repAll.InsertedAthletes, repAll.UpdatedSWRIDs, repAll.Skipped_NotInApp, repAll.Skipped_Duplicates, repAll.Skipped_UnknownDiscipline);

                if (repAll.SkippedAthletesSample?.Count > 0)
                    _logger.LogInformation("[{Rid}] Skipped sample: {Sample}", rid, string.Join(" || ", repAll.SkippedAthletesSample.Take(10)));

                if (repAll.ClubNotFoundSample?.Count > 0)
                    _logger.LogInformation("[{Rid}] ClubNotFound sample: {Sample}", rid, string.Join(" || ", repAll.ClubNotFoundSample.Take(10)));

                return Ok(repAll);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Rid}] Admin import exception.", rid);
                return StatusCode(500, ex.Message);
            }
        }

        private static async Task<string> ReadFileHeadAsync(IFormFile file, int maxChars)
        {
            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);

            var buffer = new char[maxChars];
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);

            if (stream.CanSeek) stream.Position = 0;

            return read > 0 ? new string(buffer, 0, read) : string.Empty;
        }
    }
}