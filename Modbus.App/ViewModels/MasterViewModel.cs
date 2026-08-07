using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using Modbus.App.Commands;
using Modbus.App.Models;
using Modbus.App.Profiles;
using Modbus.App.Services;
using Modbus.Communication.RTU;
using Modbus.Communication.Server;
using Modbus.Communication.TCP;
using Modbus.Core.Interfaces;
using Modbus.Protocol.Builders;
using Modbus.Protocol.Functions;
using Modbus.Protocol.Helpers;
using Modbus.Protocol.Packets;
using Modbus.Protocol.Parsers;

namespace Modbus.App.ViewModels
{
    /// <summary>
    /// MASTER / CLIENT ekranının beyni. Bir Modbus cihazına bağlanır, register/coil
    /// okur/yazar, sürekli poll eder; sonuçları Register/Bit Watch, Data Inspector,
    /// Communication Traffic ve sayaçlarla gösterir. Server/Slave kontrolü içermez.
    /// </summary>
    public sealed class MasterViewModel : ViewModelBase
    {
        private readonly PacketBuilder _builder = new();
        private readonly ResponseParser _parser = new();
        private readonly AddressTranslationService _addr = new();
        private readonly Dictionary<int, DeviceRegisterDefinition> _interp = new();
        private readonly LiBatDeviceProfile _statusProfile = new();

        private IModbusClient? _client;
        private ushort _transactionId;
        private DispatcherTimer? _pollTimer;
        private bool _requestInFlight;

        public MasterViewModel()
        {
            ConnectCommand = new RelayCommand(async () => await OnConnect(), () => !IsConnected);
            DisconnectCommand = new RelayCommand(async () => await OnDisconnect(), () => IsConnected);
            SendCommand = new RelayCommand(OnSend, () => IsConnected);
            TogglePollCommand = new RelayCommand(OnTogglePoll, () => IsConnected);
            RefreshComPortsCommand = new RelayCommand(RefreshComPorts);
            ClearLogCommand = new RelayCommand(() => LogEntries.Clear());

            // LiBat yorumlama sözlüğü (Register Watch'ta 40111 → "27.1 °C" gibi).
            foreach (DeviceRegisterDefinition d in _statusProfile.CreateRegisters())
                _interp[d.PduAddress] = d;

            foreach (StatusBitDefinition bit in _statusProfile.StatusBits)
                ActiveEvents.Add(new ActiveEventItem { Bit = bit.Bit, Severity = bit.Severity.ToString(), Description = bit.Description });

            RefreshComPorts();
            Log("INFO", "Application started in MASTER mode");
        }

        // ---------------- CONNECTION ----------------
        public string[] BaudRateOptions { get; } = { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" };
        public string[] DataBitsOptions { get; } = { "7", "8" };
        public string[] ParityOptions { get; } = { "None", "Even", "Odd", "Mark", "Space" };
        public string[] StopBitsOptions { get; } = { "One", "OnePointFive", "Two" };
        public ObservableCollection<string> AvailablePorts { get; } = new();

        private int _protocolIndex;
        public int ProtocolIndex { get => _protocolIndex; set { _protocolIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTcp)); OnPropertyChanged(nameof(IsRtu)); } }
        public bool IsTcp => ProtocolIndex == 0;
        public bool IsRtu => ProtocolIndex == 1;

        private string _ip = "127.0.0.1";
        public string Ip { get => _ip; set { _ip = value; OnPropertyChanged(); } }
        private string _port = "1502";
        public string Port { get => _port; set { _port = value; OnPropertyChanged(); } }
        private string _comPort = "COM3";
        public string ComPort { get => _comPort; set { _comPort = value; OnPropertyChanged(); } }
        private string _baud = "9600";
        public string Baud { get => _baud; set { _baud = value; OnPropertyChanged(); } }
        private string _dataBits = "8";
        public string DataBits { get => _dataBits; set { _dataBits = value; OnPropertyChanged(); } }
        private string _parity = "None";
        public string Parity { get => _parity; set { _parity = value; OnPropertyChanged(); } }
        private string _stopBits = "One";
        public string StopBits { get => _stopBits; set { _stopBits = value; OnPropertyChanged(); } }
        private string _timeout = "3000";
        public string Timeout { get => _timeout; set { _timeout = value; OnPropertyChanged(); } }

