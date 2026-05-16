using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Storix_BE.Service.Barcode
{
    /// <summary>
    /// Represents one scan line (one product) within an active barcode session.
    /// </summary>
    public sealed class BarcodeScanLine
    {
        public int ProductId { get; init; }
        public string? ProductName { get; init; }
        public string? Sku { get; init; }
        public int ExpectedQuantity { get; init; }

        /// <summary>Incremented by 1 for every successful scan of this SKU.</summary>
        public int ScannedQuantity { get; set; }

        /// <summary>True when ScannedQuantity > ExpectedQuantity.</summary>
        public bool IsOverScanned => ScannedQuantity > ExpectedQuantity;

        /// <summary>True when ScannedQuantity == ExpectedQuantity.</summary>
        public bool IsComplete => ScannedQuantity == ExpectedQuantity;
    }

    /// <summary>
    /// One barcode scan session, scoped to a single InboundOrder.
    /// Lives entirely in memory — discarded on server restart.
    /// </summary>
    public sealed class BarcodeScanSession
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public int InboundOrderId { get; init; }
        public int StaffId { get; init; }
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public bool IsFinalized { get; private set; }
        public DateTime? FinalizedAt { get; private set; }

        /// <summary>Keyed by ProductId.</summary>
        public Dictionary<int, BarcodeScanLine> Lines { get; } = new();

        public void Finalize()
        {
            IsFinalized = true;
            FinalizedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Thread-safe singleton store for all active barcode sessions.
    /// One session per InboundOrder at any time.
    /// </summary>
    public sealed class BarcodeSessionStore
    {
        // Key = InboundOrderId
        private readonly ConcurrentDictionary<int, BarcodeScanSession> _sessions = new();

        public BarcodeScanSession? Get(int inboundOrderId)
            => _sessions.TryGetValue(inboundOrderId, out var s) ? s : null;

        public BarcodeScanSession Create(int inboundOrderId, int staffId,
            IEnumerable<BarcodeScanLine> expectedLines)
        {
            var session = new BarcodeScanSession
            {
                InboundOrderId = inboundOrderId,
                StaffId = staffId
            };

            foreach (var line in expectedLines)
                session.Lines[line.ProductId] = line;

            // Replace any existing session for this order
            _sessions[inboundOrderId] = session;
            return session;
        }

        public bool Remove(int inboundOrderId)
            => _sessions.TryRemove(inboundOrderId, out _);
    }
}