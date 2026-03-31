using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class UserNotification
{
    public int Id { get; set; }

    public int? NotificationId { get; set; }

    public int? UserId { get; set; }

    public bool? IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public bool? IsHidden { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Notification? Notification { get; set; }

    public virtual User? User { get; set; }
}
