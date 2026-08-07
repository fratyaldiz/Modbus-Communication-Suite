using System.Collections.Generic;

using Modbus.App.Models;

namespace Modbus.App.Profiles
{
    /// <summary>
    /// Bir cihaz profili: register haritasını ve (varsa) status/olay tanımlarını sağlar.
    /// Slave ekranı bu arayüz üzerinden çalışır; böylece LiBat, boş cihaz veya ileride
    /// başka STM32 cihazları aynı altyapıyla eklenebilir. Cihaza özel her şey burada,
    /// ViewModel'de DEĞİL.
    /// </summary>
    public interface IDeviceProfile
    {
        /// <summary>Kullanıcıya gösterilen profil adı.</summary>
        string Name { get; }

        string Description { get; }

        /// <summary>Adresleme tabanı (LiBat için 40000; PDU = Logical - Base).</summary>
        int AddressBase { get; }

        /// <summary>Profilin varsayılan register tanımlarını üretir (her çağrıda yeni kopya).</summary>
        IEnumerable<DeviceRegisterDefinition> CreateRegisters();

        /// <summary>Bu profilin bir Battery Status / olay tanımı var mı?</summary>
        bool HasStatus { get; }

        /// <summary>Status bit tanımları (LiBat: 39 bit).</summary>
        IReadOnlyList<StatusBitDefinition> StatusBits { get; }

        /// <summary>Status kelimesini oluşturan register'ların PDU adresleri (LiBat: 114..117).</summary>
        int[] StatusRegisterPduAddresses { get; }

        /// <summary>Status register değerlerini tek 64-bit değere birleştirir.</summary>
        ulong CombineStatus(IReadOnlyList<ushort> statusRegisters);
    }
}
