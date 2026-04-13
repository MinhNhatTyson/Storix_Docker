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
    public class NotificationRepository : INotificationRepository
    {
        private readonly StorixDbContext _context;

        public NotificationRepository(StorixDbContext context)
        {
            _context = context;
        }

        public async Task<Notification> CreateAsync(Notification notification)
        {
            if (notification == null) throw new ArgumentNullException(nameof(notification));
            notification.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return notification;
        }

        public async Task<UserNotification> CreateUserNotificationAsync(UserNotification userNotification)
        {
            if (userNotification == null) throw new ArgumentNullException(nameof(userNotification));
            userNotification.CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            userNotification.IsRead = false;
            userNotification.IsHidden = false;
            _context.UserNotifications.Add(userNotification);
            await _context.SaveChangesAsync().ConfigureAwait(false);
            return userNotification;
        }

        public async Task<List<UserNotification>> GetUserNotificationsAsync(int userId, int skip = 0, int take = 50)
        {
            if (userId <= 0) throw new ArgumentException("Invalid userId.", nameof(userId));
            if (skip < 0) skip = 0;
            if (take <= 0) take = 50;

            return await _context.UserNotifications
                .AsNoTracking()
                .Include(un => un.Notification)
                .Where(un => un.UserId == userId && (un.IsHidden == null || un.IsHidden == false))
                .OrderByDescending(un => un.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<int> MarkUserNotificationAsReadAsync(int userNotificationId, int userId)
        {
            if (userNotificationId <= 0) throw new ArgumentException("Invalid userNotificationId.", nameof(userNotificationId));
            if (userId <= 0) throw new ArgumentException("Invalid userId.", nameof(userId));

            var un = await _context.UserNotifications.FirstOrDefaultAsync(u => u.Id == userNotificationId && u.UserId == userId).ConfigureAwait(false);
            if (un == null) return 0;
            un.IsRead = true;
            un.ReadAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            _context.UserNotifications.Update(un);
            return await _context.SaveChangesAsync().ConfigureAwait(false);
        }
        public async Task<bool> DeleteUserNotificationAsync(int userNotificationId, int userId)
        {
            if (userNotificationId <= 0) throw new ArgumentException("Invalid userNotificationId.", nameof(userNotificationId));
            if (userId <= 0) throw new ArgumentException("Invalid userId.", nameof(userId));

            var un = await _context.UserNotifications
                .FirstOrDefaultAsync(u => u.Id == userNotificationId && u.UserId == userId)
                .ConfigureAwait(false);

            if (un == null) return false;
            un.IsHidden = true;
            _context.UserNotifications.Update(un);
            var changed = await _context.SaveChangesAsync().ConfigureAwait(false);
            return changed > 0;
        }
    }
}
