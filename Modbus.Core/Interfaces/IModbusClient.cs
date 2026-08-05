using System.Threading.Tasks;


namespace Modbus.Core.Interfaces
{

    public interface IModbusClient
    {


        /// <summary>
        /// Modbus cihazına bağlantı kurar
        /// </summary>
        Task ConnectAsync();



        /// <summary>
        /// Bağlantıyı kapatır
        /// </summary>
        Task DisconnectAsync();



        /// <summary>
        /// Ham Modbus paketi gönderir
        /// </summary>
        /// <param name="packet">
        /// Gönderilecek byte dizisi
        /// </param>

        Task<byte[]> SendAsync(byte[] packet);



        /// <summary>
        /// Bağlantı durumu
        /// </summary>

        bool IsConnected
        {
            get;
        }


    }

}
