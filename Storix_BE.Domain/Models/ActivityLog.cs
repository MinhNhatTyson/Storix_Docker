using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class ActivityLog
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public string? Action { get; set; }

    public string? Entity { get; set; }

    public int? EntityId { get; set; }

    public DateTime? Timestamp { get; set; }

    public virtual User? User { get; set; }
}
