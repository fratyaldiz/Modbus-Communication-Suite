using System;
using System.Collections.Generic;

namespace Modbus.Communication.Server
{
    /// <summary>
    /// LiBat register haritasındaki tek bir Holding Register tanımı.
    ///
    /// LogicalAddress : Dokümanda kullanıcıya gösterilen 4xxxx adresi (örn. 40111)
    /// ProtocolAddress: Dokümandaki "Register" sütunu / Modbus isteğinde kullanılacak adres (örn. 111)
    /// Sample         : STM32/BMS simülasyonunda döndürülecek örnek ham 16-bit değer
    /// </summary>
    public sealed record BmsRegister(
        int ProtocolAddress,
        int LogicalAddress,
        string Name,
        string Type,
        string Unit,
        string Description,
        ushort Sample);

    /// <summary>
    /// LiBat™ BMS Modbus register haritası ve Battery Status bit çözümleyicisi.
    ///
    /// Kaynak:
    /// https://wiki.li-bat.com/comm/modbus/register-map/
    /// https://wiki.li-bat.com/comm/modbus/data-format/
    ///
    /// ADRESLEME NOTU
    /// ----------------
    /// LiBat tablosunda örneğin:
    ///   Address  = 40111
    ///   Register = 111
    /// olarak verilir.
    /// Bu nedenle bu profile özel dönüşüm:
    ///   Protocol/Register address = Logical PLC address - 40000
    /// şeklindedir.
    ///
    /// Örnek:
    ///   Kullanıcı Address alanına 40111 yazar.
    ///   Program Modbus isteğinde 111 (0x006F) kullanır.
    ///
    /// Gerçek kart geldiğinde ilk donanım testinde bu üretici adresleme davranışı
    /// mutlaka gerçek RTU paketi üzerinden de doğrulanmalıdır.
    /// </summary>
    public static class LiBatBmsProfile
    {
        public const int LogicalBase = 40000;
        public const int FirstLogicalAddress = 40088;
        public const int LastLogicalAddress = 40154;

