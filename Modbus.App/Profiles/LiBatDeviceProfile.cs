using System;
using System.Collections.Generic;

using Modbus.App.Models;
using Modbus.Communication.Server;

namespace Modbus.App.Profiles
{
    /// <summary>
    /// LiBat BMS / STM32 cihaz profili. Mevcut LiBatBmsProfile register haritasını
    /// (Modbus.Communication katmanı) genel DeviceRegisterDefinition modeline çevirir
    /// ve Battery Status bit tanımlarını sağlar. LiBat'a özel her şey burada tutulur;
    /// ViewModel LiBat'ı bilmez.
    /// </summary>
    public sealed class LiBatDeviceProfile : IDeviceProfile
    {
        public string Name => "LiBat BMS / STM32";
        public string Description => "LiBat batarya yönetim sistemi register haritası (40088..40154).";
        public int AddressBase => LiBatBmsProfile.LogicalBase; // 40000

        public IEnumerable<DeviceRegisterDefinition> CreateRegisters()
        {
            foreach (BmsRegister reg in LiBatBmsProfile.Registers)
            {
                (double scale, string unit) = MapUnit(reg.Unit);

                bool isCell = reg.LogicalAddress is >= 40131 and <= 40148;
                bool isTemp = reg.LogicalAddress is >= 40149 and <= 40153;

                yield return new DeviceRegisterDefinition
                {
                    PduAddress = reg.ProtocolAddress,
                    LogicalAddress = reg.LogicalAddress,
                    RegisterType = RegisterKind.Holding,
                    Name = reg.Name,
                    DataType = reg.Type.Equals("int16", StringComparison.OrdinalIgnoreCase) ? "Int16" : "UInt16",
                    Scale = scale,
                    Unit = unit,
                    Description = reg.Description,
                    Writable = true, // simülatörde tüm register'lar düzenlenebilir/yazılabilir
                    IsProfileRegister = true,
                    NoValueSentinel = (isCell || isTemp) ? (ushort)0xFFFF : (ushort?)null,
                    RawValue = reg.Sample
                };
            }
        }

        public bool HasStatus => true;

        public IReadOnlyList<StatusBitDefinition> StatusBits { get; } = BuildStatusBits();

        public int[] StatusRegisterPduAddresses { get; } = { 114, 115, 116, 117 };

        public ulong CombineStatus(IReadOnlyList<ushort> statusRegisters)
        {
            if (statusRegisters == null || statusRegisters.Count < 4)
                return 0;

            return LiBatBmsProfile.CombineStatus(
                statusRegisters[0],
                statusRegisters[1],
                statusRegisters[2],
                statusRegisters[3]);
        }

        private static List<StatusBitDefinition> BuildStatusBits()
        {
            var list = new List<StatusBitDefinition>();
            IReadOnlyList<string> names = LiBatBmsProfile.StatusBitNames;

            for (int bit = 0; bit < names.Count; bit++)
                list.Add(new StatusBitDefinition(bit, names[bit], SeverityOf(names[bit])));

            return list;
        }

        private static EventSeverity SeverityOf(string name)
        {
            if (name.Contains("Protection", StringComparison.OrdinalIgnoreCase))
                return EventSeverity.Protection;

            if (name.Contains("Warning", StringComparison.OrdinalIgnoreCase))
                return EventSeverity.Warning;

            if (name.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Fault", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Malfunction", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Timeout", StringComparison.OrdinalIgnoreCase))
                return EventSeverity.Fault;

            return EventSeverity.Status;
        }

        /// <summary>LiBat birim metnini (scale, gösterim birimi) ikilisine çevirir.</summary>
        private static (double Scale, string Unit) MapUnit(string unit)
        {
            return unit switch
            {
                "0.1 °C" => (0.1, "°C"),
                "0.1 V" => (0.1, "V"),
                "0.1 A" => (0.1, "A"),
                "mV" => (1.0, "mV"),
                "%" => (1.0, "%"),
                "cell" => (1.0, string.Empty),
                _ => (1.0, string.Empty)
            };
        }
    }
}