        private bool _isConnected;
        public bool IsConnected { get => _isConnected; set { _isConnected = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }

        private string _connectionStatus = "Disconnected";
        public string ConnectionStatus { get => _connectionStatus; set { _connectionStatus = value; OnPropertyChanged(); } }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RefreshComPortsCommand { get; }

        private void RefreshComPorts()
        {
            AvailablePorts.Clear();
            foreach (string p in ModbusRtuClient.GetAvailablePortNames())
                AvailablePorts.Add(p);
            if (AvailablePorts.Count > 0 && !AvailablePorts.Contains(ComPort))
                ComPort = AvailablePorts[0];
        }

        private async Task OnConnect()
        {
            try
            {
                ConnectionStatus = "Connecting";
                ResetCounters();

                if (IsTcp)
                {
                    var s = new TcpConnectionSettings { IpAddress = Ip.Trim(), Port = ParseInt(Port, 1, 65535, "Port"), Timeout = ParseInt(Timeout, 100, 120000, "Timeout") };
                    _client = new ModbusTcpClient(s);
                    await _client.ConnectAsync();
                    ConnectionStatus = "Connected";
                    Log("INFO", $"Connected to {s.IpAddress}:{s.Port}");
                }
                else
                {
                    var s = new RtuConnectionSettings
                    {
                        PortName = ComPort.Trim(),
                        BaudRate = ParseInt(Baud, 1, 2_000_000, "Baud"),
                        DataBits = ParseInt(DataBits, 5, 8, "Data Bits"),
                        Parity = ParseEnum<RtuParity>(Parity),
                        StopBits = ParseEnum<RtuStopBits>(StopBits),
                        Timeout = ParseInt(Timeout, 100, 120000, "Timeout")
                    };
                    _client = new ModbusRtuClient(s);
                    await _client.ConnectAsync();
                    ConnectionStatus = "COM Port Open — Device Not Verified";
                    Log("INFO", $"COM port {s.PortName} opened @ {s.BaudRate}. İlk RX cevabına kadar doğrulanmadı.");
                }

                IsConnected = true;
            }
            catch (Exception ex)
            {
                ConnectionStatus = "Connection Error";
                Log("ERROR", "Connect failed: " + ex.Message);
                await SafeDisconnect();
            }
        }

        private async Task OnDisconnect()
        {
            StopPolling();
            await SafeDisconnect();
            ConnectionStatus = "Disconnected";
            Log("INFO", "Disconnected");
        }

        private async Task SafeDisconnect()
        {
            if (_client != null)
            {
                try { await _client.DisconnectAsync(); } catch { }
                _client = null;
            }
            IsConnected = false;
        }

        // ---------------- REQUEST ----------------
        private string _unitId = "1";
        public string UnitId { get => _unitId; set { _unitId = value; OnPropertyChanged(); } }

        // 0=FC01,1=FC03,2=FC04,3=FC06,4=FC02,5=FC05,6=FC16  (SIRA KORUNUR)
        private int _functionIndex = 1;
        public int FunctionIndex { get => _functionIndex; set { _functionIndex = value; OnPropertyChanged(); } }

        private string _address = "40111";
        public string Address { get => _address; set { _address = value; OnPropertyChanged(); } }
        private string _quantity = "1";
        public string Quantity { get => _quantity; set { _quantity = value; OnPropertyChanged(); } }
        private string _writeValue = "0";
        public string WriteValue { get => _writeValue; set { _writeValue = value; OnPropertyChanged(); } }
        private string _writeValues = "100, 200, 300";
        public string WriteValues { get => _writeValues; set { _writeValues = value; OnPropertyChanged(); } }

        public ICommand SendCommand { get; }

        private async void OnSend()
        {
            if (_requestInFlight) return;
            _requestInFlight = true;
            try { await SendCore(); }
            finally { _requestInFlight = false; }
        }

        private async Task SendCore()
        {
            if (_client == null || !_client.IsConnected) { Log("WARN", "Önce bağlanın."); return; }

            try
            {
                byte unit = (byte)ParseInt(UnitId, 0, 247, "Unit ID");
                int entered = ParseInt(Address, 0, 65535, "Address");
                ushort pdu = (ushort)_addr.PduFrom(entered);
                int logical = _addr.ToLogical(pdu);

                ModbusFunctionCode fc = FunctionIndex switch
                {
                    0 => ModbusFunctionCode.ReadCoils,
                    1 => ModbusFunctionCode.ReadHoldingRegisters,
                    2 => ModbusFunctionCode.ReadInputRegisters,
                    3 => ModbusFunctionCode.WriteSingleRegister,
                    4 => ModbusFunctionCode.ReadDiscreteInputs,
                    5 => ModbusFunctionCode.WriteSingleCoil,
                    6 => ModbusFunctionCode.WriteMultipleRegisters,
                    _ => ModbusFunctionCode.ReadHoldingRegisters
                };

                bool isRegRead = fc is ModbusFunctionCode.ReadHoldingRegisters or ModbusFunctionCode.ReadInputRegisters;
                bool isBitRead = fc is ModbusFunctionCode.ReadCoils or ModbusFunctionCode.ReadDiscreteInputs;

                ushort qty = 1, wval = 0;
                bool coil = false;
                ushort[] many = Array.Empty<ushort>();

                if (isRegRead || isBitRead) qty = (ushort)ParseInt(Quantity, 1, 2000, "Quantity");
                else if (fc == ModbusFunctionCode.WriteSingleRegister) wval = (ushort)ParseInt(WriteValue, 0, 65535, "Write Value");
                else if (fc == ModbusFunctionCode.WriteSingleCoil) coil = ParseCoil(WriteValue);
                else if (fc == ModbusFunctionCode.WriteMultipleRegisters) { many = ParseList(WriteValues); qty = (ushort)many.Length; }

                byte[] reqPdu = fc switch
                {
                    ModbusFunctionCode.ReadCoils or ModbusFunctionCode.ReadDiscreteInputs or
                    ModbusFunctionCode.ReadHoldingRegisters or ModbusFunctionCode.ReadInputRegisters
                        => _builder.BuildReadPdu(fc, pdu, qty),
                    ModbusFunctionCode.WriteSingleCoil => _builder.BuildWriteSinglePdu(fc, pdu, coil ? (ushort)0xFF00 : (ushort)0x0000),
                    ModbusFunctionCode.WriteSingleRegister => _builder.BuildWriteSinglePdu(fc, pdu, wval),
                    ModbusFunctionCode.WriteMultipleRegisters => _builder.BuildWriteMultipleRegistersPdu(pdu, many),
                    _ => throw new InvalidOperationException("Desteklenmeyen fonksiyon.")
                };

                bool tcp = IsTcp;
                byte[] request = tcp ? _builder.WrapTcp(++_transactionId, unit, reqPdu) : _builder.WrapRtu(unit, reqPdu);

                FrameAnalyzer.Fill(RequestBytes, request, tcp, isResponse: false);
                TxCount++;
                ConnectionStatus = "Waiting Response";
                Log("TX", $"FC{(byte)fc:00} Address={logical} " + (isRegRead || isBitRead ? $"Quantity={qty}" : $"Value={(fc == ModbusFunctionCode.WriteSingleCoil ? (coil ? "ON" : "OFF") : (fc == ModbusFunctionCode.WriteMultipleRegisters ? string.Join(",", many) : wval.ToString()))}"));

                byte[] response = await _client.SendAsync(request);

                RxCount++;
                FrameAnalyzer.Fill(ResponseBytes, response, tcp, isResponse: true);
                ConnectionStatus = "Device Responded";

                ModbusPacket packet = tcp ? _parser.ParseTcpResponse(response) : _parser.ParseRtuResponse(response);

                if (_parser.IsErrorResponse(packet))
                {
                    byte code = packet.Data.Length > 0 ? packet.Data[0] : (byte)0;
                    ConnectionStatus = "Modbus Exception";
                    Log("ERROR", $"Modbus Exception: FC 0x{packet.FunctionCode:X2}, code 0x{code:X2}");
                    return;
                }

                if (isRegRead)
                {
                    ushort[] values = _parser.ReadRegisterValues(packet);
                    UpdateRegisterWatch(pdu, values);
                    DecodeStatusIfPresent(pdu, values);
                    Log("RX", $"Read {values.Length} register: {string.Join(", ", values)}");
                    if (values.Length > 0 && _interp.TryGetValue(pdu, out var d0))
                        Log("INFO", $"{logical} {d0.Name} = {BuildRow(pdu, values[0]).DisplayValue}");
                }
                else if (isBitRead)
                {
                    bool[] bits = _parser.ReadBitValues(packet, qty);
                    UpdateBitWatch(pdu, bits, fc == ModbusFunctionCode.ReadDiscreteInputs ? "Discrete Input" : "Coil");
                    Log("RX", $"Read {bits.Length} bit: {string.Join(",", bits.Select(b => b ? "1" : "0"))}");
                }
                else
                {
                    Log("RX", "Write onaylandı.");
                }
            }
            catch (TimeoutException ex)
            {
                TimeoutCount++;
                ConnectionStatus = "Timeout";
                Log("ERROR", "Timeout: " + ex.Message);
            }
            catch (Exception ex)
            {
                ErrCount++;
                ConnectionStatus = ex.Message.Contains("CRC", StringComparison.OrdinalIgnoreCase) ? "CRC Error" : "Connection Error";
                Log("ERROR", "Send error: " + ex.Message);
            }
        }

        // ---------------- POLLING ----------------
        private bool _isPolling;
        public bool IsPolling { get => _isPolling; set { _isPolling = value; OnPropertyChanged(); OnPropertyChanged(nameof(PollButtonText)); } }
        public string PollButtonText => _isPolling ? "STOP POLL" : "POLL";
        private string _scanRate = "1000";
        public string ScanRate { get => _scanRate; set { _scanRate = value; OnPropertyChanged(); } }
        public ICommand TogglePollCommand { get; }

        private void OnTogglePoll()
        {
            if (_isPolling) { StopPolling(); return; }
            if (_client == null || !_client.IsConnected) { Log("WARN", "Poll için önce bağlanın."); return; }
            int scan;
            try { scan = ParseInt(ScanRate, 50, 600000, "Scan"); } catch (Exception ex) { Log("ERROR", ex.Message); return; }
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(scan) };
            _pollTimer.Tick += (_, _) => { if (!_requestInFlight && _client?.IsConnected == true) OnSend(); else if (_client?.IsConnected != true) StopPolling(); };
            _pollTimer.Start();
            IsPolling = true;
            Log("INFO", $"Polling başladı ({scan} ms).");
            OnSend();
        }

