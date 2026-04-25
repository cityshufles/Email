using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Email.Controllers
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// v2 Gmail processing API (single app pool) under /v2/gmailprocessing.
    /// </summary>
    [ApiController]
    [Route("v2/gmailprocessing")]
    public sealed class GmailProcessingV2Controller : ControllerBase
    {
        private readonly ILogger<GmailProcessingV2Controller> _logger;
        private readonly Email.Services.GmailProcessing.GmailProcessingV2Service _service;

        public GmailProcessingV2Controller(
            ILogger<GmailProcessingV2Controller> logger,
            Email.Services.GmailProcessing.GmailProcessingV2Service service)
        {
            _logger = logger;
            _service = service;
        }

        /// <summary>
        /// Start processing of unprocessed inbox emails.
        /// </summary>
        [HttpPost("process-only")]
        public async Task<ActionResult<ProcessingResultDto>> ProcessOnly(CancellationToken ct)
        {
            var res = await _service.ProcessCollectedAsync(ct);
            if (res.Success) return Ok(res);
            return StatusCode(500, res);
        }

        /// <summary>
        /// Process the specific inbox email ids provided in the request.
        /// </summary>
        [HttpPost("process-specific")]
        public async Task<ActionResult<ProcessingResultDto>> ProcessSpecific([FromBody] ProcessingRequestDto request, CancellationToken ct)
        {
            var ids = request?.InboxEmailIds ?? new List<int>();
            var res = await _service.ProcessSpecificAsync(ids, ct);
            if (res.Success) return Ok(res);
            return StatusCode(500, res);
        }

        /// <summary>
        /// Returns a list of unprocessed inbox emails for the UI grid.
        /// </summary>
        [HttpGet("unprocessed-emails")]
        public async Task<ActionResult<IReadOnlyList<UnprocessedEmailRowDto>>> GetUnprocessed([FromQuery] int limit = 200, CancellationToken ct = default)
        {
            var res = await _service.GetUnprocessedAsync(limit, ct);
            return Ok(res);
        }

        /// <summary>
        /// Returns a processed email display record by id.
        /// </summary>
        [HttpGet("processed-email/{id:int}")]
        public async Task<ActionResult<ProcessedEmailDisplayDto>> GetProcessedById([FromRoute] int id, CancellationToken ct)
        {
            var res = await _service.GetProcessedEmailAsync(id, ct);
            if (res == null) return NotFound();
            return Ok(res);
        }

        /// <summary>
        /// Returns most recent processed booking-related emails.
        /// </summary>
        [HttpGet("recent-processed")]
        public async Task<ActionResult<IReadOnlyList<ProcessedEmailDisplayDto>>> GetRecentProcessed([FromQuery] int limit = 100, CancellationToken ct = default)
        {
            var res = await _service.GetRecentProcessedAsync(limit, ct);
            return Ok(res);
        }

        /// <summary>
        /// Returns recent skipped/errored processed emails.
        /// </summary>
        [HttpGet("recent-skipped")]
        public async Task<ActionResult<IReadOnlyList<ProcessedEmailDisplayDto>>> GetRecentSkipped([FromQuery] int limit = 100, CancellationToken ct = default)
        {
            var res = await _service.GetRecentSkippedAsync(limit, ct);
            return Ok(res);
        }

        /// <summary>
        /// Returns processing status summary.
        /// </summary>
        [HttpGet("status")]
        public async Task<ActionResult<GmailProcessingStatusDto>> Status(CancellationToken ct)
        {
            var res = await _service.GetStatusAsync(ct);
            if (res.Success) return Ok(res);
            return StatusCode(500, res);
        }
    }
}


