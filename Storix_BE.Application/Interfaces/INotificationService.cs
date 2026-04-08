using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    public interface INotificationService
    {
        Task SendNotificationToManagersAsync(int companyId, string title, string message, string type, string category, string referenceType, int? referenceId, int? createdByUserId);
        Task SendNotificationToUserAsync(int userId, string title, string message, string type, string category, string referenceType, int? referenceId, int? createdByUserId);
        Task<List<UserNotification>> GetUserNotificationsAsync(int userId, int skip = 0, int take = 50);
        Task<bool> MarkAsReadAsync(int userNotificationId, int userId);
    }
}
