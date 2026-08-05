namespace Modbus.App.Models
{
    /// <summary>
    /// Haberleşme yolu.
    /// TCP = Ethernet/IP üzerinden (IP + port). RTU = seri port (COM/RS485) üzerinden.
    /// </summary>
    public enum ProtocolType
    {
        TCP,
        RTU
    }
}
