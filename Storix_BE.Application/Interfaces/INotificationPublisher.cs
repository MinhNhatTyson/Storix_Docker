using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    /// <summary>
    /// Abstraction to publish notifications in real-time.
    /// Implemented in API project (SignalR) so Service layer stays independent.
    /// </summary>
    public interface INotificationPublisher
    {
        Task PublishToUserAsync(int userId, object payload);
        Task PublishToGroupAsync(string groupName, object payload);
    }
}
