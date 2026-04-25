using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Services.GmailProcessing.Models;

namespace Email.Services.GmailProcessing.Repositories
{
    /// <summary>
    /// Created: 2025-11-07 00:00 UTC
    /// Repository contract for loading email classification rules.
    /// </summary>
    public interface IClassificationRepository
    {
        Task<IReadOnlyList<ClassificationRuleRecord>> LoadActiveClassificationRulesAsync(CancellationToken cancellationToken);
    }
}


