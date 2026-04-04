using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.Service.Implementation
{
    public class ActivityLogService : IActivityLogService
    {
        private readonly IActivityLogRepository _repo;

        public ActivityLogService(IActivityLogRepository repo)
        {
            _repo = repo;
        }

        public async Task<List<ActivityLogDto>> ListActivityLogsAsync(string? entity = null, int? entityId = null, int? userId = null, DateTime? from = null, DateTime? to = null, int skip = 0, int take = 50)
        {
            var items = await _repo.ListAsync(entity, entityId, userId, from, to, skip, take).ConfigureAwait(false);
            return items.Select(Map).ToList();
        }

        public async Task<ActivityLogDto?> GetActivityLogByIdAsync(int id)
        {
            if (id <= 0) return null;
            var item = await _repo.GetByIdAsync(id).ConfigureAwait(false);
            return item == null ? null : Map(item);
        }

        private static ActivityLogDto Map(ActivityLog a) =>
            new ActivityLogDto(
                a.Id,
                a.UserId,
                a.User?.FullName,
                a.Action,
                a.Entity,
                a.EntityId,
                a.Timestamp);
    }
}