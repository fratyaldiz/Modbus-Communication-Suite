namespace Modbus.App.Models
{
    /// <summary>
    /// Ekrandaki "Devices" (cihazlar) listesinde gösterilen bir cihazı temsil eder.
    /// Sadece veri tutar (ad, adres, port, protokol).
    ///
    /// NOT: string alanlara "= string.Empty" verdik. Sebep: Nullable açık olduğu için
    /// (csproj'da Nullable enable) boş bırakılan string'ler "null olabilir" uyarısı
    /// üretir. Varsayılan boş metin vererek uyarıyı temizliyoruz.
    /// </summary>
    public class ModbusDevice
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }
        public ProtocolType Protocol { get; set; }
    }
}
