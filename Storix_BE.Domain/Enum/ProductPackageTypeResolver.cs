using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Enum
{
    public static class ProductPackageTypeResolver
    {
        // Key = normalised form (lowercase, no spaces)
        private static readonly Dictionary<string, ProductPackageTypeCode> _map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // ── Tape & Reel ────────────────────────────────────────────────
                ["băngcuốn"] = ProductPackageTypeCode.TR,
                ["cuộnbăng"] = ProductPackageTypeCode.TR,
                ["tapeandreel"] = ProductPackageTypeCode.TR,
                ["tape&reel"] = ProductPackageTypeCode.TR,
                ["tapeandreel"] = ProductPackageTypeCode.TR,
                ["tr"] = ProductPackageTypeCode.TR,

                // ── Box ───────────────────────────────────────────────────────
                ["hộp"] = ProductPackageTypeCode.BOX,
                ["box"] = ProductPackageTypeCode.BOX,

                // ── Tray ──────────────────────────────────────────────────────
                ["khaynhựa"] = ProductPackageTypeCode.TRAY,
                ["khay"] = ProductPackageTypeCode.TRAY,
                ["tray"] = ProductPackageTypeCode.TRAY,

                // ── Bulk / Loose ──────────────────────────────────────────────
                ["đónggóirời"] = ProductPackageTypeCode.BULK,
                ["rời"] = ProductPackageTypeCode.BULK,
                ["bulk"] = ProductPackageTypeCode.BULK,
                ["loose"] = ProductPackageTypeCode.BULK,

                // ── Tube / Stick ──────────────────────────────────────────────
                ["ống"] = ProductPackageTypeCode.TUBE,
                ["tube"] = ProductPackageTypeCode.TUBE,
                ["stick"] = ProductPackageTypeCode.TUBE,

                // ── Bag ───────────────────────────────────────────────────────
                ["túi"] = ProductPackageTypeCode.BAG,
                ["bag"] = ProductPackageTypeCode.BAG,

                // ── Blister ───────────────────────────────────────────────────
                ["vỉnhựa"] = ProductPackageTypeCode.BLST,
                ["vỉ"] = ProductPackageTypeCode.BLST,
                ["blister"] = ProductPackageTypeCode.BLST,
                ["blisterpack"] = ProductPackageTypeCode.BLST,
                ["blst"] = ProductPackageTypeCode.BLST,

                // ── Spool ─────────────────────────────────────────────────────
                ["cuộnchỉ"] = ProductPackageTypeCode.SPOOL,
                ["cuộn"] = ProductPackageTypeCode.SPOOL,
                ["spool"] = ProductPackageTypeCode.SPOOL,

                // ── Bar / Strip ───────────────────────────────────────────────
                ["thanh"] = ProductPackageTypeCode.BAR,
                ["thanhdài"] = ProductPackageTypeCode.BAR,
                ["bar"] = ProductPackageTypeCode.BAR,
                ["strip"] = ProductPackageTypeCode.BAR,
            };

        /// <summary>
        /// Normalises the input: lowercase + strip all whitespace,
        /// then looks up in the map.
        /// Returns <see cref="ProductPackageTypeCode.PKG"/> if not found.
        /// </summary>
        public static ProductPackageTypeCode Resolve(string? packageType)
        {
            if (string.IsNullOrWhiteSpace(packageType))
                return ProductPackageTypeCode.PKG;

            // Normalise: lowercase + remove all whitespace characters
            var normalised = Regex.Replace(packageType.Trim().ToLowerInvariant(), @"\s+", "");

            return _map.TryGetValue(normalised, out var code)
                ? code
                : ProductPackageTypeCode.PKG;
        }

        /// <summary>
        /// Returns the string representation of the resolved code (e.g. "TR", "BOX").
        /// This is the value embedded in the SKU.
        /// </summary>
        public static string ResolveAsString(string? packageType)
            => Resolve(packageType).ToString();

        /// <summary>
        /// Returns true if the input matched a known non-generic package type.
        /// </summary>
        public static bool IsKnown(string? packageType)
            => Resolve(packageType) != ProductPackageTypeCode.PKG;

        /// <summary>
        /// Exposes the full mapping for reference (e.g. tooltip hints in the UI).
        /// </summary>
        public static IReadOnlyDictionary<string, ProductPackageTypeCode> AllMappings
            => _map;
    }
}
