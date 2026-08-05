using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

using Modbus.Communication.RTU;
using Modbus.Protocol.Builders;
using Modbus.Protocol.Helpers;

namespace Modbus.Communication.Server
{
    /// <summary>
    /// Modbus RTU SLAVE (seri port üzerinden sunucu / sanal cihaz).
    ///
    /// NE ZAMAN? Kart bir Modbus MASTER ise (soruları o soruyorsa), bilgisayardaki
    /// bu uygulama SLAVE gibi davranıp cevap vermelidir. Bu sınıf bir COM portunu açar,
    /// gelen RTU isteklerini dinler, CRC'yi doğrular ve cevap üretir.
    /// (Kart bir SLAVE ise, tersine ModbusRtuClient / Master tarafını kullanırsın.)
    ///
    /// İstek işleme mantığı ModbusRequestHandler'da; burada sadece seri port
    /// okuma/yazma ve RTU çerçeveleme (CRC) var. DataStore TCP server ile ORTAKtır;
    /// yani UI tablosundaki değerler burada da geçerlidir.
    /// </summary>
    public sealed class ModbusRtuServer
    {
        private readonly RtuConnectionSettings _settings;
        private readonly byte _unitId;
        private readonly ModbusRequestHandler _handler;
        private readonly PacketBuilder _builder = new();

        private SerialPort? _port;
        private CancellationTokenSource? _cts;

        public event Action<string>? OnLog;
        public bool IsRunning { get; private set; }

        /// <param name="unitId">Bu sanal cihazın adresi. Master bu adrese soru sorar (1-247).</param>
        public ModbusRtuServer(ModbusDataStore dataStore, RtuConnectionSettings settings, byte unitId = 1)
        {
            if (dataStore == null) throw new ArgumentNullException(nameof(dataStore));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _unitId = unitId;
            _handler = new ModbusRequestHandler(dataStore, Log);
        }

        public void Start()
        {
            if (IsRunning) return;

            _settings.Validate();

            _port = new SerialPort
            {
                PortName = _settings.PortName.Trim(),
                BaudRate = _settings.BaudRate,
                DataBits = _settings.DataBits,
                Parity = MapParity(_settings.Parity),
                StopBits = MapStopBits(_settings.StopBits),
                Handshake = Handshake.None,
                ReadTimeout = 50,
                WriteTimeout = _settings.Timeout
            };

            _port.Open();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();

            _cts = new CancellationTokenSource();
            IsRunning = true;
            Log($"RTU Slave başladı. {_settings.PortName} @ {_settings.BaudRate}, Unit {_unitId} dinleniyor.");

            _ = Task.Run(() => ListenLoop(_cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _cts?.Cancel();

            try
            {
                if (_port != null && _port.IsOpen)
                    _port.Close();
            }
            catch { /* kapatma hatası önemsiz */ }
            finally
            {
                _port?.Dispose();
                _port = null;
            }

            Log("RTU Slave durduruldu.");
        }

        private void ListenLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _port != null && _port.IsOpen)
                {
                    byte[] frame = ReadFrame(_port, token);
                    if (frame.Length == 0) continue; // veri gelmedi, dinlemeye devam

                    if (frame.Length < 4)
                    {
                        Log("[RTU Slave] Çok kısa çerçeve atlandı: " + ToHex(frame));
                        continue;
                    }

                    if (!CheckCrc(frame))
                    {
                        Log("[RTU Slave] CRC hatası, çerçeve atlandı: " + ToHex(frame));
                        continue;
                    }

                    byte slaveId = frame[0];

                    // Sadece bize (veya broadcast 0'a) gelen isteklere bak.
                    if (slaveId != _unitId && slaveId != 0)
                        continue;

                    Log("[RTU Slave RX] " + ToHex(frame));

                    // PDU = çerçeveden Slave ID (baş) ve CRC (son 2) çıkarılmış hali.
                    byte[] pdu = new byte[frame.Length - 3];
                    Array.Copy(frame, 1, pdu, 0, pdu.Length);

                    byte[] responsePdu = _handler.Process(pdu);

                    // Broadcast (0) isteklere cevap verilmez.
                    if (slaveId == 0) continue;

                    byte[] response = _builder.WrapRtu(slaveId, responsePdu);
                    _port.Write(response, 0, response.Length);
                    Log("[RTU Slave TX] " + ToHex(response));
                }
            }
            catch (OperationCanceledException) { /* normal durdurma */ }
            catch (Exception ex)
            {
                Log("RTU Slave dinleme hatası: " + ex.Message);
            }
        }

        /// <summary>
        /// Bir tam RTU çerçevesini okur. RTU'da paket uzunluğu başlıkta yazmaz; paketler
        /// aralarındaki SESSİZLİK (idle gap) ile ayrılır. İlk byte geldikten sonra, kısa
        /// bir süre yeni byte gelmezse "çerçeve bitti" deriz.
        /// </summary>
        private static byte[] ReadFrame(SerialPort port, CancellationToken token)
        {
            List<byte> buffer = new(256);

            // 1) İlk byte'ı bekle (yoksa TimeoutException; döngüde tekrar denenir).
            try
            {
                buffer.Add((byte)port.ReadByte());
            }
            catch (TimeoutException)
            {
                return Array.Empty<byte>();
            }

            // 2) Byte akmaya devam ettikçe oku; kısa boşlukta dur.
            while (!token.IsCancellationRequested)
            {
                try
                {
                    buffer.Add((byte)port.ReadByte());
                }
                catch (TimeoutException)
                {
                    break; // sessizlik = çerçeve tamamlandı
                }
            }

            return buffer.ToArray();
        }

        private static bool CheckCrc(byte[] frame)
        {
            int len = frame.Length;
            byte[] withoutCrc = new byte[len - 2];
            Array.Copy(frame, 0, withoutCrc, 0, len - 2);

            ushort calc = CRC16.Calculate(withoutCrc);
            byte calcLo = (byte)(calc & 0xFF);
            byte calcHi = (byte)(calc >> 8);

            // RTU'da CRC "önce düşük byte, sonra yüksek byte" gönderilir.
            return frame[len - 2] == calcLo && frame[len - 1] == calcHi;
        }

        private static Parity MapParity(RtuParity parity) => parity switch
        {
            RtuParity.None => Parity.None,
            RtuParity.Odd => Parity.Odd,
            RtuParity.Even => Parity.Even,
            RtuParity.Mark => Parity.Mark,
            RtuParity.Space => Parity.Space,
            _ => Parity.None
        };

        private static StopBits MapStopBits(RtuStopBits stopBits) => stopBits switch
        {
            RtuStopBits.One => StopBits.One,
            RtuStopBits.OnePointFive => StopBits.OnePointFive,
            RtuStopBits.Two => StopBits.Two,
            _ => StopBits.One
        };

        private static string ToHex(byte[] data) => BitConverter.ToString(data).Replace("-", " ");

        private void Log(string message) => OnLog?.Invoke(message);
    }
}
