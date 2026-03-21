using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class Recommendation
{
    public int Id { get; set; }

    public int? BinId { get; set; }

    public string? Path { get; set; }

    public double? DistanceInfo { get; set; }

    public virtual ShelfLevelBin? Bin { get; set; }

    public virtual ICollection<StorageRecommendation> StorageRecommendations { get; set; } = new List<StorageRecommendation>();
}
