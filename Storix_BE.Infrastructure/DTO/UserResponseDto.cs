using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.DTO
{
    public sealed record UserResponseDto(
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
}
