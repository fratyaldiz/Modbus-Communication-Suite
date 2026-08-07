using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

using Modbus.App.Commands;
using Modbus.App.Models;
using Modbus.App.Profiles;
using Modbus.App.Services;
using Modbus.Communication.RTU;
using Modbus.Communication.Server;

namespace Modbus.App.ViewModels
{
    /// <summary>
    /// SLAVE / SERVER ekranının beyni. Sanal cihazı simüle eder: dinamik register
    /// hafızası (Add/Edit/Delete/Load Profile/Clear), cihaz profilleri, Battery Status
    /// olay çözümlemesi, TCP/RTU server yaşam döngüsü, byte-byte trafik ve profesyonel log.
    /// Register hafızası tek doğruluk kaynağıdır (RegisterMemoryService + ModbusDataStore).
    /// </summary>
    public sealed class SlaveViewModel : ViewModelBase
    {
        public RegisterMemoryService Memory { get; } = new();

        private ModbusTcpServer? _tcpServer;
        private ModbusRtuServer? _rtuServer;

        public SlaveViewModel()
        {
            Profiles = new ObservableCollection<IDeviceProfile>
            {
                new LiBatDeviceProfile(),
                new EmptyDeviceProfile()
            };

            LoadProfileCommand = new RelayCommand(LoadSelectedProfile);
            ClearCustomCommand = new RelayCommand(() =>
            {
                Memory.ClearCustom();
                RebuildStatusEvents();
                Log("INFO", "Kullanıcı register'ları temizlendi.");
            });
            RefreshComPortsCommand = new RelayCommand(RefreshComPorts);
            StartServerCommand = new RelayCommand(StartServer, () => !IsRunning);
            StopServerCommand = new RelayCommand(StopServer, () => IsRunning);
            ClearLogCommand = new RelayCommand(() => LogEntries.Clear());

            ActiveEventsView = CollectionViewSource.GetDefaultView(ActiveEvents);
            ActiveEventsView.Filter = o => !ShowActiveOnly || (o is ActiveEventItem ev && ev.Active);

            Memory.DataStore.HoldingRegisterChanged += OnStoreHoldingChanged;

            // Ayarlardan son profili seç.
            string last = Modbus.App.App.Settings.Current.LastDeviceProfile;
            SelectedProfile = Profiles.FirstOrDefault(p => p.Name == last) ?? Profiles[0];

            RefreshComPorts();
            LoadSelectedProfile();

            Log("INFO", "Application started in SLAVE mode");
        }

        // ============================================================
        // PROFİL
        // ============================================================
        public ObservableCollection<IDeviceProfile> Profiles { get; }

        private IDeviceProfile _selectedProfile = null!;
        public IDeviceProfile SelectedProfile
        {
            get => _selectedProfile;
            set { _selectedProfile = value; OnPropertyChanged(); }
        }

        public ICommand LoadProfileCommand { get; }
        public ICommand ClearCustomCommand { get; }

        private void LoadSelectedProfile()
        {
            if (SelectedProfile == null) return;

            Memory.LoadProfile(SelectedProfile);
            Modbus.App.App.Settings.Current.LastDeviceProfile = SelectedProfile.Name;
            Modbus.App.App.Settings.Save();

            RebuildStatusEvents();
            Log("INFO", $"{SelectedProfile.Name} profili yüklendi ({Memory.Registers.Count} register).");
        }

        // ============================================================
        // REGISTER SEÇİMİ
        // ============================================================
        private DeviceRegisterDefinition? _selectedRegister;
        public DeviceRegisterDefinition? SelectedRegister
        {
            get => _selectedRegister;
            set { _selectedRegister = value; OnPropertyChanged(); }
        }

        // ============================================================
        // BAĞLANTI / SERVER AYARLARI
        // ============================================================
        public string[] BaudRateOptions { get; } = { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" };
        public string[] DataBitsOptions { get; } = { "7", "8" };
        public string[] ParityOptions { get; } = { "None", "Even", "Odd", "Mark", "Space" };
        public string[] StopBitsOptions { get; } = { "One", "OnePointFive", "Two" };

        public ObservableCollection<string> AvailablePorts { get; } = new();

        private int _protocolIndex;              // 0=TCP, 1=RTU
        public int ProtocolIndex { get => _protocolIndex; set { _protocolIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTcp)); OnPropertyChanged(nameof(IsRtu)); } }
        public bool IsTcp => ProtocolIndex == 0;
        public bool IsRtu => ProtocolIndex == 1;

        private string _listenPort = "1502";
        public string ListenPort { get => _listenPort; set { _listenPort = value; OnPropertyChanged(); } }

        private string _comPort = "COM3";
        public string ComPort { get => _comPort; set { _comPort = value; OnPropertyChanged(); } }

