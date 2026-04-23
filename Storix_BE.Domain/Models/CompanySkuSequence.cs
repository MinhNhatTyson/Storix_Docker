namespace Storix_BE.Domain.Models;

public class CompanySkuSequence
{
    public int CompanyId { get; set; }
    public int NextVal { get; set; }

    public virtual Company Company { get; set; } = null!;
}