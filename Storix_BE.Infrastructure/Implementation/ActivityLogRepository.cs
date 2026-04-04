using Microsoft.EntityFrameworkCore;
using Storix_BE.Domain.Context;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Implementation
{
    public class ActivityLogRepository : IActivityLogRepository
    {
        private readonly StorixDbContext _context;

        public ActivityLogRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<List<ActivityLog>> ListAsync(string? entity = null, int? entityId = null, int? userId = null, DateTime? from = null, DateTime? to = null, int skip = 0, int take = 50)
        {
            if (skip < 0) skip = 0;
            if (take <= 0) take = 50;
            if (take > 500) take = 500;

            var query = _context.ActivityLogs
                .AsNoTracking()
                .Include(a => a.User)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(entity))
                query = query.Where(a => a.Entity != null && a.Entity.ToUpper() == entity.Trim().ToUpper());

            if (entityId.HasValue)
                query = query.Where(a => a.EntityId == entityId.Value);

            if (userId.HasValue)
                query = query.Where(a => a.UserId == userId.Value);

            if (from.HasValue)
                query = query.Where(a => a.Timestamp != null && a.Timestamp >= from.Value);

            if (to.HasValue)
                query = query.Where(a => a.Timestamp != null && a.Timestamp <= to.Value);

            return await query
                .OrderByDescending(a => a.Timestamp)
                .Skip(skip)
                .Take(take)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<ActivityLog?> GetByIdAsync(int id)
        {
            return await _context.ActivityLogs.AsNoTracking()
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.Id == id)
                .ConfigureAwait(false);
        }

        public async Task<int> AddAsync(ActivityLog entry)
        {
            _context.ActivityLogs.Add(entry);
            return await _context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
