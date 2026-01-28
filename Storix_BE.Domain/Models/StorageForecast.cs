using System;
using System.Collections.Generic;

namespace Storix_BE.Domain.Models;

public partial class StorageForecast
{
    public int Id { get; set; }

    public int? ProductId { get; set; }

    public int? WarehouseId { get; set; }

    public string? TypeOfForecast { get; set; }

    public int? PredictedStock { get; set; }

    public int? DaysToStockout { get; set; }

    public string? RiskLevel { get; set; }

    public double? Confidence { get; set; }

    public string? RecommendedAction { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Product? Product { get; set; }

    public virtual Warehouse? Warehouse { get; set; }
}