        private void StopPolling()
        {
            if (_pollTimer != null) { _pollTimer.Stop(); _pollTimer = null; }
            if (_isPolling) { IsPolling = false; Log("INFO", "Polling durduruldu."); }
        }

        // ---------------- WATCH / INSPECTOR ----------------
        public ObservableCollection<DeviceRegisterDefinition> ClientRegisters { get; } = new();
        public ObservableCollection<BitItem> ClientBits { get; } = new();
        public ObservableCollection<PacketFieldItem> InspectorFields { get; } = new();

        private DeviceRegisterDefinition? _selectedRegister;
        public DeviceRegisterDefinition? SelectedRegister { get => _selectedRegister; set { _selectedRegister = value; OnPropertyChanged(); RefreshInspector(); } }

        private void UpdateRegisterWatch(int startPdu, ushort[] values)
        {
            ClientRegisters.Clear();
            for (int i = 0; i < values.Length; i++)
                ClientRegisters.Add(BuildRow(startPdu + i, values[i]));
            if (ClientRegisters.Count > 0) SelectedRegister = ClientRegisters[0];
        }

        private DeviceRegisterDefinition BuildRow(int pdu, ushort raw)
        {
            if (_interp.TryGetValue(pdu, out DeviceRegisterDefinition? t))
            {
                DeviceRegisterDefinition row = t.Clone();
                row.RawValue = raw;
                return row;
            }
            return new DeviceRegisterDefinition
            {
                PduAddress = pdu,
                LogicalAddress = _addr.ToLogical(pdu),
                Name = $"Register {_addr.ToLogical(pdu)}",
                DataType = "UInt16",
                RawValue = raw
            };
        }

