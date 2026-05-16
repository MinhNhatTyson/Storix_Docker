using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Storix_BE.Domain.Models;
using Storix_BE.Repository.Interfaces;
using Storix_BE.Service.Barcode;
using Storix_BE.Service.Interfaces;

namespace Storix_BE.Service.Implementation
{
    public sealed class BarcodeScanService : IBarcodeScanService
    {
        // Statuses that allow a barcode scan session to be opened
        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Waiting for payment",
            "WAITING_RECEIPT"
        };

        private readonly BarcodeSessionStore _store;
        private readonly IInventoryInboundRepository _inboundRepo;

        public BarcodeScanService(BarcodeSessionStore store, IInventoryInboundRepository inboundRepo)
        {
            _store = store;
            _inboundRepo = inboundRepo;
        }

        // ── Start ─────────────────────────────────────────────────────────────

        public async Task<BarcodeScanSessionDto> StartSessionAsync(
            int companyId, int inboundOrderId, StartBarcodeSessionRequest request)
        {
            ValidateIds(companyId, inboundOrderId);
            if (request.StaffId <= 0)
                throw new ArgumentException("Invalid staffId.", nameof(request));

            // Load order — throws InvalidOperationException if not found / out of scope
            var orderDto = await _inboundRepo
                .GetInboundOrderByIdAsync(companyId, inboundOrderId)
                .ConfigureAwait(false);

            if (!AllowedStatuses.Contains(orderDto.Status ?? string.Empty))
                throw new InvalidOperationException(
                    $"A barcode scan session can only be started when the order is in " +
                    $"'{string.Join("' or '", AllowedStatuses)}' status. " +
                    $"Current status: '{orderDto.Status}'.");

            // Prevent duplicate active sessions
            if (_store.Get(inboundOrderId) is { IsFinalized: false })
                throw new InvalidOperationException(
                    $"An active barcode scan session already exists for order {inboundOrderId}. " +
                    "Discard it before starting a new one.");

            // Build expected scan lines from order items
            var lines = orderDto.InboundOrderItems
                .Where(i => i.ProductId.HasValue && (i.ExpectedQuantity ?? 0) > 0)
                .Select(i => new BarcodeScanLine
                {
                    ProductId = i.ProductId!.Value,
                    ProductName = i.Product.Name,
                    Sku = i.Product.Sku,
                    ExpectedQuantity = i.ExpectedQuantity!.Value,
                    ScannedQuantity = 0
                });

            var session = _store.Create(inboundOrderId, request.StaffId, lines);
            return MapSession(session);
        }

        // ── Get ───────────────────────────────────────────────────────────────

        public async Task<BarcodeScanSessionDto?> GetSessionAsync(int companyId, int inboundOrderId)
        {
            ValidateIds(companyId, inboundOrderId);

            // Validate order scope (throws if not found / wrong company)
            await _inboundRepo.GetInboundOrderByIdAsync(companyId, inboundOrderId)
                .ConfigureAwait(false);

            var session = _store.Get(inboundOrderId);
            return session is null ? null : MapSession(session);
        }

        // ── Scan ──────────────────────────────────────────────────────────────

