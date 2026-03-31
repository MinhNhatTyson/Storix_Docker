using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Implementation
{
    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _notificationRepo;
        private readonly IUserRepository _userRepo;

        public NotificationService(INotificationRepository notificationRepo, IUserRepository userRepo)
        {
            _notificationRepo = notificationRepo;
            _userRepo = userRepo;
        }

        public async Task SendNotificationToManagersAsync(int companyId, string title, string message, string type, string category, string referenceType, int? referenceId, int? createdByUserId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));

            // Create notification record
            var notification = new Notification
            {
                CompanyId = companyId,
                Title = title,
                Message = message,
                Type = type,
                Category = category,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                CreatedBy = createdByUserId
            };

            notification = await _notificationRepo.CreateAsync(notification).ConfigureAwait(false);

            // Resolve managers in the company (Role name "Manager")
            var users = await _userRepo.GetUsersByCompanyIdAsync(companyId).ConfigureAwait(false);
            var managers = users.Where(u => u.Role != null && string.Equals(u.Role.Name, "Manager", StringComparison.OrdinalIgnoreCase)).ToList();

            // Create UserNotification for each manager
            foreach (var mgr in managers)
            {
                var un = new UserNotification
                {
                    NotificationId = notification.Id,
                    UserId = mgr.Id,
                    IsRead = false,
                    IsHidden = false,
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };
                await _notificationRepo.CreateUserNotificationAsync(un).ConfigureAwait(false);
            }
        }

        public async Task<List<UserNotification>> GetUserNotificationsAsync(int userId, int skip = 0, int take = 50)
        {
            return await _notificationRepo.GetUserNotificationsAsync(userId, skip, take).ConfigureAwait(false);
        }

        public async Task<bool> MarkAsReadAsync(int userNotificationId, int userId)
        {
            var affected = await _notificationRepo.MarkUserNotificationAsReadAsync(userNotificationId, userId).ConfigureAwait(false);
            return affected > 0;
        }
    }
}