        private void UpdateBitWatch(int startPdu, bool[] bits, string type)
        {
            ClientBits.Clear();
            for (int i = 0; i < bits.Length; i++)
                ClientBits.Add(new BitItem(startPdu + i, type, $"{type} {_addr.ToLogical(startPdu + i)}", bits[i]));
        }

        private void RefreshInspector()
        {
            InspectorFields.Clear();
            if (SelectedRegister == null) return;

            int idx = ClientRegisters.IndexOf(SelectedRegister);
            ushort[] all = ClientRegisters.Select(r => r.RawValue).ToArray();
            ushort raw = SelectedRegister.RawValue;

            Add("Unsigned (UInt16)", raw.ToString());
            Add("Signed (Int16)", unchecked((short)raw).ToString());
            Add("Hex", DataConverter.ToHex(raw));
            Add("Binary", DataConverter.ToBinary(raw));

            if (idx >= 0 && idx + 1 < all.Length)
            {
                ushort[] two = { all[idx], all[idx + 1] };
                Add("UInt32 (ABCD)", DataConverter.ToUInt32(two, RegisterByteOrder.ABCD).ToString());
                Add("Int32 (ABCD)", DataConverter.ToInt32(two, RegisterByteOrder.ABCD).ToString());
                Add("Float32 (ABCD)", DataConverter.Format(DataConverter.ToFloat32(two, RegisterByteOrder.ABCD)));
            }
            if (idx >= 0 && idx + 3 < all.Length)
            {
                ushort[] four = { all[idx], all[idx + 1], all[idx + 2], all[idx + 3] };
                Add("Double64 (ABCD)", DataConverter.Format(DataConverter.ToDouble64(four, RegisterByteOrder.ABCD)));
            }
            Add("Display", SelectedRegister.DisplayValue);

            void Add(string f, string v) => InspectorFields.Add(new PacketFieldItem { Field = f, Value = v, Description = SelectedRegister!.Name });
        }

