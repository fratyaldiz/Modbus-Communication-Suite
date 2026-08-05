using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Modbus.Core.Interfaces;

namespace Modbus.Communication.TCP
{
    /// <summary>
    /// Modbus TCP client. Bir cevabı tek ReadAsync ile almak yerine önce 7 byte MBAP
    /// header'ı, ardından Length alanının söylediği kadar PDU verisini tam olarak okur.
    /// </summary>
    public class ModbusTcpClient : IModbusClient
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private readonly TcpConnectionSettings _settings;
        private readonly SemaphoreSlim _requestLock = new(1, 1);

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

        public ModbusTcpClient(TcpConnectionSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task ConnectAsync()
        {
            await DisconnectAsync();

            _tcpClient = new TcpClient
            {
                ReceiveTimeout = _settings.Timeout,
                SendTimeout = _settings.Timeout
            };

            using CancellationTokenSource timeout = new(_settings.Timeout);
            await _tcpClient.ConnectAsync(_settings.IpAddress, _settings.Port, timeout.Token);
            _stream = _tcpClient.GetStream();
        }

        public Task DisconnectAsync()
        {
            _stream?.Dispose();
            _stream = null;

            _tcpClient?.Dispose();
            _tcpClient = null;

            return Task.CompletedTask;
        }

        public async Task<byte[]> SendAsync(byte[] packet)
        {
            if (!IsConnected || _stream == null)
                throw new InvalidOperationException("TCP bağlantısı yok. Önce bağlanın.");

            await _requestLock.WaitAsync();
            try
            {
                using CancellationTokenSource timeout = new(_settings.Timeout);

                await _stream.WriteAsync(packet, timeout.Token);

                byte[] header = await ReadExactlyAsync(_stream, 7, timeout.Token);
                int length = (header[4] << 8) | header[5];

                if (length < 2 || length > 254)
                    throw new Exception($"Geçersiz Modbus TCP cevap uzunluğu: {length}");

                // Header içinde Unit ID zaten okundu. Geriye Function Code + Data kalır.
                byte[] remaining = await ReadExactlyAsync(_stream, length - 1, timeout.Token);

                byte[] response = new byte[header.Length + remaining.Length];
                Buffer.BlockCopy(header, 0, response, 0, header.Length);
                Buffer.BlockCopy(remaining, 0, response, header.Length, remaining.Length);
                return response;
            }
            finally
            {
                _requestLock.Release();
            }
        }

        private static async Task<byte[]> ReadExactlyAsync(
            NetworkStream stream,
            int count,
            CancellationToken token)
        {
            byte[] buffer = new byte[count];
            int read = 0;

            while (read < count)
            {
                int current = await stream.ReadAsync(buffer.AsMemory(read, count - read), token);
                if (current == 0)
                    throw new Exception("Karşı taraf bağlantıyı kapattı.");

                read += current;
            }

            return buffer;
        }
    }
}