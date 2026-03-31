using Storix_BE.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Repository.Interfaces
{
    public interface INotificationRepository
    {
        Task<Notification> CreateAsync(Notification notification);
        Task<UserNotification> CreateUserNotificationAsync(UserNotification userNotification);
        Task<List<UserNotification>> GetUserNotificationsAsync(int userId, int skip = 0, int take = 50);
        Task<int> MarkUserNotificationAsReadAsync(int userNotificationId, int userId);
    }
}
