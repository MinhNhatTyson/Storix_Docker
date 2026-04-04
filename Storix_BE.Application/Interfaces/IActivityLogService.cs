using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public record ActivityLogDto(int Id, int? UserId, string? UserFullName, string? Action, string? Entity, int? EntityId, DateTime? Timestamp);

    public interface IActivityLogService
    {
        Task<List<ActivityLogDto>> ListActivityLogsAsync(
            string? entity = null,
            int? entityId = null,
            int? userId = null,
            DateTime? from = null,
            DateTime? to = null,
            int skip = 0,
            int take = 50);

        Task<ActivityLogDto?> GetActivityLogByIdAsync(int id);
    }
}