        public async Task<ScanResultDto> ScanAsync(
            int companyId, int inboundOrderId, ScanBarcodeRequest request)
        {
            ValidateIds(companyId, inboundOrderId);
            if (string.IsNullOrWhiteSpace(request.Sku))
                throw new ArgumentException("SKU is required.", nameof(request));

            var session = RequireActiveSession(inboundOrderId);

            // Match by SKU against the session lines (which were seeded from the order)
            var matchedLine = session.Lines.Values
                .FirstOrDefault(l => string.Equals(
                    l.Sku, request.Sku.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matchedLine is null)
                throw new InvalidOperationException(
                    $"SKU '{request.Sku}' was not found in InboundOrder {inboundOrderId}. " +
                    "Please verify the product or use the quality-check menu to record a mismatch.");

            // One scan = one unit
            matchedLine.ScannedQuantity += 1;

            string? warning = null;
            if (matchedLine.IsOverScanned)
            {
                warning =
                    $"Over-scan detected for SKU '{matchedLine.Sku}': " +
                    $"expected {matchedLine.ExpectedQuantity}, " +
                    $"now scanned {matchedLine.ScannedQuantity}. " +
                    "You can adjust the quantity in the quality-check menu before finalizing.";
            }

            await Task.CompletedTask; // keeps method truly async for interface consistency

            return new ScanResultDto(
                Success: true,
                WarningMessage: warning,
                UpdatedLine: MapLine(matchedLine),
                Session: MapSession(session));
        }

        // ── Finalize ──────────────────────────────────────────────────────────

        public async Task<InboundQualityCheckResultDto> FinalizeSessionAsync(
            int companyId, int inboundOrderId, FinalizeBarcodeSessionRequest request)
        {
            ValidateIds(companyId, inboundOrderId);

            var session = RequireActiveSession(inboundOrderId);

            // Load the DTO once — needed to resolve InboundOrderItem IDs
            var orderDto = await _inboundRepo
                .GetInboundOrderByIdAsync(companyId, inboundOrderId)
                .ConfigureAwait(false);

            // Build lookup: productId → inboundOrderItemId
            var itemIdByProduct = orderDto.InboundOrderItems
                .Where(i => i.ProductId.HasValue)
                .ToDictionary(i => i.ProductId!.Value, i => i.Id);

            // Staff-supplied overrides (mismatch / damage corrections)
            var overrideByProduct = (request.QcOverrides ?? Enumerable.Empty<BarcodeQcOverride>())
                .ToDictionary(o => o.ProductId);

            var qcItems = new List<QualityCheckSaveDto>();

            foreach (var line in session.Lines.Values)
            {
                if (!itemIdByProduct.TryGetValue(line.ProductId, out var inboundOrderItemId))
                    continue; // defensive — should never happen

                if (overrideByProduct.TryGetValue(line.ProductId, out var ov))
                {
                    // Use staff-supplied explicit QC data
                    var failed = Math.Max(0, ov.ReceivedQuantity - ov.PassedQuantity);
                    qcItems.Add(new QualityCheckSaveDto(
                        InboundOrderItemId: inboundOrderItemId,
                        ProductId: line.ProductId,
                        ReceivedQuantity: ov.ReceivedQuantity,
                        PassedQuantity: ov.PassedQuantity,
                        FailedQuantity: failed,
                        FailureReason: ov.FailureReason,
                        Notes: ov.Notes));
                }
                else
                {
                    // Derive from scan count:
                    //   received = scannedQuantity
                    //   passed   = min(scanned, expected)  → over-scanned units counted as failed
                    var received = line.ScannedQuantity;
                    var passed = Math.Min(received, line.ExpectedQuantity);
                    var failed = received - passed;

                    qcItems.Add(new QualityCheckSaveDto(
                        InboundOrderItemId: inboundOrderItemId,
                        ProductId: line.ProductId,
                        ReceivedQuantity: received,
                        PassedQuantity: passed,
                        FailedQuantity: failed,
                        FailureReason: failed > 0
                            ? $"Over-scan: {failed} unit(s) exceed expected quantity."
                            : null,
                        Notes: null));
                }
            }

            if (!qcItems.Any())
                throw new InvalidOperationException(
                    "Cannot finalize: no scan lines found in session. " +
                    "Scan at least one product before finalizing.");

            // Persist QC records — identical DB path to legacy SubmitQualityCheckAsync
            var savedOrder = await _inboundRepo
                .SaveQualityCheckAsync(inboundOrderId, qcItems, session.StaffId)
                .ConfigureAwait(false);

            var qcRecords = await _inboundRepo
                .GetQualityChecksByOrderIdAsync(inboundOrderId)
                .ConfigureAwait(false);

            // Mark finalized and remove from store
            session.Finalize();
            _store.Remove(inboundOrderId);

            // Map to same DTO shape as legacy GET quality-check endpoint
            var itemDtos = qcRecords.Select(q => new QualityCheckItemDto(
                QualityCheckId: q.Id,
                InboundOrderItemId: q.InboundOrderItemId,
                ProductId: q.ProductId,
                ProductName: q.InboundOrderItem?.Product?.Name,
                ProductSku: q.InboundOrderItem?.Product?.Sku,
                ReceivedQuantity: q.ReceivedQuantity,
                PassedQuantity: q.PassedQuantity,
                FailedQuantity: q.FailedQuantity,
                FailureReason: q.FailureReason,
                Notes: q.Notes,
                InspectedBy: q.InspectedBy,
                InspectedAt: q.InspectedAt)).ToList();

            return new InboundQualityCheckResultDto(
                InboundOrderId: savedOrder.Id,
                OrderStatus: savedOrder.Status ?? string.Empty,
                Items: itemDtos);
        }

        // ── Discard ───────────────────────────────────────────────────────────

        public async Task DiscardSessionAsync(int companyId, int inboundOrderId)
        {
            ValidateIds(companyId, inboundOrderId);

            // Validate order scope
            await _inboundRepo.GetInboundOrderByIdAsync(companyId, inboundOrderId)
                .ConfigureAwait(false);

            if (!_store.Remove(inboundOrderId))
                throw new InvalidOperationException(
                    $"No active barcode scan session found for order {inboundOrderId}.");
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private BarcodeScanSession RequireActiveSession(int inboundOrderId)
        {
            var session = _store.Get(inboundOrderId);
            if (session is null)
                throw new InvalidOperationException(
                    $"No active barcode scan session found for order {inboundOrderId}. " +
                    "Start a session first.");
            if (session.IsFinalized)
                throw new InvalidOperationException(
                    $"The barcode scan session for order {inboundOrderId} has already been finalized.");
            return session;
        }

        private static void ValidateIds(int companyId, int inboundOrderId)
        {
            if (companyId <= 0) throw new ArgumentException("Invalid companyId.", nameof(companyId));
            if (inboundOrderId <= 0) throw new ArgumentException("Invalid inboundOrderId.", nameof(inboundOrderId));
        }

        // ── Mapping ───────────────────────────────────────────────────────────

        private static BarcodeScanLineDto MapLine(BarcodeScanLine l) =>
            new(l.ProductId, l.ProductName, l.Sku,
                l.ExpectedQuantity, l.ScannedQuantity,
                l.IsComplete, l.IsOverScanned);

        private static BarcodeScanSessionDto MapSession(BarcodeScanSession s)
        {
            var lines = s.Lines.Values.Select(MapLine).ToList();
            return new BarcodeScanSessionDto(
                SessionId: s.SessionId,
                InboundOrderId: s.InboundOrderId,
                StaffId: s.StaffId,
                StartedAt: s.StartedAt,
                IsFinalized: s.IsFinalized,
                FinalizedAt: s.FinalizedAt,
                Lines: lines,
                AllComplete: lines.Count > 0 && lines.All(l => l.IsComplete));
        }
    }
}