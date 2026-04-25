using Email.Models.Staff;

namespace Email.Services.Staff
{
    public interface IStaffService
    {
        Task<List<StaffMember>> GetAllStaffAsync(CancellationToken ct = default);
        Task<StaffMember?> GetStaffMemberAsync(int? userId, int? guideId, CancellationToken ct = default);
        Task SaveStaffMemberAsync(StaffMember staff, CancellationToken ct = default);
        Task DeleteStaffMemberAsync(int? userId, int? guideId, CancellationToken ct = default);
    }
}
