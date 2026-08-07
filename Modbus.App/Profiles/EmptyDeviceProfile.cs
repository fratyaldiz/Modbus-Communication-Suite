using System;
using System.Collections.Generic;

using Modbus.App.Models;

namespace Modbus.App.Profiles
{
    /// <summary>
    /// Boş / özel cihaz profili. Hiç hazır register içermez; kullanıcı Add Register ile
    /// kendi cihazını sıfırdan oluşturur. Status tanımı yoktur.
    /// </summary>
    public sealed class EmptyDeviceProfile : IDeviceProfile
    {
        public string Name => "Empty / Custom Device";
        public string Description => "Boş başlar. Register'ları Add Register ile kendin oluşturursun.";
        public int AddressBase => 40000;

        public IEnumerable<DeviceRegisterDefinition> CreateRegisters()
            => Array.Empty<DeviceRegisterDefinition>();

        public bool HasStatus => false;
        public IReadOnlyList<StatusBitDefinition> StatusBits { get; } = Array.Empty<StatusBitDefinition>();
        public int[] StatusRegisterPduAddresses { get; } = Array.Empty<int>();
        public ulong CombineStatus(IReadOnlyList<ushort> statusRegisters) => 0;
    }
}
