using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Storix_BE.Domain.Enum
{
    public enum ProductCategoryCode
    {
        // ── Passive Components ─────────────────────────────────────────────────
        CAP,    // Tụ điện
        RES,    // Điện trở
        IND,    // Cuộn cảm / Cuộn dây
        XTAL,   // Thạch anh / Bộ dao động

        // ── Semiconductors ────────────────────────────────────────────────────
        IC,     // Vi mạch tích hợp
        TRN,    // Transistor
        DIO,    // Diode
        LED,    // Đèn LED
        FET,    // MOSFET / JFET
        SCR,    // Thyristor / SCR
        OPT,    // Opto-coupler / Cách ly quang

        // ── Power Components ──────────────────────────────────────────────────
        VRG,    // Bộ điều chỉnh điện áp
        DCDC,   // Bộ chuyển đổi DC-DC
        ACDC,   // Bộ nguồn AC-DC
        BATT,   // Pin / Ắc quy

        // ── Electromechanical ─────────────────────────────────────────────────
        RLY,    // Rơ-le
        SW,     // Công tắc / Nút nhấn
        CONN,   // Đầu nối / Connector
        FUSE,   // Cầu chì

        // ── Sensors & Modules ─────────────────────────────────────────────────
        SEN,    // Cảm biến
        MOD,    // Module
        ANT,    // Anten

        // ── Display & Audio ───────────────────────────────────────────────────
        DSP,    // Màn hình / Display
        SPK,    // Loa / Còi

        // ── Mechanical & PCB ──────────────────────────────────────────────────
        PCB,    // Bo mạch in
        HSK,    // Tản nhiệt
        ENC,    // Vỏ hộp / Enclosure

        // ── Cables & Wire ─────────────────────────────────────────────────────
        WIRE,   // Dây dẫn / Cáp

        // ── Uncategorised fallback ─────────────────────────────────────────────
        GEN     // Chung / Khác
    }
}
