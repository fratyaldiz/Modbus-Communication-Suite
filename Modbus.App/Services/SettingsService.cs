using System;
using System.IO;
using System.Text.Json;

namespace Modbus.App.Services
{
    /// <summary>Uygulama genelinde saklanan kullanıcı ayarları.</summary>
    public sealed class AppSettings
    {
        public string LastRole { get; set; } = "";

        // Master
        public int MasterProtocol { get; set; }          // 0=TCP, 1=RTU
        public string MasterIP { get; set; } = "127.0.0.1";
        public string MasterPort { get; set; } = "1502";
        public string MasterCOM { get; set; } = "COM3";
        public string MasterBaud { get; set; } = "9600";
        public string MasterDataBits { get; set; } = "8";
        public string MasterParity { get; set; } = "None";
        public string MasterStopBits { get; set; } = "One";
        public string MasterTimeout { get; set; } = "3000";

        // Slave
        public int SlaveProtocol { get; set; }           // 0=TCP, 1=RTU
        public string SlavePort { get; set; } = "1502";
        public string SlaveCOM { get; set; } = "COM3";
        public string SlaveBaud { get; set; } = "9600";
        public string SlaveDataBits { get; set; } = "8";
        public string SlaveParity { get; set; } = "None";
        public string SlaveStopBits { get; set; } = "One";
        public string SlaveUnitId { get; set; } = "1";

        public string LastDeviceProfile { get; set; } = "LiBat BMS / STM32";
    }

    /// <summary>
    /// Ayarları %LocalAppData%/ModbusCommunicationSuite/settings.json içine
    /// System.Text.Json ile kaydeder/okur. Ek NuGet paketi kullanmaz.
    /// </summary>
    public sealed class SettingsService
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModbusCommunicationSuite");

        private static readonly string FilePath = Path.Combine(Folder, "settings.json");

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public AppSettings Current { get; private set; } = new();

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                // Bozuk/erişilemeyen ayar dosyası uygulamayı durdurmaz; varsayılana düşülür.
                Current = new AppSettings();
            }

            return Current;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Options));
            }
            catch
            {
                // Ayar kaydedilemezse sessizce geç (yetki/disk sorunu uygulamayı kapatmasın).
            }
        }
    }
}