        // ---------------- STATUS / ACTIVE EVENTS ----------------
        public ObservableCollection<ActiveEventItem> ActiveEvents { get; } = new();
        private string _statusSummary = "Battery Status için 40114..40117 oku.";
        public string StatusSummary { get => _statusSummary; set { _statusSummary = value; OnPropertyChanged(); } }

        private void DecodeStatusIfPresent(int startPdu, ushort[] values)
        {
            int[] pdus = _statusProfile.StatusRegisterPduAddresses; // 114..117
            int first = pdus[0] - startPdu;
            if (first < 0 || first + 3 >= values.Length) return;

            ulong status = _statusProfile.CombineStatus(new[] { values[first], values[first + 1], values[first + 2], values[first + 3] });
            int active = 0;
            foreach (ActiveEventItem ev in ActiveEvents)
            {
                bool on = (status & (1UL << ev.Bit)) != 0;
                ev.Active = on;
                if (on) active++;
            }
            StatusSummary = active == 0 ? $"Aktif olay yok (0x{status:X16})" : $"{active} aktif olay (0x{status:X16})";
        }

        // ---------------- TRAFFIC / LOG / COUNTERS ----------------
        public ObservableCollection<FrameByteItem> RequestBytes { get; } = new();
        public ObservableCollection<FrameByteItem> ResponseBytes { get; } = new();
        public ObservableCollection<string> LogEntries { get; } = new();
        public ICommand ClearLogCommand { get; }

        private int _tx, _rx, _err, _to;
        public int TxCount { get => _tx; set { _tx = value; OnPropertyChanged(); } }
        public int RxCount { get => _rx; set { _rx = value; OnPropertyChanged(); } }
        public int ErrCount { get => _err; set { _err = value; OnPropertyChanged(); } }
        public int TimeoutCount { get => _to; set { _to = value; OnPropertyChanged(); } }
        private void ResetCounters() { TxCount = RxCount = ErrCount = TimeoutCount = 0; }

        private void Log(string level, string message)
            => RunOnUi(() => { LogEntries.Add($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}"); while (LogEntries.Count > 800) LogEntries.RemoveAt(0); });

        // ---------------- HELPERS ----------------
        private static int ParseInt(string t, int min, int max, string f)
        {
            if (!int.TryParse(t, out int v)) throw new ArgumentException($"{f} sayı olmalı.");
            if (v < min || v > max) throw new ArgumentOutOfRangeException(f, $"{f} {min}..{max} olmalı.");
            return v;
        }
        private static TEnum ParseEnum<TEnum>(string t) where TEnum : struct, Enum => Enum.TryParse(t, out TEnum v) ? v : default;
        private static bool ParseCoil(string t)
        {
            t = (t ?? "").Trim().ToUpperInvariant();
            return t is "1" or "ON" or "TRUE" or "AÇIK" or "ACIK";
        }
        private static ushort[] ParseList(string t)
            => (t ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => ushort.TryParse(x.Trim(), out ushort v) ? v : (ushort)0).ToArray();

        private static void RunOnUi(Action a) { var d = Application.Current?.Dispatcher; if (d != null && !d.CheckAccess()) d.Invoke(a); else a(); }
    }

    internal static class AddressExtensions
    {
        public static int PduFrom(this AddressTranslationService s, int entered) => s.ToPdu(entered);
    }
}
