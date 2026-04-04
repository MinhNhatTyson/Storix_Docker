using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface IActivityLogRepository
    {
        Task<List<ActivityLog>> ListAsync(
            string? entity = null,
            int? entityId = null,
            int? userId = null,
            DateTime? from = null,
            DateTime? to = null,
            int skip = 0,
            int take = 50);

        Task<ActivityLog?> GetByIdAsync(int id);

        Task<int> AddAsync(ActivityLog entry);
    }
}
