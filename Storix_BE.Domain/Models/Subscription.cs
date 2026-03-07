using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class Subscription
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>TRIAL | PRO_MONTHLY | PRO_YEARLY</summary>
    public string PlanType { get; set; } = null!;

    /// <summary>ACTIVE | EXPIRED | CANCELLED</summary>
    public string Status { get; set; } = null!;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Company Company { get; set; } = null!;

    public virtual ICollection<CompanyPayment> CompanyPayments { get; set; } = new List<CompanyPayment>();
}