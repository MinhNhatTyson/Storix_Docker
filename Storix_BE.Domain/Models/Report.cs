using System;

namespace Storix_BE.Domain.Models;

public partial class Report
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int CreatedByUserId { get; set; }

    public string? ReportType { get; set; }

    public int? WarehouseId { get; set; }

    public int? ProductId { get; set; }

    public int? InventoryCountTicketId { get; set; }

    public DateTime? TimeFrom { get; set; }

    public DateTime? TimeTo { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ParametersJson { get; set; }

    public string? SummaryJson { get; set; }

    public string? DataJson { get; set; }

    public string? SchemaVersion { get; set; }

    public string? PdfUrl { get; set; }

    public string? PdfFileName { get; set; }

    public string? PdfContentHash { get; set; }

    public DateTime? PdfGeneratedAt { get; set; }
}
