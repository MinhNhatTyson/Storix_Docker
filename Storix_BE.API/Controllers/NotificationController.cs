using Microsoft.AspNetCore.Mvc;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _service;

        public NotificationController(INotificationService service)
        {
            _service = service;
        }

        [HttpGet("get-notifications/{userId:int}")]
        public async Task<IActionResult> GetUserNotifications(int userId, [FromQuery] int skip = 0, [FromQuery] int take = 50)
        {
            if (userId <= 0) return BadRequest(new { message = "Invalid user id." });
            var items = await _service.GetUserNotificationsAsync(userId, skip, take).ConfigureAwait(false);
            return Ok(items);
        }

        [HttpPut("mark-as-read/{userId:int}/{userNotificationId:int}")]
        public async Task<IActionResult> MarkAsRead(int userId, int userNotificationId)
        {
            if (userId <= 0 || userNotificationId <= 0) return BadRequest(new { message = "Invalid parameters." });
            var ok = await _service.MarkAsReadAsync(userNotificationId, userId).ConfigureAwait(false);
            if (!ok) return NotFound(new { message = "Notification not found or not owned by user." });
            return Ok(new { message = "Marked as read." });
        }
    }
}
