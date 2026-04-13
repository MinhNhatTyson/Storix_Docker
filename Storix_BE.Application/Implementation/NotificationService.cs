using DocumentFormat.OpenXml.Bibliography;
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
        private readonly INotificationPublisher _publisher;

        public NotificationService(
            INotificationRepository notificationRepo,
            IUserRepository userRepo,
            INotificationPublisher publisher)
        {
            _notificationRepo = notificationRepo;
            _userRepo = userRepo;
            _publisher = publisher;
        }
        public async Task SendNotificationToUserAsync(int userId, string title, string message, string type, string category, string referenceType, int? referenceId, int? createdByUserId)
        {
            if (userId <= 0) throw new ArgumentException("Invalid userId.", nameof(userId));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));

            var user = await _userRepo.GetUserByIdWithRoleAsync(userId).ConfigureAwait(false);
            if (user == null) throw new InvalidOperationException($"User {userId} not found.");

            var notification = new Notification
            {
                CompanyId = user.CompanyId,
                Title = title,
                Message = message,
                Type = type,
                Category = category,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                CreatedBy = createdByUserId
            };

            notification = await _notificationRepo.CreateAsync(notification).ConfigureAwait(false);

            var userNotification = new UserNotification
            {
                NotificationId = notification.Id,
                UserId = userId,
                IsRead = false,
                IsHidden = false,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            };

            await _notificationRepo.CreateUserNotificationAsync(userNotification).ConfigureAwait(false);

            var payload = new
            {
                UserNotificationId = userNotification.Id,
                NotificationId = notification.Id,
                notification.Title,
                notification.Message,
                notification.Type,
                notification.Category,
                notification.ReferenceType,
                notification.ReferenceId,
                CreatedAt = notification.CreatedAt
            };

            try
            {
                await _publisher.PublishToUserAsync(userId, payload).ConfigureAwait(false);
            }
            catch
            {
                Console.WriteLine($"Failed to publish notification to user {userId}. NotificationId: {notification.Id}");
            }
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
        public async Task<bool> DeleteUserNotificationAsync(int userNotificationId, int userId)
        {
            var deleted = await _notificationRepo.DeleteUserNotificationAsync(userNotificationId, userId).ConfigureAwait(false);
            if (!deleted) return false;

            // Notify client(s) that the notification was deleted so UI can update in real time
            try
            {
                await _publisher.PublishToUserAsync(userId, new { Action = "Deleted", UserNotificationId = userNotificationId }).ConfigureAwait(false);
            }
            catch
            {
                throw new InvalidOperationException($"Failed to publish deletion of notification {userNotificationId} for user {userId}.");
            }

            return true;
        }
    }
}
