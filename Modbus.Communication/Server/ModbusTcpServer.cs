using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Modbus.Protocol.Builders;
using Modbus.Protocol.Functions;

namespace Modbus.Communication.Server
{
    /// <summary>
    /// Modbus TCP server. Gelen MBAP + PDU paketini çözer, ortak DataStore üzerinde
    /// işlemi yapar ve Modbus cevabını üretir.
    /// </summary>
    public class ModbusTcpServer
    {
        private readonly int _port;
        private readonly ModbusDataStore _dataStore;
        private readonly PacketBuilder _builder = new();

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        public event Action<string>? OnLog;

        public bool IsRunning { get; private set; }

        public ModbusTcpServer(ModbusDataStore dataStore, int port = 502)
        {
            _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
            _port = port;
        }

        public void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            IsRunning = true;
            Log($"Server başladı. 0.0.0.0:{_port} dinleniyor.");

            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _cts?.Cancel();
            _listener?.Stop();
            Log("Server durduruldu.");
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client = await _listener!.AcceptTcpClientAsync(token);
                    Log($"Client bağlandı: {client.Client.RemoteEndPoint}");
                    _ = Task.Run(() => HandleClientAsync(client, token), token);
                }
            }
            catch (OperationCanceledException)
            {
                // Server normal şekilde durduruldu.
            }
            catch (ObjectDisposedException)
            {
                // Listener Stop() ile kapatıldı.
            }
            catch (Exception ex)
            {
                Log("Kabul döngüsü hatası: " + ex.Message);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        byte[] header = await ReadExactlyAsync(stream, 7, token);
                        if (header.Length == 0) break;

                        ushort transactionId = ReadUInt16(header, 0);
                        ushort protocolId = ReadUInt16(header, 2);
                        int length = ReadUInt16(header, 4);
                        byte unitId = header[6];

                        if (protocolId != 0)
                        {
                            Log($"[Server] Geçersiz Protocol ID: {protocolId}");
                            break;
                        }

                        if (length < 2 || length > 254)
                        {
                            Log($"[Server] Geçersiz MBAP Length: {length}");
                            break;
                        }

                        int pduLength = length - 1;
                        byte[] pdu = await ReadExactlyAsync(stream, pduLength, token);
                        if (pdu.Length == 0) break;

                        byte[] request = Combine(header, pdu);
                        Log("[Server RX] " + ToHex(request));

                        byte[] responsePdu = HandleRequest(pdu);
                        byte[] response = _builder.WrapTcp(transactionId, unitId, responsePdu);

                        await stream.WriteAsync(response, token);
                        Log("[Server TX] " + ToHex(response));
                    }
                }
                catch (OperationCanceledException)
                {
                    // Server durduruldu.
                }
                catch (Exception ex)
                {
                    Log("Client işleme hatası: " + ex.Message);
                }
                finally
                {
                    Log("Client bağlantısı kapandı.");
                }
            }
        }

        private byte[] HandleRequest(byte[] pdu)
        {
            if (pdu.Length == 0)
                return BuildException(0, 0x03); // Illegal Data Value

            byte function = pdu[0];

            switch (function)
            {
                case (byte)ModbusFunctionCode.ReadHoldingRegisters:
                    return HandleReadHoldingRegisters(pdu, function);

                case (byte)ModbusFunctionCode.ReadInputRegisters:
                    return HandleReadInputRegisters(pdu, function);

                case (byte)ModbusFunctionCode.ReadCoils:
                    return HandleReadCoils(pdu, function);

                case (byte)ModbusFunctionCode.ReadDiscreteInputs:
                    return HandleReadDiscreteInputs(pdu, function);

                case (byte)ModbusFunctionCode.WriteSingleRegister:
                    return HandleWriteSingleRegister(pdu, function);

                case (byte)ModbusFunctionCode.WriteSingleCoil:
                    return HandleWriteSingleCoil(pdu, function);

                case (byte)ModbusFunctionCode.WriteMultipleRegisters:
                    return HandleWriteMultipleRegisters(pdu, function);

                default:
                    return BuildException(function, 0x01); // Illegal Function
            }
        }

        private byte[] HandleReadHoldingRegisters(byte[] pdu, byte function)
        {
            if (pdu.Length != 5)
                return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);

            if (quantity is < 1 or > 125)
                return BuildException(function, 0x03);

            if (!_dataStore.IsValidRegisterRange(startAddress, quantity))
                return BuildException(function, 0x02); // Illegal Data Address

            List<byte> response = new(2 + quantity * 2)
            {
                function,
                (byte)(quantity * 2)
            };

            for (int i = 0; i < quantity; i++)
            {
                ushort value = _dataStore.GetHoldingRegister(startAddress + i);
                response.Add((byte)(value >> 8));
                response.Add((byte)value);
            }

            Log($"[Server] FC03: adres {startAddress}, adet {quantity} okundu.");
            return response.ToArray();
        }

        /// <summary>
        /// FC04. Holding Register okumayla aynı yapıdadır; tek farkı AYRI bir
        /// adres alanından (3x / Input Register) okumasıdır. Input register'lar
        /// salt okunurdur, bu yüzden yazma fonksiyonları onlara dokunamaz.
        /// </summary>
        private byte[] HandleReadInputRegisters(byte[] pdu, byte function)
        {
            if (pdu.Length != 5)
                return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);

            if (quantity is < 1 or > 125)
                return BuildException(function, 0x03);

            if (!_dataStore.IsValidInputRegisterRange(startAddress, quantity))
                return BuildException(function, 0x02); // Illegal Data Address

            List<byte> response = new(2 + quantity * 2)
            {
                function,
                (byte)(quantity * 2)
            };

            for (int i = 0; i < quantity; i++)
            {
                ushort value = _dataStore.GetInputRegister(startAddress + i);
                response.Add((byte)(value >> 8));
                response.Add((byte)value);
            }

            Log($"[Server] FC04: input register {startAddress}, adet {quantity} okundu.");
            return response.ToArray();
        }

        private byte[] HandleReadCoils(byte[] pdu, byte function)
        {
            if (pdu.Length != 5)
                return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);

            if (quantity is < 1 or > 2000)
                return BuildException(function, 0x03);

            if (!_dataStore.IsValidCoilRange(startAddress, quantity))
                return BuildException(function, 0x02);

            int byteCount = (quantity + 7) / 8;
            byte[] coilBytes = new byte[byteCount];

            for (int i = 0; i < quantity; i++)
            {
                if (_dataStore.GetCoil(startAddress + i))
                    coilBytes[i / 8] |= (byte)(1 << (i % 8));
            }

            List<byte> response = new(2 + byteCount) { function, (byte)byteCount };
            response.AddRange(coilBytes);
            Log($"[Server] FC01: adres {startAddress}, adet {quantity} okundu.");
            return response.ToArray();
        }

        private byte[] HandleReadDiscreteInputs(byte[] pdu, byte function)
        {
            if (pdu.Length != 5)
                return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);

            if (quantity is < 1 or > 2000)
                return BuildException(function, 0x03);

            if (!_dataStore.IsValidDiscreteInputRange(startAddress, quantity))
                return BuildException(function, 0x02);

            int byteCount = (quantity + 7) / 8;
            byte[] inputBytes = new byte[byteCount];

            for (int i = 0; i < quantity; i++)
            {
                if (_dataStore.GetDiscreteInput(startAddress + i))
                    inputBytes[i / 8] |= (byte)(1 << (i % 8));
            }

            List<byte> response = new(2 + byteCount)
            {
                function,
                (byte)byteCount
            };

            response.AddRange(inputBytes);
            Log($"[Server] FC02: discrete input {startAddress}, adet {quantity} okundu.");
            return response.ToArray();
        }

        private byte[] HandleWriteSingleRegister(byte[] pdu, byte function)
        {
            if (pdu.Length != 5)
                return BuildException(function, 0x03);

            ushort address = ReadUInt16(pdu, 1);
            ushort value = ReadUInt16(pdu, 3);

            if (!_dataStore.IsValidRegisterRange(address, 1))
                return BuildException(function, 0x02);

            // LiBat 40154 / protocol register 154 Modbus slave adresidir.
            // Yalnızca 1..247 kabul edilir.
            if (address == 154 && value is < 1 or > 247)
                return BuildException(function, 0x03);

            _dataStore.SetHoldingRegister(address, value);
            Log($"[Server] FC06: Register[{address}] = {value} yazıldı.");
            return pdu;
        }

        private byte[] HandleWriteSingleCoil(byte[] pdu, byte function)
        {
            if (pdu.Length != 5)
                return BuildException(function, 0x03);

            ushort address = ReadUInt16(pdu, 1);
            ushort rawValue = ReadUInt16(pdu, 3);

            if (rawValue != 0xFF00 && rawValue != 0x0000)
                return BuildException(function, 0x03);

            if (!_dataStore.IsValidCoilRange(address, 1))
                return BuildException(function, 0x02);

            bool value = rawValue == 0xFF00;
            _dataStore.SetCoil(address, value);
            Log($"[Server] FC05: Coil[{address}] = {value} yazıldı.");
            return pdu;
        }

        private byte[] HandleWriteMultipleRegisters(byte[] pdu, byte function)
        {
            // Function + Start Address(2) + Quantity(2) + Byte Count(1) + values
            if (pdu.Length < 8)
                return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);
            int byteCount = pdu[5];

            if (quantity is < 1 or > 123)
                return BuildException(function, 0x03);

            if (byteCount != quantity * 2)
                return BuildException(function, 0x03);

            if (pdu.Length != 6 + byteCount)
                return BuildException(function, 0x03);

            if (!_dataStore.IsValidRegisterRange(startAddress, quantity))
                return BuildException(function, 0x02);

            // Register 154 bu FC16 aralığındaysa geçersiz Unit ID değerini
            // herhangi bir register yazılmadan önce reddet.
            for (int i = 0; i < quantity; i++)
            {
                int targetAddress = startAddress + i;
                ushort candidateValue = ReadUInt16(pdu, 6 + (i * 2));

                if (targetAddress == 154 && candidateValue is < 1 or > 247)
                    return BuildException(function, 0x03);
            }

            for (int i = 0; i < quantity; i++)
            {
                ushort value = ReadUInt16(pdu, 6 + (i * 2));
                _dataStore.SetHoldingRegister(startAddress + i, value);
            }

            Log($"[Server] FC16: adres {startAddress}, adet {quantity} register yazıldı.");

            return new[]
            {
                function,
                (byte)(startAddress >> 8),
                (byte)startAddress,
                (byte)(quantity >> 8),
                (byte)quantity
            };
        }

        private static byte[] BuildException(byte function, byte exceptionCode)
        {
            return new[] { (byte)(function | 0x80), exceptionCode };
        }

        private static ushort ReadUInt16(byte[] data, int index)
        {
            return (ushort)((data[index] << 8) | data[index + 1]);
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count, CancellationToken token)
        {
            if (count <= 0) return Array.Empty<byte>();

            byte[] buffer = new byte[count];
            int read = 0;

            while (read < count)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read, count - read), token);
                if (n == 0) return Array.Empty<byte>();
                read += n;
            }

            return buffer;
        }

        private static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        private static string ToHex(byte[] data) => BitConverter.ToString(data).Replace("-", " ");

        private void Log(string message) => OnLog?.Invoke(message);
    }
}