using Microsoft.AspNetCore.SignalR;
using Storix_BE.API.Hubs;
using Storix_BE.Service.Interfaces;
using System.Threading.Tasks;

namespace Storix_BE.API.RealTime
{
    public class SignalRNotificationPublisher : INotificationPublisher
    {
        private readonly IHubContext<NotificationHub> _hub;

        public SignalRNotificationPublisher(IHubContext<NotificationHub> hub)
        {
            _hub = hub;
        }

        public Task PublishToUserAsync(int userId, object payload)
        {
            return _hub.Clients.Group(GetUserGroup(userId)).SendAsync("ReceiveNotification", payload);
        }

        public Task PublishToGroupAsync(string groupName, object payload)
        {
            return _hub.Clients.Group(groupName).SendAsync("ReceiveNotification", payload);
        }

        private static string GetUserGroup(int userId) => $"user-{userId}";
    }
}