        // ============================================================
        // Holding Register haritası (Table 2.3.1) — örnek değerlerle
        // ============================================================
        public static IReadOnlyList<BmsRegister> Registers { get; } = new List<BmsRegister>
        {
            // --- Kimlik / sürüm ---
            new(88,  40088, "Software Version Major", "uint16", "-", "Firmware version - Major", 1),
            new(89,  40089, "Software Version Minor", "uint16", "-", "Firmware version - Minor", 2),
            new(90,  40090, "Software Version Patch", "uint16", "-", "Firmware version - Patch", 3),

            new(91,  40091, "Hardware Version Major", "uint16", "-", "Hardware version - Major", 1),
            new(92,  40092, "Hardware Version Minor", "uint16", "-", "Hardware version - Minor", 0),
            new(93,  40093, "Hardware Version Patch", "uint16", "-", "Hardware version - Patch", 0),

            new(94,  40094, "Serial Number 1", "uint16", "-", "Device serial number word 1", 0x1234),
            new(95,  40095, "Serial Number 2", "uint16", "-", "Device serial number word 2", 0x5678),
            new(96,  40096, "Serial Number 3", "uint16", "-", "Device serial number word 3", 0x9ABC),
            new(97,  40097, "Serial Number 4", "uint16", "-", "Device serial number word 4", 0xDEF0),

            // Claude'un ilk sürümünde eksik olan Bootloader Version registerları.
            new(98,  40098, "Bootloader Version 1", "uint16", "-", "Bootloader version word 1", 1),
            new(99,  40099, "Bootloader Version 2", "uint16", "-", "Bootloader version word 2", 0),
            new(100, 40100, "Bootloader Version 3", "uint16", "-", "Bootloader version word 3", 0),
            new(101, 40101, "Bootloader Version 4", "uint16", "-", "Bootloader version word 4", 0),

            new(102, 40102, "Model Number", "uint16", "-", "Device model number", 4820),

            // --- Ana ölçümler ---
            new(103, 40103, "Pack Voltage", "uint16", "0.1 V", "Battery pack total voltage. Multiples of 100 mV.", 512),
            new(104, 40104, "Pack Current", "int16", "0.1 A", "Battery pack current. Multiples of 100 mA.", 250),
            new(107, 40107, "SOC", "uint16", "%", "State of charge of the battery pack.", 87),
            new(108, 40108, "Min. Cell Voltage", "uint16", "mV", "Minimum cell voltage in the system.", 3298),
            new(109, 40109, "Max. Cell Voltage", "uint16", "mV", "Maximum cell voltage in the system.", 3315),
            new(110, 40110, "Min. Temperature", "uint16", "0.1 °C", "Minimum temperature in the system.", 235),
            new(111, 40111, "Max. Temperature", "uint16", "0.1 °C", "Maximum temperature in the system.", 271),

            // 40105, 40106, 40112, 40113 ve 40118..40128 dokümanda Reserved.
            // DataStore içinde bu adresler varsayılan olarak 0 kalır.

            // --- Battery Status (4 register, 64-bit bit alanı) ---
            new(114, 40114, "Battery Status MSB (bit 63:48)", "uint16", "bitfield", "Battery Status most-significant word.", 0x0000),
            new(115, 40115, "Battery Status (bit 47:32)", "uint16", "bitfield", "Battery Status word 2.", 0x0000),
            new(116, 40116, "Battery Status (bit 31:16)", "uint16", "bitfield", "Battery Status word 3.", 0x0000),
            new(117, 40117, "Battery Status LSB (bit 15:0)", "uint16", "bitfield", "Battery Status least-significant word.", 0x0005),

            // --- Hücre / modül seçimi ---
            new(129, 40129, "Slave Data Select", "uint16", "-", "Selects which slave module populates registers 40130..40153.", 0),
            new(130, 40130, "Cell Count in Slave", "uint16", "cell", "Connected cell count in selected slave module.", 16),

            // --- Hücre gerilimleri (mV). 0xFFFF = hücre yok ---
            new(131, 40131, "Cell 1 Voltage",  "uint16", "mV", "Cell 1 voltage. 0xFFFF means cell does not exist.", 3305),
            new(132, 40132, "Cell 2 Voltage",  "uint16", "mV", "Cell 2 voltage. 0xFFFF means cell does not exist.", 3308),
            new(133, 40133, "Cell 3 Voltage",  "uint16", "mV", "Cell 3 voltage. 0xFFFF means cell does not exist.", 3301),
            new(134, 40134, "Cell 4 Voltage",  "uint16", "mV", "Cell 4 voltage. 0xFFFF means cell does not exist.", 3312),
            new(135, 40135, "Cell 5 Voltage",  "uint16", "mV", "Cell 5 voltage. 0xFFFF means cell does not exist.", 3299),
            new(136, 40136, "Cell 6 Voltage",  "uint16", "mV", "Cell 6 voltage. 0xFFFF means cell does not exist.", 3315),
            new(137, 40137, "Cell 7 Voltage",  "uint16", "mV", "Cell 7 voltage. 0xFFFF means cell does not exist.", 3302),
            new(138, 40138, "Cell 8 Voltage",  "uint16", "mV", "Cell 8 voltage. 0xFFFF means cell does not exist.", 3310),
            new(139, 40139, "Cell 9 Voltage",  "uint16", "mV", "Cell 9 voltage. 0xFFFF means cell does not exist.", 3298),
            new(140, 40140, "Cell 10 Voltage", "uint16", "mV", "Cell 10 voltage. 0xFFFF means cell does not exist.", 3307),
            new(141, 40141, "Cell 11 Voltage", "uint16", "mV", "Cell 11 voltage. 0xFFFF means cell does not exist.", 3311),
            new(142, 40142, "Cell 12 Voltage", "uint16", "mV", "Cell 12 voltage. 0xFFFF means cell does not exist.", 3300),
            new(143, 40143, "Cell 13 Voltage", "uint16", "mV", "Cell 13 voltage. 0xFFFF means cell does not exist.", 3309),
            new(144, 40144, "Cell 14 Voltage", "uint16", "mV", "Cell 14 voltage. 0xFFFF means cell does not exist.", 3303),
            new(145, 40145, "Cell 15 Voltage", "uint16", "mV", "Cell 15 voltage. 0xFFFF means cell does not exist.", 3313),
            new(146, 40146, "Cell 16 Voltage", "uint16", "mV", "Cell 16 voltage. 0xFFFF means cell does not exist.", 3306),
            new(147, 40147, "Cell 17 Voltage", "uint16", "mV", "Cell 17 voltage. 0xFFFF means cell does not exist.", 0xFFFF),
            new(148, 40148, "Cell 18 Voltage", "uint16", "mV", "Cell 18 voltage. 0xFFFF means cell does not exist.", 0xFFFF),

            // --- Sıcaklık sensörleri (0.1 °C, int16). 0xFFFF = sensör yok ---
            new(149, 40149, "Temperature 1", "int16", "0.1 °C", "Temperature sensor 1. 0xFFFF means sensor does not exist.", 250),
            new(150, 40150, "Temperature 2", "int16", "0.1 °C", "Temperature sensor 2. 0xFFFF means sensor does not exist.", 248),
            new(151, 40151, "Temperature 3", "int16", "0.1 °C", "Temperature sensor 3. 0xFFFF means sensor does not exist.", 252),
            new(152, 40152, "Temperature 4", "int16", "0.1 °C", "Temperature sensor 4. 0xFFFF means sensor does not exist.", 0xFFFF),
            new(153, 40153, "Temperature 5", "int16", "0.1 °C", "Temperature sensor 5. 0xFFFF means sensor does not exist.", 0xFFFF),

            // --- Modbus slave adresi (FC06 ile yazılabilir) ---
            new(154, 40154, "Modbus Address", "uint16", "1-247", "Modbus slave address. Writing 1..247 changes the device address immediately.", 1),
        };

