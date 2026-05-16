using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storix_BE.Service.Interfaces
{
    // ── Request / Response DTOs ──────────────────────────────────────────────

    public sealed record StartBarcodeSessionRequest(int StaffId);

    public sealed record ScanBarcodeRequest(string Sku);

    public sealed record FinalizeBarcodeSessionRequest(
        /// <summary>
        /// Quality-check overrides supplied by the staff for mismatched items.
        /// If a product was fully scanned without issues, staff does NOT need to include it here.
        /// </summary>
        IEnumerable<BarcodeQcOverride> QcOverrides);

    /// <summary>
    /// Allows staff to supply the "real" received / passed quantities for one product
    /// when the scanned count does not match expectations (over-scan, damage, etc.).
    /// </summary>
    public sealed record BarcodeQcOverride(
        int ProductId,
        /// <summary>Actual physical units received (may differ from scanned).</summary>
        int ReceivedQuantity,
        /// <summary>Units that passed visual inspection.</summary>
        int PassedQuantity,
        string? FailureReason,
        string? Notes);

    // ── Scan line DTO (read) ─────────────────────────────────────────────────

    public sealed record BarcodeScanLineDto(
        int ProductId,
        string? ProductName,
        string? Sku,
        int ExpectedQuantity,
        int ScannedQuantity,
        bool IsComplete,
        bool IsOverScanned);

    // ── Session DTO (read) ───────────────────────────────────────────────────

    public sealed record BarcodeScanSessionDto(
        Guid SessionId,
        int InboundOrderId,
        int StaffId,
        DateTime StartedAt,
        bool IsFinalized,
        DateTime? FinalizedAt,
        IReadOnlyList<BarcodeScanLineDto> Lines,
        /// <summary>True when every line IsComplete (none under-scanned, ignores over-scans).</summary>
        bool AllComplete);

    // ── Scan result DTO ──────────────────────────────────────────────────────

    public sealed record ScanResultDto(
        bool Success,
        string? WarningMessage,
        BarcodeScanLineDto UpdatedLine,
        BarcodeScanSessionDto Session);

    // ── Service interface ────────────────────────────────────────────────────

    public interface IBarcodeScanService
    {
        /// <summary>
        /// Opens a new barcode scan session for <paramref name="inboundOrderId"/>.
        /// Throws if order not found, not in the correct status, or a session already exists.
        /// </summary>
        Task<BarcodeScanSessionDto> StartSessionAsync(int companyId, int inboundOrderId,
            StartBarcodeSessionRequest request);

        /// <summary>
        /// Retrieves the current session state.
        /// Returns null if no session is active for <paramref name="inboundOrderId"/>.
        /// </summary>
        Task<BarcodeScanSessionDto?> GetSessionAsync(int companyId, int inboundOrderId);

        /// <summary>
        /// Records one unit scan for the product identified by <paramref name="request.Sku"/>.
        /// Returns a warning (not an exception) for over-scanning.
        /// Throws if SKU not found in the order.
        /// </summary>
        Task<ScanResultDto> ScanAsync(int companyId, int inboundOrderId, ScanBarcodeRequest request);

        /// <summary>
        /// Finalizes the session: persists QC records (replacing SubmitQualityCheckAsync),
        /// transitions the order to QUALITY_CHECK status, and removes the session.
        /// </summary>
        Task<InboundQualityCheckResultDto> FinalizeSessionAsync(int companyId, int inboundOrderId,
            FinalizeBarcodeSessionRequest request);

        /// <summary>
        /// Discards an active session without persisting anything.
        /// </summary>
        Task DiscardSessionAsync(int companyId, int inboundOrderId);
    }
}