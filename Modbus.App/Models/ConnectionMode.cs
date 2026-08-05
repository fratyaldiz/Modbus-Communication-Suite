namespace Modbus.App.Models
{
    /// <summary>
    /// Uygulamanın hangi rolde çalıştığı.
    /// Client = istek gönderen (Master). Server = istek karşılayan (Slave).
    /// Staj görevi: ikisi de aynı ekranda seçilebilecek.
    /// </summary>
    public enum ConnectionMode
    {
        Client, // Master: sorar
        Server  // Slave: cevaplar
    }
}