        private string _baudRate = "9600";
        public string BaudRate { get => _baudRate; set { _baudRate = value; OnPropertyChanged(); } }

        private string _dataBits = "8";
        public string DataBits { get => _dataBits; set { _dataBits = value; OnPropertyChanged(); } }

        private string _parity = "None";
        public string Parity { get => _parity; set { _parity = value; OnPropertyChanged(); } }

        private string _stopBits = "One";
        public string StopBits { get => _stopBits; set { _stopBits = value; OnPropertyChanged(); } }

        private string _unitId = "1";
        public string UnitId { get => _unitId; set { _unitId = value; OnPropertyChanged(); } }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }

        private string _serverStatus = "Stopped";
        public string ServerStatus { get => _serverStatus; set { _serverStatus = value; OnPropertyChanged(); } }

        public ICommand RefreshComPortsCommand { get; }
        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand ClearLogCommand { get; }

        private void RefreshComPorts()
        {
            AvailablePorts.Clear();
            foreach (string p in ModbusRtuClient.GetAvailablePortNames())
                AvailablePorts.Add(p);
            if (AvailablePorts.Count > 0 && !AvailablePorts.Contains(ComPort))
                ComPort = AvailablePorts[0];
        }

        private void StartServer()
        {
            try
            {
                ServerStatus = "Starting";

                if (IsTcp)
                {
                    int port = ParseInt(ListenPort, 1, 65535, "Listen Port");
                    _tcpServer = new ModbusTcpServer(Memory.DataStore, port);
                    _tcpServer.OnLog += OnServerLog;
                    _tcpServer.Start();
                    ServerStatus = $"Listening :{port}";
                    Log("INFO", $"TCP Slave listening on port {port}");
                }
                else
                {
                    var settings = new RtuConnectionSettings
                    {
                        PortName = ComPort.Trim(),
                        BaudRate = ParseInt(BaudRate, 1, 2_000_000, "Baud"),
                        DataBits = ParseInt(DataBits, 5, 8, "Data Bits"),
                        Parity = ParseEnum<RtuParity>(Parity),
                        StopBits = ParseEnum<RtuStopBits>(StopBits),
                        Timeout = 3000
                    };
                    byte unit = (byte)ParseInt(UnitId, 1, 247, "Unit ID");
                    _rtuServer = new ModbusRtuServer(Memory.DataStore, settings, unit);
                    _rtuServer.OnLog += OnServerLog;
                    _rtuServer.Start();
                    ServerStatus = $"RTU Port Open ({settings.PortName})";
                    Log("INFO", $"RTU Slave listening on {settings.PortName} @ {settings.BaudRate}, Unit {unit}");
                }

                if (SelectedProfile != null)
                    Log("INFO", $"{SelectedProfile.Name} profile loaded");

                IsRunning = true;
            }
            catch (Exception ex)
            {
                ServerStatus = "Error";
                Log("ERROR", "Server start failed: " + ex.Message);
                CleanupServers();
            }
        }

        private void StopServer()
        {
            try
            {
                CleanupServers();
                IsRunning = false;
                ServerStatus = "Stopped";
                Log("INFO", "Server stopped");
            }
            catch (Exception ex)
            {
                Log("ERROR", "Server stop failed: " + ex.Message);
            }
        }

        private void CleanupServers()
        {
            if (_tcpServer != null) { _tcpServer.OnLog -= OnServerLog; _tcpServer.Stop(); _tcpServer = null; }
            if (_rtuServer != null) { _rtuServer.OnLog -= OnServerLog; _rtuServer.Stop(); _rtuServer = null; }
        }

        // Server arka planından gelen loglar → status + trafik + kayıt.
        private void OnServerLog(string message)
        {
            RunOnUi(() =>
            {
                if (message.Contains("RX]"))
                {
                    byte[] frame = ParseHex(message);
                    FrameAnalyzer.Fill(RequestBytes, frame, IsTcp, isResponse: false);
                    LastRequestSummary = SummarizeRequest(frame);
                    ServerStatus = "Request Received";
                    Log("RX", LastRequestSummary);
                }
                else if (message.Contains("TX]"))
                {
                    byte[] frame = ParseHex(message);
                    FrameAnalyzer.Fill(ResponseBytes, frame, IsTcp, isResponse: true);
                    ServerStatus = "Response Sent";
                    Log("TX", "Response " + ToHex(frame));
                }
                else if (message.Contains("Client bağlandı"))
                {
                    ServerStatus = "Client Connected";
                    Log("INFO", message);
                }
                else
                {
                    Log("INFO", message);
                }
            });
        }

        // ============================================================
        // STATUS / ACTIVE EVENTS (profil tabanlı, hard-code DEĞİL)
        // ============================================================
        public ObservableCollection<ActiveEventItem> ActiveEvents { get; } = new();
        public ICollectionView ActiveEventsView { get; }

        public bool HasStatus => SelectedProfile?.HasStatus == true;

        private bool _showActiveOnly;
        public bool ShowActiveOnly { get => _showActiveOnly; set { _showActiveOnly = value; OnPropertyChanged(); ActiveEventsView.Refresh(); } }

        private string _statusSummary = "—";
        public string StatusSummary { get => _statusSummary; set { _statusSummary = value; OnPropertyChanged(); } }

        private void RebuildStatusEvents()
        {
            ActiveEvents.Clear();
            OnPropertyChanged(nameof(HasStatus));

            if (SelectedProfile?.HasStatus != true)
            {
                StatusSummary = "Bu profilde status tanımı yok.";
                return;
            }

            foreach (StatusBitDefinition bit in SelectedProfile.StatusBits)
            {
                ActiveEvents.Add(new ActiveEventItem
                {
                    Bit = bit.Bit,
                    Severity = bit.Severity.ToString(),
                    Description = bit.Description
                });
            }

            RefreshStatus();
        }

        private void OnStoreHoldingChanged(int address, ushort value)
        {
            if (SelectedProfile?.HasStatus != true) return;
            if (Array.IndexOf(SelectedProfile.StatusRegisterPduAddresses, address) < 0) return;
            RunOnUi(RefreshStatus);
        }

        private void RefreshStatus()
        {
            if (SelectedProfile?.HasStatus != true) return;

            var regs = SelectedProfile.StatusRegisterPduAddresses
                .Select(pdu => Memory.DataStore.HoldingRegisters[pdu])
                .ToList();

            ulong status = SelectedProfile.CombineStatus(regs);

            int activeCount = 0;
            foreach (ActiveEventItem ev in ActiveEvents)
            {
                bool active = (status & (1UL << ev.Bit)) != 0;
                ev.Active = active;
                if (active) activeCount++;
            }

            StatusSummary = activeCount == 0
                ? $"Aktif olay yok  (0x{status:X16})"
                : $"{activeCount} aktif olay  (0x{status:X16})";

            ActiveEventsView.Refresh();
        }

        // ============================================================
        // TRAFİK / LOG
        // ============================================================
        public ObservableCollection<FrameByteItem> RequestBytes { get; } = new();
        public ObservableCollection<FrameByteItem> ResponseBytes { get; } = new();
        public ObservableCollection<string> LogEntries { get; } = new();

        private string _lastRequestSummary = "—";
        public string LastRequestSummary { get => _lastRequestSummary; set { _lastRequestSummary = value; OnPropertyChanged(); } }

        private string SummarizeRequest(byte[] frame)
        {
            if (frame == null || frame.Length < (IsTcp ? 8 : 2)) return "—";
            int fcIndex = IsTcp ? 7 : 1;
            int unit = IsTcp ? frame[6] : frame[0];
            byte fc = frame[fcIndex];
            if (frame.Length >= fcIndex + 3)
            {
                int addr = (frame[fcIndex + 1] << 8) | frame[fcIndex + 2];
                int logical = SelectedProfile != null ? SelectedProfile.AddressBase + addr : addr;
                int qty = frame.Length >= fcIndex + 5 ? ((frame[fcIndex + 3] << 8) | frame[fcIndex + 4]) : 0;
                return $"Unit={unit} FC{fc:00} Address={logical} Quantity={qty}";
            }
            return $"Unit={unit} FC{fc:00}";
        }

        private void Log(string level, string message)
            => RunOnUi(() =>
            {
                LogEntries.Add($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
                while (LogEntries.Count > 800) LogEntries.RemoveAt(0);
            });

        // ============================================================
        // YARDIMCILAR
        // ============================================================
        private static string ToHex(byte[] d) => d == null || d.Length == 0 ? "(boş)" : BitConverter.ToString(d).Replace("-", " ");

        private static byte[] ParseHex(string logLine)
        {
            int idx = logLine.IndexOf(']');
            string hex = idx >= 0 ? logLine[(idx + 1)..] : logLine;
            var bytes = new List<byte>();
            foreach (string tok in hex.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (byte.TryParse(tok, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    bytes.Add(b);
            return bytes.ToArray();
        }

        private static int ParseInt(string text, int min, int max, string field)
        {
            if (!int.TryParse(text, out int v)) throw new ArgumentException($"{field} sayı olmalı.");
            if (v < min || v > max) throw new ArgumentOutOfRangeException(field, $"{field} {min}..{max} olmalı.");
            return v;
        }

        private static TEnum ParseEnum<TEnum>(string text) where TEnum : struct, Enum
            => Enum.TryParse(text, out TEnum v) ? v : default;

        private static void RunOnUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d != null && !d.CheckAccess()) d.Invoke(action); else action();
        }
    }
}
