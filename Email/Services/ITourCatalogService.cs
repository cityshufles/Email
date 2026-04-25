using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Email.Services
{
    /// <summary>
    /// 2025-12-05 00:00 UTC - Catalog of canonical tours (names, times, defaults).
    /// </summary>
    public interface ITourCatalogService
    {
        Task<TourCatalogSnapshot> BuildCatalogAsync(CancellationToken ct = default);

        string NormalizeName(string rawName);

        string NormalizeTime(string rawTime);
    }
}


