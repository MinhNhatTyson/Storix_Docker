using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.DTO
{
    public sealed record UserSummaryDto(
        int Id,
        int? CompanyId,
        string? FullName,
        string? Email,
        string? Phone,
        int? RoleId,
        string? RoleName,
        string? Status,
        DateTime? CreatedAt,
        DateTime? UpdatedAt);

    public sealed record WarehouseSummaryDto(
        int Id,
        int? CompanyId,
        string? Name,
        string? Status);

    public sealed record WarehouseAssignmentResponseDto(
        int Id,
        int? UserId,
        int? WarehouseId,
        string? RoleInWarehouse,
        DateTime? AssignedAt,
        UserSummaryDto? User,
        WarehouseSummaryDto? Warehouse);
}
