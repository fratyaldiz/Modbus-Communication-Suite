namespace Modbus.App.Models
{
    /// <summary>
    /// Arayüzde girilen bağlantı ayarlarını tek bir yerde toplayan model.
    ///
    /// NOT: Bu dosya eskiden YANLIŞ içerikliydi; içinde ikinci bir "ViewModelBase"
    /// sınıfı vardı (üstelik namespace'siz). Bu, projede iki tane ViewModelBase
    /// olmasına ve derleme çakışmasına yol açıyordu. Düzelttik: artık gerçek bir
    /// ConnectionSettings sınıfı.
    /// </summary>
    public class ConnectionSettings
    {
        // ---- Ortak ----
        /// <summary>Client mı Server mı? (enum ConnectionMode)</summary>
        public ConnectionMode Mode { get; set; } = ConnectionMode.Client;

        /// <summary>TCP mi RTU mu? (enum ProtocolType)</summary>
        public ProtocolType Protocol { get; set; } = ProtocolType.TCP;

        // ---- TCP ----
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;

        // ---- RTU ----
        public string ComPort { get; set; } = "COM3";
        public int BaudRate { get; set; } = 9600;
    }
}
