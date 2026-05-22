using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Email.Models.Resources;

namespace Email.Services.Resources
{
    public interface IResourceService
    {
        Task<List<ResourceItem>> GetAllResourcesAsync(CancellationToken ct = default);
        Task<List<ResourceItem>> GetResourcesByTourAsync(int tourId, CancellationToken ct = default);
        Task<int> CreateResourceAsync(ResourceItem resource, CancellationToken ct = default);
        Task UpdateResourceToursAsync(int resourceId, IEnumerable<int> tourIds, CancellationToken ct = default);
        Task DeleteResourceAsync(int resourceId, CancellationToken ct = default);
    }
}
