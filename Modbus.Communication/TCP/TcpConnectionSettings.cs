namespace Modbus.Communication.TCP
{
    /// <summary>
    /// Modbus TCP bağlantısı için ayarlar (IP, port, zaman aşımı).
    /// Bu bir "ayar taşıyıcı" sınıftır; sadece veri tutar, iş yapmaz.
    /// Yanına yazdığımız "= ..." değerler VARSAYILAN değerlerdir.
    /// </summary>
    public class TcpConnectionSettings
    {
        /// <summary>Cihazın IP adresi. 127.0.0.1 = "localhost" (kendi bilgisayarın).</summary>
        public string IpAddress { get; set; } = "127.0.0.1";

        /// <summary>TCP portu. Modbus için standart port 502'dir.</summary>
        public int Port { get; set; } = 502;

        /// <summary>Zaman aşımı (milisaniye). Cevap bu süre içinde gelmezse vazgeç.</summary>
        public int Timeout { get; set; } = 3000;
    }
}
