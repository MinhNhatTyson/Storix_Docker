using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace Storix_BE.API.Hubs
{
    public class NotificationHub : Hub
    {
        /// <summary>
        /// Client calls Register(userId) after connecting to join its user-group.
        /// This avoids relying on claims mapping (keeps implementation simple).
        /// </summary>
        public Task Register(int userId)
        {
            return Groups.AddToGroupAsync(Context.ConnectionId, GetUserGroup(userId));
        }

        public Task Unregister(int userId)
        {
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, GetUserGroup(userId));
        }

        private static string GetUserGroup(int userId) => $"user-{userId}";
    }
}