        // ============================================================
        // Battery Status bit isimleri (Table 2.3.2)
        // ============================================================
        public static IReadOnlyList<string> StatusBitNames { get; } = new[]
        {
            "User Attention Required",                              // 0
            "Over Temperature Protection",                         // 1
            "Over Temperature Warning",                            // 2
            "DCHG Under Temperature Protection",                   // 3
            "DCHG Under Temperature Warning",                      // 4
            "CHG Under Temperature Protection",                    // 5
            "CHG Under Temperature Warning",                       // 6
            "Cell Over Voltage Protection",                        // 7
            "Cell Over Voltage Warning",                           // 8
            "Cell Under Voltage Protection",                       // 9
            "Cell Under Voltage Warning",                          // 10
            "Max. Cell Delta Voltage Protection",                  // 11
            "Max. Temperature Delta Protection",                   // 12
            "DCHG Over Current Warning",                           // 13
            "DCHG Over Current Protection",                        // 14
            "DCHG Over Current 2nd Protection",                    // 15
            "CHG Over Current Warning",                            // 16
            "CHG Over Current Protection",                         // 17
            "Short Circuit Protection",                            // 18
            "Low SOC 1st Warning",                                 // 19
            "Low SOC 2nd Warning",                                 // 20
            "PCB Over Temperature Warning",                        // 21
            "PCB Over Temperature Protection",                     // 22
            "FET Over Temperature Warning",                        // 23
            "FET Over Temperature Protection",                     // 24
            "Internal Error",                                      // 25
            "Cell Connection Error",                               // 26
            "Sleep Cannot Execute",                                // 27
            "Max. Parallel Group Delta Voltage Protection",        // 28
            "Slave Module Communication Error",                    // 29
            "Main Contactor Malfunction",                          // 30
            "PreCharge Fault",                                     // 31
            "System Power On",                                     // 32
            "Multi-Master Enable Power-Out Sequence",              // 33
            "Multi-Master Communication Timeout",                  // 34
            "Multi-Master Parallel Packs Delta Voltage Error",     // 35
            "Charge Contactor Malfunction",                        // 36
            "Discharge Contactor Malfunction",                     // 37
            "Balancing Over Temperature Warning",                  // 38
        };

        /// <summary>
        /// Verilen adres LiBat dokümanındaki 4xxxx Holding Register aralığında mı?
        /// </summary>
        public static bool IsLogicalAddress(int address)
            => address >= FirstLogicalAddress && address <= LastLogicalAddress;

        /// <summary>
        /// LiBat PLC adresini dokümandaki Register/PDU adresine çevirir.
        /// 40111 -> 111.
        /// </summary>
        public static bool TryLogicalToProtocolAddress(int logicalAddress, out ushort protocolAddress)
        {
            protocolAddress = 0;

            if (!IsLogicalAddress(logicalAddress))
                return false;

            int value = logicalAddress - LogicalBase;
            if (value < 0 || value > ushort.MaxValue)
                return false;

            protocolAddress = (ushort)value;
            return true;
        }

        /// <summary>
        /// Register/PDU adresini dokümandaki 4xxxx adrese çevirir.
        /// 111 -> 40111.
        /// </summary>
        public static int ProtocolToLogicalAddress(int protocolAddress)
            => LogicalBase + protocolAddress;

        public static bool TryGetByLogicalAddress(int logicalAddress, out BmsRegister register)
        {
            foreach (BmsRegister item in Registers)
            {
                if (item.LogicalAddress == logicalAddress)
                {
                    register = item;
                    return true;
                }
            }

            register = null!;
            return false;
        }

