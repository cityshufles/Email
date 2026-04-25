using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Email.Services.GmailCollection.Dtos;

namespace Email.Controllers
{
    /// <summary>
    /// Created: 2025-11-04 00:00 UTC
    /// Migrated: 2025-11-21 00:00 UTC - from CityShufflesGuides to Email (verbatim)
    /// v2 Gmail collection API (single app pool) under /v2/gmailcollection.
    /// </summary>
    [ApiController]
    [Route("v2/gmailcollection")]
    public sealed class GmailCollectionV2Controller : ControllerBase
    {
        private readonly ILogger<GmailCollectionV2Controller> _logger;
        private readonly Services.GmailCollection.GmailCollectionV2Service _service;

        public GmailCollectionV2Controller(ILogger<GmailCollectionV2Controller> logger, Services.GmailCollection.GmailCollectionV2Service service)
        {
            _logger = logger;
            _service = service;
        }

        /// <summary>
        /// Start a collection window: day|week|month|daysPrior. Example: /v2/gmailcollection/collect?window=week&limit=1000
        /// </summary>
        [HttpPost("collect")]
        public async Task<ActionResult<CollectionResultDto>> Collect([FromQuery] CollectionRequestDto request, CancellationToken ct)
        {
            var res = await _service.CollectAsync(request, ct);
            if (res.Success) return Ok(res);
            return StatusCode(500, res);
        }

        /// <summary>
        /// Status of inbox rows and current watermark.
        /// </summary>
        [HttpGet("status")]
        public async Task<ActionResult<StatusDto>> Status(CancellationToken ct)
        {
            var res = await _service.GetStatusAsync(ct);
            if (res.Success) return Ok(res);
            return StatusCode(500, res);
        }

        /// <summary>
        /// Get current watermark row for INBOX.
        /// </summary>
        [HttpGet("watermark")]
        public async Task<IActionResult> GetWatermark(CancellationToken ct)
        {
            var wm = await _service.GetWatermarkAsync(ct);
            return Ok(wm);
        }

        /// <summary>
        /// Reset high-watermark (LastSeenUid=0) to rescan from oldest.
        /// </summary>
        [HttpPost("watermark/reset")]
        public async Task<IActionResult> ResetWatermark(CancellationToken ct)
        {
            await _service.ResetWatermarkAsync(ct);
            return Ok(new { Success = true, Message = "Watermark reset" });
        }
    }
}


