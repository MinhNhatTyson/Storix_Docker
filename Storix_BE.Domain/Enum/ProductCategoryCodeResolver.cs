using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Enum
{
    public static class ProductCategoryCodeResolver
    {
        // Key   = Vietnamese name or common alias (lower-cased at runtime)
        // Value = the canonical code
        private static readonly Dictionary<string, ProductCategoryCode> _map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // ── Passive ───────────────────────────────────────────────────
                ["tụ điện"] = ProductCategoryCode.CAP,
                ["tụ"] = ProductCategoryCode.CAP,
                ["capacitor"] = ProductCategoryCode.CAP,

                ["điện trở"] = ProductCategoryCode.RES,
                ["resistor"] = ProductCategoryCode.RES,

                ["cuộn cảm"] = ProductCategoryCode.IND,
                ["cuộn dây"] = ProductCategoryCode.IND,
                ["inductor"] = ProductCategoryCode.IND,

                ["thạch anh"] = ProductCategoryCode.XTAL,
                ["bộ dao động"] = ProductCategoryCode.XTAL,
                ["crystal"] = ProductCategoryCode.XTAL,
                ["oscillator"] = ProductCategoryCode.XTAL,

                // ── Semiconductors ────────────────────────────────────────────
                ["vi mạch tích hợp"] = ProductCategoryCode.IC,
                ["vi mạch"] = ProductCategoryCode.IC,
                ["ic"] = ProductCategoryCode.IC,
                ["integrated circuit"] = ProductCategoryCode.IC,

                ["transistor"] = ProductCategoryCode.TRN,
                ["bóng bán dẫn"] = ProductCategoryCode.TRN,

                ["diode"] = ProductCategoryCode.DIO,
                ["đi-ốt"] = ProductCategoryCode.DIO,
                ["diốt"] = ProductCategoryCode.DIO,

                ["đèn led"] = ProductCategoryCode.LED,
                ["led"] = ProductCategoryCode.LED,

                ["mosfet"] = ProductCategoryCode.FET,
                ["jfet"] = ProductCategoryCode.FET,
                ["fet"] = ProductCategoryCode.FET,
                ["transistor trường"] = ProductCategoryCode.FET,

                ["thyristor"] = ProductCategoryCode.SCR,
                ["scr"] = ProductCategoryCode.SCR,
                ["triac"] = ProductCategoryCode.SCR,

                ["opto-coupler"] = ProductCategoryCode.OPT,
                ["optocoupler"] = ProductCategoryCode.OPT,
                ["cách ly quang"] = ProductCategoryCode.OPT,
                ["linh kiện quang"] = ProductCategoryCode.OPT,

                // ── Power ─────────────────────────────────────────────────────
                ["bộ điều chỉnh điện áp"] = ProductCategoryCode.VRG,
                ["voltage regulator"] = ProductCategoryCode.VRG,
                ["vrg"] = ProductCategoryCode.VRG,

                ["bộ chuyển đổi dc-dc"] = ProductCategoryCode.DCDC,
                ["dc-dc converter"] = ProductCategoryCode.DCDC,
                ["dcdc"] = ProductCategoryCode.DCDC,

                ["bộ nguồn ac-dc"] = ProductCategoryCode.ACDC,
                ["ac-dc"] = ProductCategoryCode.ACDC,
                ["nguồn"] = ProductCategoryCode.ACDC,
                ["power supply"] = ProductCategoryCode.ACDC,

                ["pin"] = ProductCategoryCode.BATT,
                ["ắc quy"] = ProductCategoryCode.BATT,
                ["battery"] = ProductCategoryCode.BATT,

                // ── Electromechanical ─────────────────────────────────────────
                ["rơ-le"] = ProductCategoryCode.RLY,
                ["rơle"] = ProductCategoryCode.RLY,
                ["relay"] = ProductCategoryCode.RLY,

                ["công tắc"] = ProductCategoryCode.SW,
                ["nút nhấn"] = ProductCategoryCode.SW,
                ["switch"] = ProductCategoryCode.SW,
                ["button"] = ProductCategoryCode.SW,

                ["đầu nối"] = ProductCategoryCode.CONN,
                ["connector"] = ProductCategoryCode.CONN,
                ["cổng kết nối"] = ProductCategoryCode.CONN,

                ["cầu chì"] = ProductCategoryCode.FUSE,
                ["fuse"] = ProductCategoryCode.FUSE,

                // ── Sensors & Modules ─────────────────────────────────────────
                ["cảm biến"] = ProductCategoryCode.SEN,
                ["sensor"] = ProductCategoryCode.SEN,

                ["module"] = ProductCategoryCode.MOD,
                ["mô-đun"] = ProductCategoryCode.MOD,
                ["mô đun"] = ProductCategoryCode.MOD,

                ["anten"] = ProductCategoryCode.ANT,
                ["antenna"] = ProductCategoryCode.ANT,
                ["ăng-ten"] = ProductCategoryCode.ANT,

                // ── Display & Audio ───────────────────────────────────────────
                ["màn hình"] = ProductCategoryCode.DSP,
                ["display"] = ProductCategoryCode.DSP,
                ["lcd"] = ProductCategoryCode.DSP,
                ["oled"] = ProductCategoryCode.DSP,

                ["loa"] = ProductCategoryCode.SPK,
                ["còi"] = ProductCategoryCode.SPK,
                ["buzzer"] = ProductCategoryCode.SPK,
                ["speaker"] = ProductCategoryCode.SPK,

                // ── Mechanical & PCB ──────────────────────────────────────────
                ["bo mạch in"] = ProductCategoryCode.PCB,
                ["pcb"] = ProductCategoryCode.PCB,
                ["printed circuit board"] = ProductCategoryCode.PCB,

                ["tản nhiệt"] = ProductCategoryCode.HSK,
                ["heatsink"] = ProductCategoryCode.HSK,
                ["heat sink"] = ProductCategoryCode.HSK,

                ["vỏ hộp"] = ProductCategoryCode.ENC,
                ["enclosure"] = ProductCategoryCode.ENC,
                ["hộp đựng"] = ProductCategoryCode.ENC,

                // ── Cables & Wire ─────────────────────────────────────────────
                ["dây dẫn"] = ProductCategoryCode.WIRE,
                ["cáp"] = ProductCategoryCode.WIRE,
                ["wire"] = ProductCategoryCode.WIRE,
                ["cable"] = ProductCategoryCode.WIRE,

                // ── Fallback ──────────────────────────────────────────────────
                ["chung"] = ProductCategoryCode.GEN,
                ["khác"] = ProductCategoryCode.GEN,
                ["general"] = ProductCategoryCode.GEN,
                ["other"] = ProductCategoryCode.GEN,
            };

        /// <summary>
        /// Tries to resolve a category code from a Vietnamese or English name.
        /// Returns the matching <see cref="ProductCategoryCode"/> or
        /// <see cref="ProductCategoryCode.GEN"/> if no match is found.
        /// </summary>
        public static ProductCategoryCode Resolve(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return ProductCategoryCode.GEN;

            var key = categoryName.Trim();
            return _map.TryGetValue(key, out var code)
                ? code
                : ProductCategoryCode.GEN;
        }

        /// <summary>
        /// Returns the string representation of the resolved code (e.g. "CAP").
        /// This is the value stored in <c>product_categories.category_code</c>.
        /// </summary>
        public static string ResolveAsString(string categoryName)
            => Resolve(categoryName).ToString();

        /// <summary>
        /// Returns true if the given name maps to a known non-generic code.
        /// </summary>
        public static bool IsKnown(string categoryName)
            => Resolve(categoryName) != ProductCategoryCode.GEN;

        /// <summary>
        /// Exposes the full mapping for display purposes (e.g. a dropdown hint in the UI).
        /// </summary>
        public static IReadOnlyDictionary<string, ProductCategoryCode> AllMappings
            => _map;
    }
}