        public static bool TryGetByProtocolAddress(int protocolAddress, out BmsRegister register)
        {
            foreach (BmsRegister item in Registers)
            {
                if (item.ProtocolAddress == protocolAddress)
                {
                    register = item;
                    return true;
                }
            }

            register = null!;
            return false;
        }

        /// <summary>
        /// Sanal STM32/BMS'in Holding Register hafızasını örnek LiBat verileriyle doldurur.
        /// </summary>
        public static void Apply(ModbusDataStore store)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));

            foreach (BmsRegister reg in Registers)
            {
                if (reg.ProtocolAddress >= 0 &&
                    reg.ProtocolAddress < store.HoldingRegisters.Length)
                {
                    store.SetHoldingRegister(reg.ProtocolAddress, reg.Sample);
                }
            }
        }

        /// <summary>
        /// Tek bir ham register değerini LiBat dokümanındaki ölçek/birime göre
        /// kullanıcı dostu metne çevirir.
        /// </summary>
        public static string FormatValue(BmsRegister reg, ushort raw)
        {
            if (reg == null)
                throw new ArgumentNullException(nameof(reg));

            int logical = reg.LogicalAddress;

            // 0xFFFF özel "mevcut değil" değeri kullanılan alanlar.
            if (logical is >= 40131 and <= 40148 && raw == 0xFFFF)
                return "Hücre mevcut değil";

            if (logical is >= 40149 and <= 40153 && raw == 0xFFFF)
                return "Sensör mevcut değil";

            return logical switch
            {
                40103 => $"{raw / 10.0:0.0} V",
                40104 => $"{unchecked((short)raw) / 10.0:0.0} A",
                40107 => $"{raw} %",

                40108 or 40109 =>
                    $"{raw} mV ({raw / 1000.0:0.000} V)",

                40110 or 40111 =>
                    $"{raw / 10.0:0.0} °C",

                >= 40131 and <= 40148 =>
                    $"{raw} mV ({raw / 1000.0:0.000} V)",

                >= 40149 and <= 40153 =>
                    $"{unchecked((short)raw) / 10.0:0.0} °C",

                >= 40114 and <= 40117 =>
                    $"0x{raw:X4}",

                40154 =>
                    $"Unit ID {raw}",

                _ => raw.ToString()
            };
        }

        /// <summary>
        /// Bir register için arayüzde gösterilecek açıklamayı üretir.
        /// </summary>
        public static string BuildComment(BmsRegister reg, ushort raw)
        {
            string formatted = FormatValue(reg, raw);
            return $"Register {reg.ProtocolAddress} | {reg.Description} | Yorumlanan değer: {formatted}";
        }

        /// <summary>
        /// 40114..40117 status registerlarını tek 64-bit değere birleştirir.
        /// 40114 = bits 63:48, 40117 = bits 15:0.
        /// </summary>
        public static ulong CombineStatus(
            ushort r40114,
            ushort r40115,
            ushort r40116,
            ushort r40117)
        {
            return ((ulong)r40114 << 48) |
                   ((ulong)r40115 << 32) |
                   ((ulong)r40116 << 16) |
                    (ulong)r40117;
        }

        /// <summary>
        /// 64-bit Battery Status içindeki 1 olan bitleri isimleriyle döndürür.
        /// Boş liste = aktif durum/uyarı/arıza biti yok.
        /// </summary>
        public static List<string> DecodeStatus(ulong status)
        {
            List<string> active = new();

            for (int bit = 0; bit < StatusBitNames.Count; bit++)
            {
                if ((status & (1UL << bit)) != 0)
                    active.Add($"Bit {bit}: {StatusBitNames[bit]}");
            }

            return active;
        }

        /// <summary>
        /// Bir FC03 cevabı 40114..40117'nin tamamını içeriyorsa Battery Status'u çözer.
        /// logicalStartAddress kullanıcıya gösterilen 4xxxx başlangıç adresidir.
        /// </summary>
        public static bool TryDecodeStatusFromRead(
            int logicalStartAddress,
            ushort[] values,
            out ulong status,
            out List<string> active)
        {
            status = 0;
            active = new List<string>();

            if (values == null || values.Length == 0)
                return false;

            int firstIndex = 40114 - logicalStartAddress;
            int lastIndex = 40117 - logicalStartAddress;

            if (firstIndex < 0 || lastIndex >= values.Length)
                return false;

            status = CombineStatus(
                values[firstIndex],
                values[firstIndex + 1],
                values[firstIndex + 2],
                values[firstIndex + 3]);

            active = DecodeStatus(status);
            return true;
        }
    }
}
