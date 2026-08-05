using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Modbus.Core.Interfaces;
using Modbus.Protocol.Helpers;

namespace Modbus.Communication.RTU
{
    /// <summary>
    /// Modbus RTU Master/Client.
    /// COM portu açar, RTU isteğini gönderir, cevabın tamamını bekler ve CRC'yi doğrular.
    /// </summary>
    public sealed class ModbusRtuClient : IModbusClient, IDisposable
    {
        private readonly RtuConnectionSettings _settings;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private SerialPort? _serialPort;
        private bool _disposed;

        public bool IsConnected =>
            !_disposed &&
            _serialPort != null &&
            _serialPort.IsOpen;

        public ModbusRtuClient(RtuConnectionSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Bilgisayarda o anda görünen COM portlarını doğal sırada döndürür.
        /// Örnek sıra: COM2, COM3, COM10.
        /// </summary>
        public static string[] GetAvailablePortNames()
        {
            return SerialPort
                .GetPortNames()
                .OrderBy(GetPortSortNumber)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public Task ConnectAsync()
        {
            ThrowIfDisposed();
            _settings.Validate();

            return Task.Run(() =>
            {
                DisconnectInternal();

                var serialPort = new SerialPort
                {
                    PortName = _settings.PortName.Trim(),
                    BaudRate = _settings.BaudRate,
                    DataBits = _settings.DataBits,
                    Parity = MapParity(_settings.Parity),
                    StopBits = MapStopBits(_settings.StopBits),
                    Handshake = Handshake.None,
                    ReadTimeout = _settings.Timeout,
                    WriteTimeout = _settings.Timeout,
                    DtrEnable = false,
                    RtsEnable = false,
                    ReadBufferSize = 4096,
                    WriteBufferSize = 2048
                };

                try
                {
                    serialPort.Open();
                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();
                    _serialPort = serialPort;
                }
                catch
                {
                    serialPort.Dispose();
                    throw;
                }
            });
        }

        public Task DisconnectAsync()
        {
            return Task.Run(DisconnectInternal);
        }

        public async Task<byte[]> SendAsync(byte[] packet)
        {
            ThrowIfDisposed();

            if (packet == null)
                throw new ArgumentNullException(nameof(packet));

            if (packet.Length < 4)
                throw new ArgumentException("RTU istek paketi çok kısa.", nameof(packet));

            await _sendLock.WaitAsync().ConfigureAwait(false);

            try
            {
                return await Task.Run(() => SendAndReceive(packet)).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private byte[] SendAndReceive(byte[] request)
        {
            SerialPort serialPort = _serialPort
                ?? throw new InvalidOperationException("RTU bağlantısı yok. Önce COM portu açın.");

            if (!serialPort.IsOpen)
                throw new InvalidOperationException("RTU COM portu kapalı.");

            byte requestedUnitId = request[0];
            byte requestedFunction = request[1];

            // Uygulama cevap beklediği için broadcast adresi bu aşamada kabul edilmiyor.
            if (requestedUnitId == 0)
            {
                throw new InvalidOperationException(
                    "RTU Unit ID 0 broadcast adresidir ve cevap göndermez. " +
                    "Cevap beklenen testlerde 1 ile 247 arasında bir Unit ID kullanın.");
            }

            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            serialPort.Write(request, 0, request.Length);

            var response = new List<byte>(256);
            var stopwatch = Stopwatch.StartNew();
            int? expectedLength = null;

            while (stopwatch.ElapsedMilliseconds < _settings.Timeout)
            {
                int available = serialPort.BytesToRead;

                if (available > 0)
                {
                    byte[] chunk = new byte[available];
                    int read = serialPort.Read(chunk, 0, chunk.Length);

                    for (int i = 0; i < read; i++)
                        response.Add(chunk[i]);

                    expectedLength ??= TryGetExpectedResponseLength(response);

                    if (expectedLength.HasValue && response.Count >= expectedLength.Value)
                    {
                        byte[] completeFrame = response
                            .Take(expectedLength.Value)
                            .ToArray();

                        ValidateResponse(
                            completeFrame,
                            requestedUnitId,
                            requestedFunction);

                        return completeFrame;
                    }
                }

                Thread.Sleep(2);
            }

            if (response.Count == 0)
            {
                throw new TimeoutException(
                    $"RTU cihazından {_settings.Timeout} ms içinde hiç cevap gelmedi. " +
                    "COM portu, baud rate, parity, data bits, stop bits, Unit ID ve A/B kablolarını kontrol edin.");
            }

            throw new TimeoutException(
                $"RTU cevabı eksik kaldı. Alınan: {response.Count} byte, " +
                $"beklenen: {(expectedLength?.ToString() ?? "belirlenemedi")} byte.");
        }

        private static int? TryGetExpectedResponseLength(IReadOnlyList<byte> bytes)
        {
            if (bytes.Count < 2)
                return null;

            byte function = bytes[1];

            // Hata cevabı: Unit + Function|0x80 + Exception + CRC(2) = 5 byte.
            if ((function & 0x80) != 0)
                return 5;

            return function switch
            {
                // Okuma cevaplarında üçüncü byte veri byte sayısını taşır.
                0x01 or 0x02 or 0x03 or 0x04 when bytes.Count >= 3
                    => 5 + bytes[2],

                // Tekli/çoklu yazma onay cevapları sabit 8 bytedır.
                0x05 or 0x06 or 0x0F or 0x10
                    => 8,

                _ => null
            };
        }

        private static void ValidateResponse(
            byte[] response,
            byte requestedUnitId,
            byte requestedFunction)
        {
            if (response.Length < 5)
                throw new InvalidDataException("RTU cevabı çok kısa.");

            if (response[0] != requestedUnitId)
            {
                throw new InvalidDataException(
                    $"Yanlış cihazdan cevap geldi. Beklenen Unit ID: {requestedUnitId}, " +
                    $"gelen Unit ID: {response[0]}.");
            }

            byte responseFunction = response[1];
            bool isNormalResponse = responseFunction == requestedFunction;
            bool isExceptionResponse = responseFunction == (byte)(requestedFunction | 0x80);

            if (!isNormalResponse && !isExceptionResponse)
            {
                throw new InvalidDataException(
                    $"Cevaptaki function code istekle uyuşmuyor. " +
                    $"Beklenen: 0x{requestedFunction:X2}, gelen: 0x{responseFunction:X2}.");
            }

            int dataLength = response.Length - 2;
            byte[] withoutCrc = new byte[dataLength];
            Array.Copy(response, withoutCrc, dataLength);

            ushort calculatedCrc = CRC16.Calculate(withoutCrc);
            ushort receivedCrc = (ushort)(
                response[^2] |
                (response[^1] << 8));

            if (calculatedCrc != receivedCrc)
            {
                throw new InvalidDataException(
                    $"RTU CRC hatası. Hesaplanan: 0x{calculatedCrc:X4}, " +
                    $"gelen: 0x{receivedCrc:X4}. Kablo veya seri ayarlarını kontrol edin.");
            }
        }

        private void DisconnectInternal()
        {
            SerialPort? serialPort = _serialPort;
            _serialPort = null;

            if (serialPort == null)
                return;

            try
            {
                if (serialPort.IsOpen)
                    serialPort.Close();
            }
            finally
            {
                serialPort.Dispose();
            }
        }

        private static Parity MapParity(RtuParity parity)
        {
            return parity switch
            {
                RtuParity.None => Parity.None,
                RtuParity.Odd => Parity.Odd,
                RtuParity.Even => Parity.Even,
                RtuParity.Mark => Parity.Mark,
                RtuParity.Space => Parity.Space,
                _ => throw new ArgumentOutOfRangeException(nameof(parity))
            };
        }

        private static StopBits MapStopBits(RtuStopBits stopBits)
        {
            return stopBits switch
            {
                RtuStopBits.One => StopBits.One,
                RtuStopBits.OnePointFive => StopBits.OnePointFive,
                RtuStopBits.Two => StopBits.Two,
                _ => throw new ArgumentOutOfRangeException(nameof(stopBits))
            };
        }

        private static int GetPortSortNumber(string portName)
        {
            string digits = new(portName.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int number)
                ? number
                : int.MaxValue;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ModbusRtuClient));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            DisconnectInternal();
            _sendLock.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
