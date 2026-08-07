using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Modbus.App.Commands;
using Modbus.App.Models;
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
    /// Tek ekranda bağımsız Client ve Server yönetimi, görünür register tabloları
    /// ve ham/çözümlenmiş paket takibi sağlar.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        private readonly PacketBuilder _builder = new();
        private readonly ResponseParser _parser = new();
        private readonly ModbusDataStore _dataStore = new(registerCount: 256, bitCount: 100);

        private IModbusClient? _client;
        private ModbusTcpServer? _server;
        private ModbusRtuServer? _rtuServer;   // RTU (seri) slave — kart Master olursa
        private ushort _transactionId;

        // Sürekli okuma (polling) ve aynı anda tek istek güvencesi
        private System.Windows.Threading.DispatcherTimer? _pollTimer;
        private bool _requestInFlight;   // bir istek işlenirken yenisini başlatma (overlap engeli)

        public MainViewModel()
        {
            ConnectClientCommand = new RelayCommand(OnConnectClient);
            DisconnectClientCommand = new RelayCommand(OnDisconnectClient);
            RefreshComPortsCommand = new RelayCommand(OnRefreshComPorts);
            StartServerCommand = new RelayCommand(OnStartServer);
            StopServerCommand = new RelayCommand(OnStopServer);
            SendCommand = new RelayCommand(OnSend);
            TogglePollCommand = new RelayCommand(OnTogglePoll);
            ClearLogCommand = new RelayCommand(() => LogEntries.Clear());
            ResetColorsCommand = new RelayCommand(OnResetColors);

            CreateServerRegisterRows();
            CreateServerBitRows();

            _dataStore.HoldingRegisterChanged += OnHoldingRegisterChanged;
            _dataStore.InputRegisterChanged += OnInputRegisterChanged;
            _dataStore.CoilChanged += OnCoilChanged;
            _dataStore.DiscreteInputChanged += OnDiscreteInputChanged;

            // Açılışta Data Inspector boş kalmasın.
            SelectedServerRegister =
                ServerRegisters.Count > 0
                    ? ServerRegisters[0]
                    : null;

            AddLog(
                "Uygulama hazır. Server'ı başlatın veya bir Modbus cihazına bağlanın.");

            OnRefreshComPorts();
        }

        // ================================================================
        // CLIENT AYARLARI
        // ================================================================

        private int _clientProtocolIndex;

        public int ClientProtocolIndex
        {
            get => _clientProtocolIndex;

            set
            {
                _clientProtocolIndex = value;
                OnPropertyChanged();
            }
        }

        private string _clientIpAddress = "127.0.0.1";

        public string ClientIpAddress
        {
            get => _clientIpAddress;

            set
            {
                _clientIpAddress = value;
                OnPropertyChanged();
            }
        }

        private string _clientPort = "1502";

        public string ClientPort
        {
            get => _clientPort;

            set
            {
                _clientPort = value;
                OnPropertyChanged();
            }
        }

        private string _clientComPort = "COM3";

        public string ClientComPort
        {
            get => _clientComPort;

            set
            {
                _clientComPort = value;
                OnPropertyChanged();
            }
        }

        private string _clientBaudRate = "9600";

        public string ClientBaudRate
        {
            get => _clientBaudRate;

            set
            {
                _clientBaudRate = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Bilgisayarda o anda görünen COM portları.</summary>
        public ObservableCollection<string> AvailableComPorts { get; } = new();

        public string[] BaudRateOptions { get; } =
        {
            "1200", "2400", "4800", "9600", "19200", "38400",
            "57600", "115200", "230400", "460800", "921600"
        };

        public string[] DataBitsOptions { get; } = { "7", "8" };

        public string[] ParityOptions { get; } =
        {
            "None", "Even", "Odd", "Mark", "Space"
        };

        public string[] StopBitsOptions { get; } =
        {
            "One", "OnePointFive", "Two"
        };

        private string _clientDataBits = "8";

        public string ClientDataBits
        {
            get => _clientDataBits;
            set
            {
                _clientDataBits = value;
                OnPropertyChanged();
            }
        }

        private string _clientParity = "None";

        public string ClientParity
        {
            get => _clientParity;
            set
            {
                _clientParity = value;
                OnPropertyChanged();
            }
        }

        private string _clientStopBits = "One";

        public string ClientStopBits
        {
            get => _clientStopBits;
            set
            {
                _clientStopBits = value;
                OnPropertyChanged();
            }
        }

        private string _clientTimeout = "3000";

        public string ClientTimeout
        {
            get => _clientTimeout;
            set
            {
                _clientTimeout = value;
                OnPropertyChanged();
            }
        }

        private string _clientStatus = "Bağlı değil";

        public string ClientStatus
        {
            get => _clientStatus;

            set
            {
                if (_clientStatus == value)
                    return;

                _clientStatus = value;
                OnPropertyChanged();
            }
        }

        // ================================================================
        // SERVER AYARLARI
        // ================================================================

        private string _serverPort = "1502";

        public string ServerPort
        {
            get => _serverPort;

            set
            {
                _serverPort = value;
                OnPropertyChanged();
            }
        }

        private string _serverStatus = "Durduruldu";

        /// <summary>
        /// Server'ın TCP portunu dinleyip dinlemediğini gösterir.
        /// Örnek: Durduruldu veya Çalışıyor :1502.
        /// </summary>
        public string ServerStatus
        {
            get => _serverStatus;

            set
            {
                if (_serverStatus == value)
                    return;

                _serverStatus = value;
                OnPropertyChanged();
            }
        }

        private string _serverClientStatus = "İstemci bağlı değil";

        /// <summary>
        /// Server'ın gerçekten kabul ettiği client bağlantısını gösterir.
        /// Örnek: Bağlı: 127.0.0.1:52341.
        /// </summary>
        public string ServerClientStatus
        {
            get => _serverClientStatus;

            set
            {
                if (_serverClientStatus == value)
                    return;

                _serverClientStatus = value;
                OnPropertyChanged();
            }
        }

        private bool _isServerClientConnected;

        /// <summary>
        /// XAML'deki kırmızı/yeşil bağlantı lambasını kontrol eder.
        /// </summary>
        public bool IsServerClientConnected
        {
            get => _isServerClientConnected;

            set
            {
                if (_isServerClientConnected == value)
                    return;

                _isServerClientConnected = value;
                OnPropertyChanged();
            }
        }

        // -------- Server protokolü ve RTU slave ayarları --------
        // 0 = TCP (Ethernet), 1 = RTU (seri port / kart Master ise)
        private int _serverProtocolIndex;

        public int ServerProtocolIndex
        {
            get => _serverProtocolIndex;
            set { _serverProtocolIndex = value; OnPropertyChanged(); }
        }

        private string _serverComPort = "COM3";

        public string ServerComPort
        {
            get => _serverComPort;
            set { _serverComPort = value; OnPropertyChanged(); }
        }

        private string _serverBaudRate = "9600";

        public string ServerBaudRate
        {
            get => _serverBaudRate;
            set { _serverBaudRate = value; OnPropertyChanged(); }
        }

        private string _serverDataBits = "8";

        public string ServerDataBits
        {
            get => _serverDataBits;
            set { _serverDataBits = value; OnPropertyChanged(); }
        }

        private string _serverParity = "None";

        public string ServerParity
        {
            get => _serverParity;
            set { _serverParity = value; OnPropertyChanged(); }
        }

        private string _serverStopBits = "One";

        public string ServerStopBits
        {
            get => _serverStopBits;
            set { _serverStopBits = value; OnPropertyChanged(); }
        }

        // Bu sanal cihazın (slave) adresi; Master bu adrese soru sorar.
        private string _serverUnitId = "1";

        public string ServerUnitId
        {
            get => _serverUnitId;
            set { _serverUnitId = value; OnPropertyChanged(); }
        }

        // ================================================================
        // İSTEK AYARLARI
        // ================================================================

        private string _slaveId = "1";

        public string SlaveId
        {
            get => _slaveId;

            set
            {
                _slaveId = value;
                OnPropertyChanged();
            }
        }

        // Function Code sırası (ilk dört indeks geriye uyumluluk için korunur):
        // 0 = FC01 Read Coils
        // 1 = FC03 Read Holding Registers
        // 2 = FC04 Read Input Registers
        // 3 = FC06 Write Single Register
        // 4 = FC02 Read Discrete Inputs
        // 5 = FC05 Write Single Coil
        // 6 = FC16 Write Multiple Registers
        private int _functionIndex = 1;

        public int FunctionIndex
        {
            get => _functionIndex;

            set
            {
                _functionIndex = value;
                OnPropertyChanged();
            }
        }

        private string _address = "0";

        public string Address
        {
            get => _address;

            set
            {
                _address = value;
                OnPropertyChanged();
            }
        }

        private string _quantity = "3";

        public string Quantity
        {
            get => _quantity;

            set
            {
                _quantity = value;
                OnPropertyChanged();
            }
        }

        private string _writeValue = "0";

        public string WriteValue
        {
            get => _writeValue;

            set
            {
                _writeValue = value;
                OnPropertyChanged();
            }
        }

        private string _writeValues = "10, 20, 30";

        /// <summary>
        /// FC16 için virgül, noktalı virgül veya boşlukla ayrılmış register değerleri.
        /// Örnek: 100, 200, 300
        /// </summary>
        public string WriteValues
        {
            get => _writeValues;

            set
            {
                _writeValues = value;
                OnPropertyChanged();
            }
        }

        private string _lastRequestSummary = "Henüz istek gönderilmedi.";

        public string LastRequestSummary
        {
            get => _lastRequestSummary;

            set
            {
                _lastRequestSummary = value;
                OnPropertyChanged();
            }
        }

        private string _lastResponseSummary = "Henüz cevap alınmadı.";

        public string LastResponseSummary
        {
            get => _lastResponseSummary;

            set
            {
                _lastResponseSummary = value;
                OnPropertyChanged();
            }
        }

        // ================================================================
        // EKRANDA GÖSTERİLEN KOLEKSİYONLAR
        // ================================================================

        public ObservableCollection<RegisterItem> ServerRegisters { get; } =
            new();

        public ObservableCollection<RegisterItem> ClientRegisters { get; } =
            new();

        public ObservableCollection<BitItem> ServerCoils { get; } =
            new();

        public ObservableCollection<BitItem> ServerDiscreteInputs { get; } =
            new();

        public ObservableCollection<BitItem> ClientBits { get; } =
            new();

        public ObservableCollection<PacketFieldItem> RequestDetails { get; } =
            new();

        public ObservableCollection<PacketFieldItem> ResponseDetails { get; } =
            new();

        public ObservableCollection<string> LogEntries { get; } =
            new();

        // Byte byte paket çözümlemesi
        public ObservableCollection<FrameByteItem> RequestBytes { get; } =
            new();

        public ObservableCollection<FrameByteItem> ResponseBytes { get; } =
            new();

        // Data Inspector
        public ObservableCollection<PacketFieldItem> InspectorBasic { get; } =
            new();

        public ObservableCollection<PacketFieldItem> InspectorLong { get; } =
            new();

        public ObservableCollection<PacketFieldItem> InspectorFloat { get; } =
            new();

        public ObservableCollection<PacketFieldItem> InspectorDouble { get; } =
            new();

        public ObservableCollection<PacketFieldItem> InspectorString { get; } =
            new();

        public ObservableCollection<PacketFieldItem> Ieee754Details { get; } =
            new();

        // ================================================================
        // COMMAND'LAR
        // ================================================================

        public ICommand ConnectClientCommand { get; }

        public ICommand DisconnectClientCommand { get; }

        public ICommand RefreshComPortsCommand { get; }

        public ICommand StartServerCommand { get; }

        public ICommand StopServerCommand { get; }

        public ICommand SendCommand { get; }

        public ICommand TogglePollCommand { get; }

        public ICommand ClearLogCommand { get; }

        // -------- Sürekli okuma (Polling) --------
        private bool _isPolling;

        /// <summary>Sürekli okuma açık mı? Arayüzdeki POLL düğmesinin durumunu belirler.</summary>
        public bool IsPolling
        {
            get => _isPolling;
            set { _isPolling = value; OnPropertyChanged(); OnPropertyChanged(nameof(PollButtonText)); }
        }

        /// <summary>POLL düğmesinin üzerindeki yazı.</summary>
        public string PollButtonText => _isPolling ? "STOP POLL" : "POLL";

        private string _scanRate = "1000";

        /// <summary>İki okuma arasındaki süre (ms). Modbus Poll'daki "Scan Rate" karşılığı.</summary>
        public string ScanRate
        {
            get => _scanRate;
            set { _scanRate = value; OnPropertyChanged(); }
        }

        // -------- İstatistik sayaçları (Modbus Poll durum çubuğu gibi) --------
        private int _txCount;
        public int TxCount { get => _txCount; set { _txCount = value; OnPropertyChanged(); } }

        private int _rxCount;
        public int RxCount { get => _rxCount; set { _rxCount = value; OnPropertyChanged(); } }

        private int _errCount;
        public int ErrCount { get => _errCount; set { _errCount = value; OnPropertyChanged(); } }

        private int _timeoutCount;
        public int TimeoutCount { get => _timeoutCount; set { _timeoutCount = value; OnPropertyChanged(); } }

        public ICommand ResetColorsCommand { get; }

        // ================================================================
        // DATA INSPECTOR SEÇİMLERİ
        // ================================================================

        /// <summary>
        /// Şu anda incelenen satır.
        /// Server veya Client tablosundan gelebilir.
        /// </summary>
        private RegisterItem? _inspectedRegister;

        private RegisterItem? _selectedServerRegister;

        public RegisterItem? SelectedServerRegister
        {
            get => _selectedServerRegister;

            set
            {
                if (_selectedServerRegister == value)
                    return;

                _selectedServerRegister = value;
                OnPropertyChanged();

                if (value != null)
                    Inspect(value, "Server");
            }
        }

        private RegisterItem? _selectedClientRegister;

        public RegisterItem? SelectedClientRegister
        {
            get => _selectedClientRegister;

            set
            {
                if (_selectedClientRegister == value)
                    return;

                _selectedClientRegister = value;
                OnPropertyChanged();

                if (value != null)
                    Inspect(value, "Client");
            }
        }

        private string _inspectorHeader =
            "İncelenecek bir register satırı seçin.";

        public string InspectorHeader
        {
            get => _inspectorHeader;

            set
            {
                _inspectorHeader = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// IEEE 754 görünümünün byte sırası:
        /// 0=ABCD, 1=CDAB, 2=BADC, 3=DCBA.
        /// </summary>
        private int _inspectorByteOrderIndex;

        public int InspectorByteOrderIndex
        {
            get => _inspectorByteOrderIndex;

            set
            {
                if (_inspectorByteOrderIndex == value)
                    return;

                _inspectorByteOrderIndex = value;
                OnPropertyChanged();

                RefreshInspector();
            }
        }

        // ================================================================
        // RENKLERİ SIFIRLA
        // ================================================================

        private void OnResetColors()
        {
            foreach (RegisterItem item in ServerRegisters)
                item.ResetChangeState();

            foreach (RegisterItem item in ClientRegisters)
                item.ResetChangeState();

            foreach (BitItem item in ServerCoils)
                item.ResetChangeState();

            foreach (BitItem item in ServerDiscreteInputs)
                item.ResetChangeState();

            foreach (BitItem item in ClientBits)
                item.ResetChangeState();

            AddLog("Değişim renkleri sıfırlandı.");
        }

        // ================================================================
        // CLIENT BAĞLANTI İŞLEMLERİ
        // ================================================================

        private void OnRefreshComPorts()
        {
            try
            {
                string[] ports = ModbusRtuClient.GetAvailablePortNames();

                AvailableComPorts.Clear();

                foreach (string port in ports)
                    AvailableComPorts.Add(port);

                if (ports.Length > 0)
                {
                    bool selectedPortStillExists = ports.Any(
                        port => string.Equals(
                            port,
                            ClientComPort,
                            StringComparison.OrdinalIgnoreCase));

                    if (!selectedPortStillExists)
                        ClientComPort = ports[0];

                    AddLog(
                        "COM portları yenilendi: " +
                        string.Join(", ", ports));
                }
                else
                {
                    AddLog(
                        "Bilgisayarda kullanılabilir COM port bulunamadı. " +
                        "USB-RS485 dönüştürücüyü takıp Refresh COM düğmesine basın.");
                }
            }
            catch (Exception ex)
            {
                AddLog("HATA (COM Port Refresh): " + ex.Message);
            }
        }

        private async void OnConnectClient()
        {
            try
            {
                if (_client != null)
                {
                    await _client.DisconnectAsync();
                    _client = null;
                }

                ClientStatus = "Bağlanıyor";

                if (ClientProtocolIndex == 0)
                {
                    int port = ParseIntInRange(
                        ClientPort,
                        1,
                        65535,
                        "Client portu");

                    var settings = new TcpConnectionSettings
                    {
                        IpAddress = ClientIpAddress.Trim(),
                        Port = port,
                        Timeout = 5000
                    };

                    _client = new ModbusTcpClient(settings);

                    AddLog(
                        $"TCP client bağlanıyor: " +
                        $"{settings.IpAddress}:{settings.Port} ...");
                }
                else
                {
                    int baudRate = ParseIntInRange(
                        ClientBaudRate,
                        1,
                        2_000_000,
                        "Baud rate");

                    int dataBits = ParseIntInRange(
                        ClientDataBits,
                        5,
                        8,
                        "Data bits");

                    int timeout = ParseIntInRange(
                        ClientTimeout,
                        100,
                        120_000,
                        "Timeout");

                    RtuParity parity = ParseEnumOption<RtuParity>(
                        ClientParity,
                        "Parity");

                    RtuStopBits stopBits = ParseEnumOption<RtuStopBits>(
                        ClientStopBits,
                        "Stop bits");

                    var settings = new RtuConnectionSettings
                    {
                        PortName = ClientComPort.Trim(),
                        BaudRate = baudRate,
                        DataBits = dataBits,
                        Parity = parity,
                        StopBits = stopBits,
                        Timeout = timeout
                    };

                    _client = new ModbusRtuClient(settings);

                    AddLog(
                        "RTU COM portu açılıyor: " +
                        settings.ToShortText() + " ...");
                }

                await _client.ConnectAsync();

                ClientStatus = "Bağlandı";
                ResetCounters(); // Yeni bağlantıda istatistikleri sıfırla.

                if (ClientProtocolIndex == 0)
                {
                    AddLog("TCP client bağlantısı başarılı.");
                }
                else
                {
                    AddLog(
                        "RTU COM portu açıldı. Bu durum yalnızca portun açıldığını gösterir; " +
                        "kart iletişimi ilk başarılı RX cevabıyla doğrulanır.");
                }
            }
            catch (Exception ex)
            {
                ClientStatus = "Hata";

                if (_client != null)
                {
                    try
                    {
                        await _client.DisconnectAsync();
                    }
                    catch
                    {
                        // Asıl bağlantı hatasının üzerine yeni hata yazmıyoruz.
                    }

                    _client = null;
                }

                AddLog("HATA (Client Connect): " + ex.Message);
            }
        }

        private async void OnDisconnectClient()
        {
            try
            {
                StopPolling(); // Bağlantı kapatılırken sürekli okumayı da durdur.

                if (_client != null)
                {
                    await _client.DisconnectAsync();
                    _client = null;
                }

                ClientStatus = "Bağlı değil";
                AddLog("Client bağlantısı kapatıldı.");
            }
            catch (Exception ex)
            {
                ClientStatus = "Hata";
                AddLog("HATA (Client Disconnect): " + ex.Message);
            }
        }

        // ================================================================
        // SERVER BAŞLATMA / DURDURMA
        // ================================================================

        private void OnStartServer()
        {
            try
            {
                bool anyRunning =
                    (_server != null && _server.IsRunning) ||
                    (_rtuServer != null && _rtuServer.IsRunning);

                if (anyRunning)
                {
                    AddLog("Server zaten çalışıyor.");
                    return;
                }

                // RTU slave (seri) seçiliyse ayrı yol izle.
                if (ServerProtocolIndex == 1)
                {
                    StartRtuSlave();
                    return;
                }

                // ---- TCP slave (mevcut davranış) ----
                int port = ParseIntInRange(
                    ServerPort,
                    1,
                    65535,
                    "Server portu");

                ServerClientStatus = "İstemci bağlı değil";
                IsServerClientConnected = false;

                _server = new ModbusTcpServer(
                    _dataStore,
                    port);

                // Server loglarını yalnızca ekrana yazmakla kalmıyoruz.
                // Client bağlandı/ayrıldı bilgisini de bu metotta işliyoruz.
                _server.OnLog += OnServerLog;

                _server.Start();

                ServerStatus = $"TCP çalışıyor :{port}";
            }
            catch (Exception ex)
            {
                if (_server != null)
                {
                    _server.OnLog -= OnServerLog;
                    _server = null;
                }

                if (_rtuServer != null)
                {
                    _rtuServer.OnLog -= AddLog;
                    _rtuServer = null;
                }

                ServerStatus = "Hata";
                ServerClientStatus = "İstemci bağlı değil";
                IsServerClientConnected = false;

                AddLog("HATA (Server Start): " + ex.Message);
            }
        }

        /// <summary>
        /// RTU (seri) slave'i başlatır. Kart bir Modbus MASTER ise bu uygulama
        /// COM portundan gelen istekleri karşılar. DataStore TCP ile ortaktır;
        /// yani tablodaki değerler burada da geçerlidir.
        /// </summary>
        private void StartRtuSlave()
        {
            int baud = ParseIntInRange(
                ServerBaudRate, 1, 2_000_000, "Server baud rate");

            int dataBits = ParseIntInRange(
                ServerDataBits, 5, 8, "Server data bits");

            byte unitId = (byte)ParseIntInRange(
                ServerUnitId, 1, 247, "Server Unit ID");

            RtuParity parity = ParseEnumOption<RtuParity>(
                ServerParity, "Server parity");

            RtuStopBits stopBits = ParseEnumOption<RtuStopBits>(
                ServerStopBits, "Server stop bits");

            var settings = new RtuConnectionSettings
            {
                PortName = ServerComPort.Trim(),
                BaudRate = baud,
                DataBits = dataBits,
                Parity = parity,
                StopBits = stopBits,
                Timeout = 3000
            };

            _rtuServer = new ModbusRtuServer(_dataStore, settings, unitId);
            _rtuServer.OnLog += AddLog;
            _rtuServer.Start();

            ServerStatus =
                $"RTU çalışıyor {settings.PortName} @ {settings.BaudRate}, Unit {unitId}";
            ServerClientStatus = "Seri hat dinleniyor";
            IsServerClientConnected = false;
        }

        private void OnStopServer()
        {
            try
            {
                if (_server != null)
                {
                    _server.Stop();

                    _server.OnLog -= OnServerLog;
                    _server = null;
                }

                if (_rtuServer != null)
                {
                    _rtuServer.Stop();
                    _rtuServer.OnLog -= AddLog;
                    _rtuServer = null;
                }

                ServerStatus = "Durduruldu";
                ServerClientStatus = "İstemci bağlı değil";
                IsServerClientConnected = false;
            }
            catch (Exception ex)
            {
                AddLog("HATA (Server Stop): " + ex.Message);
            }
        }

        // ================================================================
        // MODBUS İSTEĞİ GÖNDER
        // ================================================================

        private async void OnSend()
        {
            // Bir istek işleniyorsa (özellikle polling sırasında) yenisini başlatma.
            if (_requestInFlight)
                return;

            _requestInFlight = true;
            try
            {
                await SendRequestCoreAsync();
            }
            finally
            {
                _requestInFlight = false;
            }
        }

        private async Task SendRequestCoreAsync()
        {
            if (_client == null || !_client.IsConnected)
            {
                AddLog("Önce Client bölümünden bağlantı kurun.");
                return;
            }

            try
            {
                byte slaveId = (byte)ParseIntInRange(
                    SlaveId,
                    0,
                    247,
                    "Unit ID");

                ModbusFunctionCode function = FunctionIndex switch
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

                int enteredAddress = ParseIntInRange(
                    Address,
                    0,
                    65535,
                    "Adres");

                bool useLiBatProfile = false;
                ushort address;

                bool holdingOperation =
                    function == ModbusFunctionCode.ReadHoldingRegisters ||
                    function == ModbusFunctionCode.WriteSingleRegister ||
                    function == ModbusFunctionCode.WriteMultipleRegisters;

                if (holdingOperation &&
                    LiBatBmsProfile.TryLogicalToProtocolAddress(
                        enteredAddress,
                        out ushort liBatProtocolAddress))
                {
                    useLiBatProfile = true;
                    address = liBatProtocolAddress;

                    AddLog(
                        $"[LiBat] PLC adresi {enteredAddress} -> " +
                        $"Register/PDU adresi {address} (0x{address:X4}) olarak çevrildi.");
                }
                else
                {
                    address = (ushort)enteredAddress;
                }

                bool isRegisterRead =
                    function == ModbusFunctionCode.ReadHoldingRegisters ||
                    function == ModbusFunctionCode.ReadInputRegisters;

                bool isBitRead =
                    function == ModbusFunctionCode.ReadCoils ||
                    function == ModbusFunctionCode.ReadDiscreteInputs;

                ushort quantity = 1;
                ushort writeValue = 0;
                bool coilWriteValue = false;
                ushort[] multipleValues = Array.Empty<ushort>();

                if (isRegisterRead || isBitRead)
                {
                    quantity = (ushort)ParseIntInRange(
                        Quantity,
                        1,
                        isRegisterRead ? 125 : 2000,
                        "Miktar");
                }
                else if (function == ModbusFunctionCode.WriteSingleRegister)
                {
                    writeValue = (ushort)ParseIntInRange(
                        WriteValue,
                        0,
                        65535,
                        "Yazma değeri");
                }
                else if (function == ModbusFunctionCode.WriteSingleCoil)
                {
                    coilWriteValue = ParseCoilValue(WriteValue);
                    writeValue = coilWriteValue ? (ushort)1 : (ushort)0;
                }
                else if (function == ModbusFunctionCode.WriteMultipleRegisters)
                {
                    multipleValues = ParseRegisterValueList(WriteValues);
                    quantity = (ushort)multipleValues.Length;
                }

                if (useLiBatProfile &&
                    function == ModbusFunctionCode.WriteSingleRegister &&
                    enteredAddress == 40154 &&
                    writeValue is < 1 or > 247)
                {
                    throw new InvalidOperationException(
                        "LiBat 40154 (Modbus Address) yalnızca 1..247 arasında yazılabilir.");
                }

                if (useLiBatProfile &&
                    (isRegisterRead ||
                     function == ModbusFunctionCode.WriteMultipleRegisters))
                {
                    int lastLogicalAddress = enteredAddress + quantity - 1;
                    if (lastLogicalAddress > LiBatBmsProfile.LastLogicalAddress)
                    {
                        throw new InvalidOperationException(
                            $"LiBat isteği register map sınırını aşıyor. " +
                            $"Son geçerli adres {LiBatBmsProfile.LastLogicalAddress}.");
                    }
                }

                byte[] pdu = function switch
                {
                    ModbusFunctionCode.ReadCoils or
                    ModbusFunctionCode.ReadDiscreteInputs or
                    ModbusFunctionCode.ReadHoldingRegisters or
                    ModbusFunctionCode.ReadInputRegisters =>
                        _builder.BuildReadPdu(
                            function,
                            address,
                            quantity),

                    ModbusFunctionCode.WriteSingleCoil =>
                        _builder.BuildWriteSinglePdu(
                            function,
                            address,
                            coilWriteValue ? (ushort)0xFF00 : (ushort)0x0000),

                    ModbusFunctionCode.WriteSingleRegister =>
                        _builder.BuildWriteSinglePdu(
                            function,
                            address,
                            writeValue),

                    ModbusFunctionCode.WriteMultipleRegisters =>
                        _builder.BuildWriteMultipleRegistersPdu(
                            address,
                            multipleValues),

                    _ =>
                        throw new InvalidOperationException(
                            "Desteklenmeyen function code.")
                };

                bool isTcp = ClientProtocolIndex == 0;

                byte[] request;

                if (isTcp)
                {
                    _transactionId++;

                    request = _builder.WrapTcp(
                        _transactionId,
                        slaveId,
                        pdu);
                }
                else
                {
                    request = _builder.WrapRtu(
                        slaveId,
                        pdu);
                }

                FillRequestDetails(
                    request,
                    isTcp,
                    function,
                    address,
                    quantity,
                    writeValue,
                    multipleValues);

                if (useLiBatProfile)
                {
                    AddPacketField(
                        RequestDetails,
                        "LiBat PLC Address",
                        enteredAddress.ToString(),
                        $"Dokümandaki 4xxxx adresi; Modbus Register/PDU adresi {address} (0x{address:X4})");

                    LastRequestSummary =
                        $"LiBat {enteredAddress} -> Register {address} | " +
                        LastRequestSummary;
                }

                TxCount++;
                AddLog("[Client TX] " + ToHex(request));

                if (!isTcp)
                {
                    AddLog(
                        $"RTU cihaz cevabı bekleniyor (timeout: {ClientTimeout} ms) ...");
                }

                byte[] response =
                    await _client.SendAsync(request);

                RxCount++;
                AddLog("[Client RX] " + ToHex(response));

                if (!isTcp)
                {
                    AddLog(
                        "RTU cihaz cevabı alındı; Unit ID, function code ve CRC doğrulandı.");
                }

                ModbusPacket packet = isTcp
                    ? _parser.ParseTcpResponse(response)
                    : _parser.ParseRtuResponse(response);

                FillResponseDetails(
                    packet,
                    function,
                    address,
                    quantity);

                if (_parser.IsErrorResponse(packet))
                {
                    byte errorCode =
                        packet.Data.Length > 0
                            ? packet.Data[0]
                            : (byte)0;

                    LastResponseSummary =
                        $"Modbus hata cevabı: " +
                        $"FC 0x{packet.FunctionCode:X2}, " +
                        $"hata 0x{errorCode:X2} — " +
                        ExceptionDescription(errorCode);

                    AddLog(LastResponseSummary);
                    return;
                }

                if (isRegisterRead)
                {
                    ushort[] values =
                        _parser.ReadRegisterValues(packet);

                    string registerType =
                        function == ModbusFunctionCode.ReadInputRegisters
                            ? "Input Register"
                            : "Holding Register";

                    int displayStartAddress =
                        useLiBatProfile
                            ? enteredAddress
                            : address;

                    UpdateClientRegisterRows(
                        address,
                        displayStartAddress,
                        values,
                        registerType,
                        useLiBatProfile);

                    if (useLiBatProfile)
                    {
                        LastResponseSummary =
                            BuildLiBatReadSummary(
                                enteredAddress,
                                values);

                        AddLiBatResponseDetails(
                            enteredAddress,
                            values);

                        if (LiBatBmsProfile.TryDecodeStatusFromRead(
                                enteredAddress,
                                values,
                                out ulong batteryStatus,
                                out var activeStatusBits))
                        {
                            string statusText =
                                activeStatusBits.Count == 0
                                    ? "Aktif Battery Status biti yok."
                                    : string.Join(" | ", activeStatusBits);

                            AddPacketField(
                                ResponseDetails,
                                "Battery Status 64-bit",
                                $"0x{batteryStatus:X16}",
                                statusText);

                            AddLog(
                                "[LiBat Battery Status] " +
                                statusText);

                            LastResponseSummary +=
                                " | Battery Status: " +
                                statusText;
                        }
                    }
                    else
                    {
                        LastResponseSummary =
                            $"{values.Length} register okundu: " +
                            $"{string.Join(", ", values)}";
                    }

                    AddLog(LastResponseSummary);

                    if (ClientRegisters.Count > 0)
                    {
                        SelectedClientRegister =
                            ClientRegisters[0];
                    }
                }
                else if (isBitRead)
                {
                    bool[] values =
                        _parser.ReadBitValues(packet, quantity);

                    string bitType =
                        function == ModbusFunctionCode.ReadDiscreteInputs
                            ? "Discrete Input"
                            : "Coil";

                    UpdateClientBitRows(
                        address,
                        values,
                        bitType);

                    LastResponseSummary =
                        $"{values.Length} {bitType} okundu: " +
                        string.Join(", ", values.Select(value => value ? "1" : "0"));

                    AddLog(LastResponseSummary);
                }
                else if (function == ModbusFunctionCode.WriteSingleRegister)
                {
                    (ushort confirmedAddress, ushort confirmedValue) =
                        _parser.ReadWriteSingleConfirmation(packet);

                    if (confirmedAddress != address || confirmedValue != writeValue)
                    {
                        throw new InvalidOperationException(
                            "FC06 onay cevabı gönderilen adres/değer ile uyuşmuyor.");
                    }

                    int displayAddress =
                        useLiBatProfile
                            ? enteredAddress
                            : address;

                    LastResponseSummary =
                        useLiBatProfile
                            ? $"LiBat {displayAddress} (Register {address}) için yazma onaylandı. Değer: {writeValue}"
                            : $"Register {address} için yazma onaylandı. Değer: {writeValue}";

                    UpdateSingleClientRegister(
                        address,
                        displayAddress,
                        writeValue,
                        useLiBatProfile);

                    // LiBat 40154 yazılırsa gerçek BMS davranışına göre sonraki
                    // isteklerde yeni Unit ID kullanılmalıdır. Client alanını da
                    // kullanıcı yanlışlıkla eski ID ile devam etmesin diye güncelle.
                    if (useLiBatProfile &&
                        enteredAddress == 40154 &&
                        writeValue is >= 1 and <= 247)
                    {
                        SlaveId = writeValue.ToString();
                        ServerUnitId = writeValue.ToString();

                        AddLog(
                            $"[LiBat] 40154 yazıldı. Yeni Modbus Unit ID: {writeValue}. " +
                            "Sonraki istekler bu Unit ID ile gönderilmelidir.");
                    }

                    AddLog(LastResponseSummary);
                }
                else if (function == ModbusFunctionCode.WriteSingleCoil)
                {
                    (ushort confirmedAddress, ushort confirmedRawValue) =
                        _parser.ReadWriteSingleConfirmation(packet);

                    ushort expectedRawValue =
                        coilWriteValue ? (ushort)0xFF00 : (ushort)0x0000;

                    if (confirmedAddress != address || confirmedRawValue != expectedRawValue)
                    {
                        throw new InvalidOperationException(
                            "FC05 onay cevabı gönderilen adres/değer ile uyuşmuyor.");
                    }

                    UpdateSingleClientBit(
                        address,
                        coilWriteValue,
                        "Coil");

                    LastResponseSummary =
                        $"Coil {address} için yazma onaylandı. " +
                        $"Durum: {(coilWriteValue ? "ON / 1" : "OFF / 0")}";

                    AddLog(LastResponseSummary);
                }
                else if (function == ModbusFunctionCode.WriteMultipleRegisters)
                {
                    (ushort confirmedAddress, ushort confirmedQuantity) =
                        _parser.ReadWriteMultipleConfirmation(packet);

                    if (confirmedAddress != address ||
                        confirmedQuantity != multipleValues.Length)
                    {
                        throw new InvalidOperationException(
                            "FC16 onay cevabı gönderilen başlangıç adresi/adet ile uyuşmuyor.");
                    }

                    int displayStartAddress =
                        useLiBatProfile
                            ? enteredAddress
                            : address;

                    UpdateMultipleClientRegisters(
                        address,
                        displayStartAddress,
                        multipleValues,
                        useLiBatProfile);

                    LastResponseSummary =
                        useLiBatProfile
                            ? $"LiBat FC16 yazma onaylandı. Başlangıç: {displayStartAddress} (Register {address}), " +
                              $"adet: {multipleValues.Length}, değerler: {string.Join(", ", multipleValues)}"
                            : $"FC16 yazma onaylandı. Başlangıç adresi: {address}, " +
                              $"adet: {multipleValues.Length}, değerler: {string.Join(", ", multipleValues)}";

                    AddLog(LastResponseSummary);
                }
            }
            catch (TimeoutException ex)
            {
                TimeoutCount++;
                LastResponseSummary =
                    "Zaman aşımı: " + ex.Message;

                AddLog(
                    "HATA (RTU Timeout): " + ex.Message);
            }
            catch (Exception ex)
            {
                ErrCount++;
                LastResponseSummary =
                    "Hata: " + ex.Message;

                AddLog(
                    "HATA (Send): " + ex.Message);
            }
        }

        // ================================================================
        // SÜREKLİ OKUMA (POLLING)
        // ================================================================

        private void OnTogglePoll()
        {
            if (_isPolling)
                StopPolling();
            else
                StartPolling();
        }

        private void StartPolling()
        {
            if (_isPolling)
                return;

            if (_client == null || !_client.IsConnected)
            {
                AddLog("Sürekli okuma için önce Client bağlantısı kurun.");
                return;
            }

            int scan;
            try
            {
                scan = ParseIntInRange(ScanRate, 50, 600000, "Scan rate");
            }
            catch (Exception ex)
            {
                AddLog("HATA (Scan rate): " + ex.Message);
                return;
            }

            _pollTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(scan)
            };
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();

            IsPolling = true;
            AddLog($"Sürekli okuma başladı. Tarama aralığı: {scan} ms.");

            // İlk okumayı aralığı beklemeden hemen yap.
            OnSend();
        }

        private void StopPolling()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Tick -= OnPollTick;
                _pollTimer = null;
            }

            if (_isPolling)
            {
                IsPolling = false;
                AddLog("Sürekli okuma durduruldu.");
            }
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            // Bağlantı düştüyse otomatik dur.
            if (_client == null || !_client.IsConnected)
            {
                StopPolling();
                return;
            }

            // Önceki istek hâlâ sürüyorsa bu turu atla.
            if (_requestInFlight)
                return;

            OnSend();
        }

        private void ResetCounters()
        {
            TxCount = 0;
            RxCount = 0;
            ErrCount = 0;
            TimeoutCount = 0;
        }

        // ================================================================
        // SERVER REGISTER SATIRLARI
        // ================================================================

        private void CreateServerRegisterRows()
        {
            int visibleRegisterCount = Math.Min(
                20,
                _dataStore.HoldingRegisters.Length);

            // Önce mevcut generic test registerlarını koru.
            for (
                int address = 0;
                address < visibleRegisterCount;
                address++)
            {
                RegisterItem item = new(
                    address,
                    GetRegisterName(address),
                    _dataStore.HoldingRegisters[address]);

                item.RegisterReader = ReadServerRegister;
                item.PropertyChanged += OnServerRegisterRowChanged;
                ServerRegisters.Add(item);
            }

            // Hazır IEEE 754 Float32 örneği.
            if (ServerRegisters.Count > 11)
            {
                ServerRegisters[10].Alias =
                    "Float örneği (123.456)";

                ServerRegisters[10].DataType =
                    "Float32";

                ServerRegisters[10].Comment =
                    "Register 10 ve 11 birlikte IEEE 754 Float32 oluşturur.";

                ServerRegisters[11].Alias =
                    "Float örneği (düşük word)";

                ServerRegisters[10].RefreshDerived();
            }

            // Ardından LiBat BMS / STM32 simülasyon registerlarını dokümandaki
            // 4xxxx adresleriyle göster. Bunların gerçek DataStore adresleri
            // ProtocolAddress (88..154) değeridir.
            foreach (BmsRegister reg in LiBatBmsProfile.Registers)
            {
                if (reg.ProtocolAddress < 0 ||
                    reg.ProtocolAddress >= _dataStore.HoldingRegisters.Length)
                {
                    continue;
                }

                ushort rawValue =
                    _dataStore.GetHoldingRegister(reg.ProtocolAddress);

                RegisterItem item = new(
                    reg.LogicalAddress,
                    reg.Name,
                    rawValue)
                {
                    RegisterType = "Holding Register",
                    DataType = reg.Type == "int16" ? "Int16" : "UInt16",
                    DisplayValueOverride = LiBatBmsProfile.FormatValue(reg, rawValue),
                    Comment = LiBatBmsProfile.BuildComment(reg, rawValue),
                    RegisterReader = ReadServerRegister
                };

                item.PropertyChanged += OnServerRegisterRowChanged;
                ServerRegisters.Add(item);
            }
        }

        private void CreateServerBitRows()
        {
            int visibleBitCount = Math.Min(
                20,
                Math.Min(
                    _dataStore.Coils.Length,
                    _dataStore.DiscreteInputs.Length));

            for (int address = 0; address < visibleBitCount; address++)
            {
                BitItem coil = new(
                    address,
                    "Coil",
                    GetBitName(address, "Coil"),
                    _dataStore.GetCoil(address),
                    "FC01 ile okunur, FC05 ile yazılır.");

                coil.PropertyChanged += OnServerBitRowChanged;
                ServerCoils.Add(coil);

                BitItem discreteInput = new(
                    address,
                    "Discrete Input",
                    GetBitName(address, "Discrete Input"),
                    _dataStore.GetDiscreteInput(address),
                    "FC02 ile okunur. Emülatörde sensör durumunu taklit etmek için değiştirilebilir.");

                discreteInput.PropertyChanged += OnServerBitRowChanged;
                ServerDiscreteInputs.Add(discreteInput);
            }
        }

        private void OnServerBitRowChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (sender is not BitItem item ||
                e.PropertyName != nameof(BitItem.State))
            {
                return;
            }

            try
            {
                if (item.BitType == "Discrete Input")
                {
                    if (_dataStore.GetDiscreteInput(item.Address) == item.State)
                        return;

                    _dataStore.SetDiscreteInput(item.Address, item.State);
                    AddLog(
                        $"[UI] Server Discrete Input[{item.Address}] = " +
                        $"{(item.State ? "ON" : "OFF")} olarak değiştirildi.");
                    return;
                }

                if (_dataStore.GetCoil(item.Address) == item.State)
                    return;

                _dataStore.SetCoil(item.Address, item.State);
                AddLog(
                    $"[UI] Server Coil[{item.Address}] = " +
                    $"{(item.State ? "ON" : "OFF")} olarak değiştirildi.");
            }
            catch (Exception ex)
            {
                AddLog("Bit durumu güncelleme hatası: " + ex.Message);
            }
        }

        /// <summary>
        /// Server tablosunda verilen adresteki registerı bulur.
        /// </summary>
        private ushort? ReadServerRegister(int address)
        {
            foreach (RegisterItem item in ServerRegisters)
            {
                if (item.Address == address)
                    return item.Value;
            }

            return null;
        }

        /// <summary>
        /// Client tablosunda verilen adresteki registerı bulur.
        /// </summary>
        private ushort? ReadClientRegister(int address)
        {
            foreach (RegisterItem item in ClientRegisters)
            {
                if (item.Address == address)
                    return item.Value;
            }

            return null;
        }

        private void OnServerRegisterRowChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (sender is not RegisterItem item)
                return;

            // Register türü Holding/Input olarak değiştirildiyse
            // değeri doğru veri tablosuna aktar.
            if (e.PropertyName ==
                nameof(RegisterItem.RegisterType))
            {
                // LiBat register map yalnızca Holding Register alanını kullanır.
                // Profil satırının türü yanlışlıkla değiştirilirse geri al.
                if (LiBatBmsProfile.IsLogicalAddress(item.Address) &&
                    item.RegisterType != "Holding Register")
                {
                    item.RegisterType = "Holding Register";
                    AddLog(
                        $"[LiBat] {item.Address} Holding Register'dır; register tipi değiştirilemez.");
                    return;
                }

                WriteRowToDataStore(item);

                RefreshNeighbours(
                    ServerRegisters,
                    item.Address);

                RefreshInspector();
                return;
            }

            if (e.PropertyName !=
                nameof(RegisterItem.Value))
            {
                return;
            }

            WriteRowToDataStore(item);

            // Double64 dört register kullandığı için
            // önceki üç satırın sonucu da etkilenebilir.
            RefreshNeighbours(
                ServerRegisters,
                item.Address);

            RefreshInspector();
        }

        /// <summary>
        /// Register satırını türüne göre Holding veya Input Register
        /// veri alanına yazar.
        /// </summary>
        private void WriteRowToDataStore(
            RegisterItem item)
        {
            try
            {
                if (item.RegisterType ==
                    "Input Register")
                {
                    if (_dataStore.GetInputRegister(
                            item.Address) == item.Value)
                    {
                        return;
                    }

                    _dataStore.SetInputRegister(
                        item.Address,
                        item.Value);

                    AddLog(
                        $"[UI] Server Input Register" +
                        $"[{item.Address}] = {item.Value} " +
                        $"olarak değiştirildi.");

                    return;
                }

                int storageAddress =
                    GetHoldingStorageAddress(item.Address);

                if (item.Address == 40154 &&
                    item.Value is < 1 or > 247)
                {
                    ushort previousValue =
                        _dataStore.GetHoldingRegister(storageAddress);

                    AddLog(
                        $"[LiBat] 40154 Unit ID değeri 1..247 olmalıdır. " +
                        $"{item.Value} reddedildi; önceki değer {previousValue} korunuyor.");

                    item.Value = previousValue;
                    ApplyLiBatMetadataToRow(item);
                    return;
                }

                if (_dataStore.HoldingRegisters[storageAddress] == item.Value)
                {
                    ApplyLiBatMetadataToRow(item);
                    return;
                }

                _dataStore.SetHoldingRegister(
                    storageAddress,
                    item.Value);

                ApplyLiBatMetadataToRow(item);

                if (item.Address >= 40000)
                {
                    AddLog(
                        $"[UI][LiBat] PLC {item.Address} / Register {storageAddress} = " +
                        $"{item.Value} olarak değiştirildi.");
                }
                else
                {
                    AddLog(
                        $"[UI] Server Holding Register" +
                        $"[{item.Address}] = {item.Value} " +
                        $"olarak değiştirildi.");
                }
            }
            catch (Exception ex)
            {
                AddLog(
                    "Register güncelleme hatası: " +
                    ex.Message);
            }
        }

        /// <summary>
        /// Değiştirilen registerın önceki satırlarını da tazeler.
        /// Çünkü bu satır, Int32/Float32/Double64 değerinin parçası olabilir.
        /// </summary>
        private static void RefreshNeighbours(
            ObservableCollection<RegisterItem> rows,
            int changedAddress)
        {
            foreach (RegisterItem row in rows)
            {
                int distance =
                    changedAddress - row.Address;

                if (distance >= 0 &&
                    distance <= 3)
                {
                    row.RefreshDerived();
                }
            }
        }

        private void OnHoldingRegisterChanged(
            int address,
            ushort value)
        {
            RunOnUiThread(
                () =>
                {
                    ApplyToServerRow(
                        address,
                        value,
                        "Holding Register");

                    // LiBat 40154 / Register 154 Unit ID registerıdır.
                    if (address == 154 && value is >= 1 and <= 247)
                    {
                        ServerUnitId = value.ToString();
                    }
                });
        }

        private void OnInputRegisterChanged(
            int address,
            ushort value)
        {
            RunOnUiThread(
                () => ApplyToServerRow(
                    address,
                    value,
                    "Input Register"));
        }

        private void ApplyToServerRow(
            int address,
            ushort value,
            string registerType)
        {
            foreach (RegisterItem row in ServerRegisters)
            {
                int rowStorageAddress =
                    registerType == "Holding Register"
                        ? GetHoldingStorageAddress(row.Address)
                        : row.Address;

                if (rowStorageAddress == address &&
                    row.RegisterType == registerType)
                {
                    row.Value = value;
                    ApplyLiBatMetadataToRow(row);
                }
            }
        }

        private void OnCoilChanged(int address, bool value)
        {
            RunOnUiThread(
                () => ApplyToServerBitRow(
                    ServerCoils,
                    address,
                    value));
        }

        private void OnDiscreteInputChanged(int address, bool value)
        {
            RunOnUiThread(
                () => ApplyToServerBitRow(
                    ServerDiscreteInputs,
                    address,
                    value));
        }

        private static void ApplyToServerBitRow(
            ObservableCollection<BitItem> rows,
            int address,
            bool value)
        {
            BitItem? row = rows.FirstOrDefault(item => item.Address == address);

            if (row != null)
                row.State = value;
        }

        // ================================================================
        // CLIENT REGISTER SATIRLARI
        // ================================================================

        private void UpdateClientRegisterRows(
            ushort protocolStartAddress,
            int displayStartAddress,
            ushort[] values,
            string registerType,
            bool useLiBatProfile)
        {
            ClientRegisters.Clear();

            for (int i = 0; i < values.Length; i++)
            {
                int displayAddress =
                    displayStartAddress + i;

                RegisterItem row = new(
                    displayAddress,
                    GetRegisterName(displayAddress),
                    values[i])
                {
                    RegisterType = registerType,
                    RegisterReader = ReadClientRegister
                };

                if (useLiBatProfile)
                    ApplyLiBatMetadataToRow(row);

                ClientRegisters.Add(row);
            }

            // Komşu registerlar ancak bütün satırlar eklendikten sonra okunabilir.
            foreach (RegisterItem row in ClientRegisters)
                row.RefreshDerived();
        }

        private void UpdateSingleClientRegister(
            ushort protocolAddress,
            int displayAddress,
            ushort value,
            bool useLiBatProfile)
        {
            RegisterItem? existing =
                ClientRegisters.FirstOrDefault(
                    x => x.Address == displayAddress);

            if (existing != null)
            {
                existing.Value = value;

                if (useLiBatProfile)
                    ApplyLiBatMetadataToRow(existing);

                RefreshNeighbours(
                    ClientRegisters,
                    displayAddress);

                return;
            }

            RegisterItem row = new(
                displayAddress,
                GetRegisterName(displayAddress),
                value)
            {
                RegisterReader = ReadClientRegister
            };

            if (useLiBatProfile)
                ApplyLiBatMetadataToRow(row);

            ClientRegisters.Add(row);
            row.RefreshDerived();
        }

        private void UpdateMultipleClientRegisters(
            ushort protocolStartAddress,
            int displayStartAddress,
            ushort[] values,
            bool useLiBatProfile)
        {
            for (int i = 0; i < values.Length; i++)
            {
                UpdateSingleClientRegister(
                    (ushort)(protocolStartAddress + i),
                    displayStartAddress + i,
                    values[i],
                    useLiBatProfile);
            }

            foreach (RegisterItem row in ClientRegisters)
                row.RefreshDerived();
        }

        private void UpdateClientBitRows(
            ushort startAddress,
            bool[] values,
            string bitType)
        {
            ClientBits.Clear();

            for (int i = 0; i < values.Length; i++)
            {
                int address = startAddress + i;

                ClientBits.Add(
                    new BitItem(
                        address,
                        bitType,
                        GetBitName(address, bitType),
                        values[i],
                        bitType == "Coil"
                            ? "FC01 okuma sonucu; FC05 ile yazılabilir."
                            : "FC02 okuma sonucu; yalnızca okunur."));
            }
        }

        private void UpdateSingleClientBit(
            ushort address,
            bool value,
            string bitType)
        {
            BitItem? existing = ClientBits.FirstOrDefault(
                item => item.Address == address &&
                        item.BitType == bitType);

            if (existing != null)
            {
                existing.State = value;
                return;
            }

            ClientBits.Add(
                new BitItem(
                    address,
                    bitType,
                    GetBitName(address, bitType),
                    value,
                    "Yazma onayından sonra güncellendi."));
        }

        // ================================================================
        // GÖNDERİLEN PAKET AYRINTILARI
        // ================================================================

        private void FillRequestDetails(
            byte[] request,
            bool isTcp,
            ModbusFunctionCode function,
            ushort address,
            ushort quantity,
            ushort writeValue,
            ushort[] multipleValues)
        {
            RequestDetails.Clear();

            if (isTcp)
            {
                AddPacketField(
                    RequestDetails,
                    "Transaction ID",
                    ReadUInt16(request, 0).ToString(),
                    "İstek ile cevabı eşleştiren numara");

                AddPacketField(
                    RequestDetails,
                    "Protocol ID",
                    ReadUInt16(request, 2).ToString(),
                    "Modbus TCP için 0 olmalıdır");

                AddPacketField(
                    RequestDetails,
                    "Length",
                    ReadUInt16(request, 4).ToString(),
                    "Unit ID ve PDU uzunluğu");

                AddPacketField(
                    RequestDetails,
                    "Unit ID",
                    request[6].ToString(),
                    "Hedef cihaz numarası");

                AddPacketField(
                    RequestDetails,
                    "Function",
                    $"0x{request[7]:X2}",
                    FunctionDescription(request[7]));
            }
            else
            {
                AddPacketField(
                    RequestDetails,
                    "Unit ID",
                    request[0].ToString(),
                    "Hedef cihaz numarası");

                AddPacketField(
                    RequestDetails,
                    "Function",
                    $"0x{request[1]:X2}",
                    FunctionDescription(request[1]));

                AddPacketField(
                    RequestDetails,
                    "CRC",
                    $"{request[^2]:X2} {request[^1]:X2}",
                    "RTU hata kontrolü; düşük byte önce gösterilir");
            }

            AddPacketField(
                RequestDetails,
                "Start Address",
                address.ToString(),
                "İşlemin başladığı register, coil veya discrete input adresi");

            switch (function)
            {
                case ModbusFunctionCode.WriteSingleCoil:
                    AddPacketField(
                        RequestDetails,
                        "Write Coil State",
                        writeValue == 1 ? "ON / 1" : "OFF / 0",
                        writeValue == 1
                            ? "Paket içinde 0xFF00 gönderilir"
                            : "Paket içinde 0x0000 gönderilir");
                    break;

                case ModbusFunctionCode.WriteSingleRegister:
                    AddPacketField(
                        RequestDetails,
                        "Write Value",
                        writeValue.ToString(),
                        $"0x{writeValue:X4}");
                    break;

                case ModbusFunctionCode.WriteMultipleRegisters:
                    AddPacketField(
                        RequestDetails,
                        "Quantity",
                        multipleValues.Length.ToString(),
                        "Yazılacak register sayısı");

                    AddPacketField(
                        RequestDetails,
                        "Byte Count",
                        (multipleValues.Length * 2).ToString(),
                        "Her register 2 byte olduğu için adet × 2");

                    AddPacketField(
                        RequestDetails,
                        "Write Values",
                        string.Join(", ", multipleValues),
                        "Başlangıç adresinden itibaren sırayla yazılacak değerler");
                    break;

                default:
                    AddPacketField(
                        RequestDetails,
                        "Quantity",
                        quantity.ToString(),
                        "Okunacak eleman sayısı");
                    break;
            }

            AddPacketField(
                RequestDetails,
                "Raw Frame",
                ToHex(request),
                "Ağdan veya seri kablodan gönderilen ham paket");

            FillFrameBytes(
                RequestBytes,
                request,
                isTcp,
                isResponse: false);

            LastRequestSummary = function switch
            {
                ModbusFunctionCode.WriteSingleCoil =>
                    $"{FunctionDescription((byte)function)} — adres {address}, " +
                    $"durum {(writeValue == 1 ? "ON / 1" : "OFF / 0")}",

                ModbusFunctionCode.WriteSingleRegister =>
                    $"{FunctionDescription((byte)function)} — adres {address}, " +
                    $"değer {writeValue}",

                ModbusFunctionCode.WriteMultipleRegisters =>
                    $"{FunctionDescription((byte)function)} — başlangıç {address}, " +
                    $"adet {multipleValues.Length}, değerler {string.Join(", ", multipleValues)}",

                _ =>
                    $"{FunctionDescription((byte)function)} — adres {address}, adet {quantity}"
            };
        }

        // ================================================================
        // GELEN PAKET AYRINTILARI
        // ================================================================

        private void FillResponseDetails(
            ModbusPacket packet,
            ModbusFunctionCode requestedFunction,
            ushort startAddress,
            ushort requestedQuantity)
        {
            ResponseDetails.Clear();

            if (ClientProtocolIndex == 0)
            {
                AddPacketField(
                    ResponseDetails,
                    "Transaction ID",
                    packet.TransactionId.ToString(),
                    "İstekle aynı olmalıdır");

                AddPacketField(
                    ResponseDetails,
                    "Protocol ID",
                    packet.ProtocolId.ToString(),
                    "Modbus TCP için 0");

                AddPacketField(
                    ResponseDetails,
                    "Length",
                    packet.Length.ToString(),
                    "Cevap uzunluğu");
            }

            AddPacketField(
                ResponseDetails,
                "Unit ID",
                packet.SlaveId.ToString(),
                "Cevap veren cihaz");

            AddPacketField(
                ResponseDetails,
                "Function",
                $"0x{packet.FunctionCode:X2}",
                FunctionDescription(packet.FunctionCode));

            if ((packet.FunctionCode & 0x80) != 0)
            {
                string exception =
                    packet.Data.Length > 0
                        ? $"0x{packet.Data[0]:X2}"
                        : "Yok";

                AddPacketField(
                    ResponseDetails,
                    "Exception Code",
                    exception,
                    ExceptionDescription(
                        packet.Data.FirstOrDefault()));
            }
            else if (
                requestedFunction == ModbusFunctionCode.ReadHoldingRegisters ||
                requestedFunction == ModbusFunctionCode.ReadInputRegisters)
            {
                AddPacketField(
                    ResponseDetails,
                    "Byte Count",
                    packet.Data.Length > 0
                        ? packet.Data[0].ToString()
                        : "0",
                    "Register verilerinin byte sayısı");

                ushort[] values =
                    _parser.ReadRegisterValues(packet);

                for (int i = 0; i < values.Length; i++)
                {
                    AddPacketField(
                        ResponseDetails,
                        $"Register {startAddress + i}",
                        values[i].ToString(),
                        $"HEX {DataConverter.ToHex(values[i])} / " +
                        $"Binary {DataConverter.ToBinary(values[i])}");
                }
            }
            else if (
                requestedFunction == ModbusFunctionCode.ReadCoils ||
                requestedFunction == ModbusFunctionCode.ReadDiscreteInputs)
            {
                AddPacketField(
                    ResponseDetails,
                    "Byte Count",
                    packet.Data.Length > 0
                        ? packet.Data[0].ToString()
                        : "0",
                    "Paketlenmiş bit verisinin byte sayısı");

                AddPacketField(
                    ResponseDetails,
                    "Packed Bit Data",
                    packet.Data.Length > 1
                        ? ToHex(packet.Data.Skip(1).ToArray())
                        : "(boş)",
                    "Her byte içinde düşük bitten yüksek bite doğru 8 durum bulunur");

                bool[] values =
                    _parser.ReadBitValues(packet, requestedQuantity);

                string fieldPrefix =
                    requestedFunction == ModbusFunctionCode.ReadDiscreteInputs
                        ? "Discrete Input"
                        : "Coil";

                for (int i = 0; i < values.Length; i++)
                {
                    AddPacketField(
                        ResponseDetails,
                        $"{fieldPrefix} {startAddress + i}",
                        values[i] ? "ON / 1" : "OFF / 0",
                        $"Bit index {i}; byte {i / 8}, bit {i % 8}");
                }
            }
            else if (
                requestedFunction == ModbusFunctionCode.WriteSingleRegister ||
                requestedFunction == ModbusFunctionCode.WriteSingleCoil)
            {
                (ushort address, ushort value) =
                    _parser.ReadWriteSingleConfirmation(packet);

                AddPacketField(
                    ResponseDetails,
                    "Written Address",
                    address.ToString(),
                    "Cihazın onayladığı adres");

                if (requestedFunction == ModbusFunctionCode.WriteSingleCoil)
                {
                    AddPacketField(
                        ResponseDetails,
                        "Written Coil State",
                        value == 0xFF00 ? "ON / 1" : "OFF / 0",
                        $"Ham onay değeri: 0x{value:X4}");
                }
                else
                {
                    AddPacketField(
                        ResponseDetails,
                        "Written Value",
                        value.ToString(),
                        $"0x{value:X4}");
                }
            }
            else if (
                requestedFunction == ModbusFunctionCode.WriteMultipleRegisters)
            {
                (ushort address, ushort quantity) =
                    _parser.ReadWriteMultipleConfirmation(packet);

                AddPacketField(
                    ResponseDetails,
                    "Written Start Address",
                    address.ToString(),
                    "Cihazın onayladığı ilk register adresi");

                AddPacketField(
                    ResponseDetails,
                    "Written Quantity",
                    quantity.ToString(),
                    "Cihazın yazdığını onayladığı register sayısı");
            }

            AddPacketField(
                ResponseDetails,
                "Raw Frame",
                ToHex(packet.RawData),
                "Karşı taraftan gelen ham paket");

            FillFrameBytes(
                ResponseBytes,
                packet.RawData,
                ClientProtocolIndex == 0,
                isResponse: true);
        }

        // ================================================================
        // BYTE BYTE PAKET ÇÖZÜMLEMESİ
        // ================================================================

        private static void FillFrameBytes(
            ObservableCollection<FrameByteItem> target,
            byte[] frame,
            bool isTcp,
            bool isResponse)
        {
            target.Clear();

            for (
                int i = 0;
                i < frame.Length;
                i++)
            {
                target.Add(
                    new FrameByteItem
                    {
                        Offset = i,
                        Hex = $"0x{frame[i]:X2}",
                        Decimal = frame[i],
                        Binary =
                            DataConverter.ToBinary(frame[i]),
                        Meaning =
                            DescribeFrameByte(
                                i,
                                frame,
                                isTcp,
                                isResponse)
                    });
            }
        }

        private static string DescribeFrameByte(
            int index,
            byte[] frame,
            bool isTcp,
            bool isResponse)
        {
            if (isTcp)
            {
                switch (index)
                {
                    case 0:
                        return "MBAP — Transaction ID (yüksek byte)";

                    case 1:
                        return "MBAP — Transaction ID (düşük byte)";

                    case 2:
                        return "MBAP — Protocol ID (yüksek byte), Modbus'ta 0";

                    case 3:
                        return "MBAP — Protocol ID (düşük byte), Modbus'ta 0";

                    case 4:
                        return "MBAP — Length (yüksek byte)";

                    case 5:
                        return "MBAP — Length (düşük byte), sonraki byte sayısı";

                    case 6:
                        return "MBAP — Unit ID (hedef cihaz)";

                    case 7:
                        return "PDU — Function Code";
                }

                return DescribePduByte(
                    index - 8,
                    frame[7],
                    isResponse);
            }

            // RTU:
            // [Slave ID][Function Code][Veri...][CRC Low][CRC High]
            if (index == 0)
                return "RTU — Slave ID (hedef cihaz adresi)";

            if (index == 1)
                return "PDU — Function Code";

            if (index == frame.Length - 2)
                return "RTU — CRC (düşük byte)";

            if (index == frame.Length - 1)
                return "RTU — CRC (yüksek byte)";

            return DescribePduByte(
                index - 2,
                frame.Length > 1
                    ? frame[1]
                    : (byte)0,
                isResponse);
        }

        private static string DescribePduByte(
            int dataIndex,
            byte functionCode,
            bool isResponse)
        {
            if ((functionCode & 0x80) != 0)
            {
                return dataIndex == 0
                    ? "Exception Code (hata sebebi)"
                    : "Hata cevabı verisi";
            }

            if (!isResponse)
            {
                if (functionCode == 0x10)
                {
                    return dataIndex switch
                    {
                        0 => "İstek — Başlangıç adresi (yüksek byte)",
                        1 => "İstek — Başlangıç adresi (düşük byte)",
                        2 => "İstek — Yazılacak register adedi (yüksek byte)",
                        3 => "İstek — Yazılacak register adedi (düşük byte)",
                        4 => "İstek — Byte Count (adet × 2)",
                        _ =>
                            $"İstek — Register {(dataIndex - 5) / 2} " +
                            $"({(((dataIndex - 5) % 2 == 0) ? "yüksek" : "düşük")} byte)"
                    };
                }

                bool isSingleWrite =
                    functionCode == 0x05 ||
                    functionCode == 0x06;

                return dataIndex switch
                {
                    0 => "İstek — Start Address (yüksek byte)",
                    1 => "İstek — Start Address (düşük byte)",
                    2 => isSingleWrite
                        ? functionCode == 0x05
                            ? "İstek — Coil değeri (yüksek byte: FF=ON, 00=OFF)"
                            : "İstek — Yazılacak değer (yüksek byte)"
                        : "İstek — Quantity (yüksek byte)",
                    3 => isSingleWrite
                        ? functionCode == 0x05
                            ? "İstek — Coil değeri (düşük byte, her zaman 00)"
                            : "İstek — Yazılacak değer (düşük byte)"
                        : "İstek — Quantity (düşük byte)",
                    _ => "İstek verisi"
                };
            }

            if (functionCode == 0x05 ||
                functionCode == 0x06 ||
                functionCode == 0x10)
            {
                return dataIndex switch
                {
                    0 => "Cevap — Yazılan başlangıç adresi (yüksek byte)",
                    1 => "Cevap — Yazılan başlangıç adresi (düşük byte)",
                    2 => functionCode == 0x10
                        ? "Cevap — Yazılan register adedi (yüksek byte)"
                        : functionCode == 0x05
                            ? "Cevap — Coil onay değeri (yüksek byte)"
                            : "Cevap — Yazılan değer (yüksek byte)",
                    3 => functionCode == 0x10
                        ? "Cevap — Yazılan register adedi (düşük byte)"
                        : functionCode == 0x05
                            ? "Cevap — Coil onay değeri (düşük byte)"
                            : "Cevap — Yazılan değer (düşük byte)",
                    _ => "Cevap verisi"
                };
            }

            if (dataIndex == 0)
                return "Cevap — Byte Count (kaç byte veri geliyor)";

            if (functionCode == 0x01 ||
                functionCode == 0x02)
            {
                return
                    $"Cevap — Paketlenmiş bit verisi " +
                    $"(byte {dataIndex - 1})";
            }

            int registerIndex =
                (dataIndex - 1) / 2;

            bool isHighByte =
                (dataIndex - 1) % 2 == 0;

            return
                $"Cevap — Register {registerIndex} " +
                $"({(isHighByte ? "yüksek" : "düşük")} byte)";
        }

        // ================================================================
        // DATA INSPECTOR
        // ================================================================

        private void Inspect(
            RegisterItem register,
            string source)
        {
            _inspectedRegister = register;

            InspectorHeader =
                $"{source} tablosu — " +
                $"Adres {register.Address} " +
                $"({register.AddressHex}), " +
                $"{register.RegisterType}, " +
                $"ham değer {register.Value}";

            RefreshInspector();
        }

        private void RefreshInspector()
        {
            InspectorBasic.Clear();
            InspectorLong.Clear();
            InspectorFloat.Clear();
            InspectorDouble.Clear();
            InspectorString.Clear();
            Ieee754Details.Clear();

            RegisterItem? register =
                _inspectedRegister;

            if (register == null)
                return;

            // Tek register
            InspectorBasic.Add(
                new PacketFieldItem
                {
                    Field = "Unsigned (UInt16)",
                    Value =
                        register.UnsignedValue.ToString(),
                    Description =
                        "16 bitin işaretsiz okunuşu: 0 ile 65535"
                });

            InspectorBasic.Add(
                new PacketFieldItem
                {
                    Field = "Signed (Int16)",
                    Value =
                        register.SignedValue.ToString(),
                    Description =
                        "En üst bit işaret kabul edilir: " +
                        "-32768 ile 32767"
                });

            InspectorBasic.Add(
                new PacketFieldItem
                {
                    Field = "Hex",
                    Value = register.HexValue,
                    Description =
                        "Aynı 16 bitin hexadecimal gösterimi"
                });

            InspectorBasic.Add(
                new PacketFieldItem
                {
                    Field = "Binary",
                    Value = register.BinaryValue,
                    Description =
                        "Registerın gerçekte tuttuğu 16 bit"
                });

            // 32 bit: seçili register + sonraki register
            ushort[]? words32 =
                ReadNeighbours(register, 2);

            if (words32 == null)
            {
                InspectorLong.Add(
                    NotEnoughRegisters(2));

                InspectorFloat.Add(
                    NotEnoughRegisters(2));
            }
            else
            {
                for (
                    int i = 0;
                    i < 4;
                    i++)
                {
                    RegisterByteOrder order =
                        (RegisterByteOrder)i;

                    string name =
                        DataConverter.ByteOrderNames32[i];

                    InspectorLong.Add(
                        new PacketFieldItem
                        {
                            Field = "Long " + name,

                            Value =
                                DataConverter
                                    .ToInt32(words32, order)
                                    .ToString(),

                            Description =
                                "İşaretsiz karşılığı: " +
                                DataConverter
                                    .ToUInt32(words32, order)
                        });

                    InspectorFloat.Add(
                        new PacketFieldItem
                        {
                            Field = "Float " + name,

                            Value =
                                DataConverter.Format(
                                    DataConverter.ToFloat32(
                                        words32,
                                        order)),

                            Description =
                                "IEEE 754 Single — ham byte'lar: " +
                                DataConverter.ToHex(
                                    DataConverter.Reorder(
                                        words32,
                                        order))
                        });
                }
            }

            // 64 bit: seçili register + sonraki üç register
            ushort[]? words64 =
                ReadNeighbours(register, 4);

            if (words64 == null)
            {
                InspectorDouble.Add(
                    NotEnoughRegisters(4));
            }
            else
            {
                for (
                    int i = 0;
                    i < 4;
                    i++)
                {
                    RegisterByteOrder order =
                        (RegisterByteOrder)i;

                    InspectorDouble.Add(
                        new PacketFieldItem
                        {
                            Field =
                                "Double " +
                                DataConverter
                                    .ByteOrderNames64[i],

                            Value =
                                DataConverter.Format(
                                    DataConverter.ToDouble64(
                                        words64,
                                        order)),

                            Description =
                                "IEEE 754 Double — ham byte'lar: " +
                                DataConverter.ToHex(
                                    DataConverter.Reorder(
                                        words64,
                                        order))
                        });
                }
            }

            // ASCII / String
            ushort[] words =
                ReadNeighbours(register, 4) ??
                ReadNeighbours(register, 2) ??
                new[] { register.Value };

            InspectorString.Add(
                new PacketFieldItem
                {
                    Field = "ASCII (AB CD)",

                    Value =
                        DataConverter.ToAscii(
                            words,
                            RegisterByteOrder.ABCD),

                    Description =
                        $"{words.Length} register = " +
                        $"{words.Length * 2} byte"
                });

            InspectorString.Add(
                new PacketFieldItem
                {
                    Field = "ASCII (BA DC)",

                    Value =
                        DataConverter.ToAscii(
                            words,
                            RegisterByteOrder.BADC),

                    Description =
                        "Her registerın iki byte'ı takas edilmiştir"
                });

            FillIeee754Details(register);
        }

        private void FillIeee754Details(
            RegisterItem register)
        {
            RegisterByteOrder order =
                (RegisterByteOrder)
                InspectorByteOrderIndex;

            ushort[]? words32 =
                ReadNeighbours(register, 2);

            if (words32 == null)
            {
                Ieee754Details.Add(
                    NotEnoughRegisters(2));

                return;
            }

            Ieee754Analysis single =
                DataConverter.AnalyzeSingle(
                    words32,
                    order);

            AddIeeeRows(
                "FLOAT32",
                single);

            ushort[]? words64 =
                ReadNeighbours(register, 4);

            if (words64 == null)
                return;

            Ieee754Analysis dbl =
                DataConverter.AnalyzeDouble(
                    words64,
                    order);

            AddIeeeRows(
                "DOUBLE64",
                dbl);
        }

        private void AddIeeeRows(
            string prefix,
            Ieee754Analysis analysis)
        {
            AddPacketField(
                Ieee754Details,
                prefix + " — Biçim",
                analysis.FormatName,
                "IEEE 754 standardındaki bit dağılımı");

            AddPacketField(
                Ieee754Details,
                prefix + " — Hex",
                analysis.Hex,
                "Tüm bitlerin hexadecimal hâli");

            AddPacketField(
                Ieee754Details,
                prefix + " — Tüm bitler",
                analysis.FullBinary,
                "İşaret + üs + mantis");

            AddPacketField(
                Ieee754Details,
                prefix + " — İşaret biti",
                analysis.SignBit,
                analysis.SignMeaning);

            AddPacketField(
                Ieee754Details,
                prefix + " — Üs bitleri",
                analysis.ExponentBits,
                $"Ham üs {analysis.RawExponent} − " +
                $"bias {analysis.Bias} = " +
                $"gerçek üs {analysis.ActualExponent}");

            AddPacketField(
                Ieee754Details,
                prefix + " — Mantis bitleri",
                analysis.MantissaBits,
                "Sayının kesir kısmını taşıyan bitler");

            AddPacketField(
                Ieee754Details,
                prefix + " — Mantis değeri",
                analysis.MantissaFraction,
                "Normal sayılarda gizli 1 hesaplamaya eklenir");

            AddPacketField(
                Ieee754Details,
                prefix + " — Sınıf",
                analysis.Category,
                "Normal, denormal, sıfır, sonsuz veya NaN");

            AddPacketField(
                Ieee754Details,
                prefix + " — Hesap",
                analysis.Formula,
                "Değerin bitlerden hesaplanışı");

            AddPacketField(
                Ieee754Details,
                prefix + " — Sonuç",
                analysis.Value,
                "Ekranda gösterilen son sayı");
        }

        private static ushort[]? ReadNeighbours(
            RegisterItem register,
            int count)
        {
            if (register.RegisterReader == null)
                return null;

            ushort[] buffer =
                new ushort[count];

            for (
                int i = 0;
                i < count;
                i++)
            {
                ushort? value =
                    register.RegisterReader(
                        register.Address + i);

                if (value == null)
                    return null;

                buffer[i] = value.Value;
            }

            return buffer;
        }

        private static PacketFieldItem NotEnoughRegisters(
            int needed)
        {
            return new PacketFieldItem
            {
                Field = "Yetersiz register",
                Value = "—",

                Description =
                    $"Bu değer için ardışık {needed} " +
                    $"register gerekir; tabloda bulunamadı."
            };
        }

        // ================================================================
        // YARDIMCI METOTLAR
        // ================================================================

        /// <summary>
        /// Server tablosunda LiBat 4xxxx adresi kullanılıyorsa gerçek DataStore
        /// indeksini döndürür. Örn. 40111 -> 111. Generic satırlar aynen kalır.
        /// </summary>
        private static int GetHoldingStorageAddress(int displayAddress)
        {
            return LiBatBmsProfile.TryLogicalToProtocolAddress(
                    displayAddress,
                    out ushort protocolAddress)
                ? protocolAddress
                : displayAddress;
        }

        /// <summary>
        /// Bir RegisterItem LiBat haritasındaki bir adrese aitse isim, tip,
        /// ölçeklenmiş değer ve açıklama bilgilerini uygular.
        /// </summary>
        private static void ApplyLiBatMetadataToRow(RegisterItem row)
        {
            if (!LiBatBmsProfile.TryGetByLogicalAddress(
                    row.Address,
                    out BmsRegister reg))
            {
                return;
            }

            row.Alias = reg.Name;
            row.DataType =
                reg.Type == "int16"
                    ? "Int16"
                    : "UInt16";
            row.DisplayValueOverride =
                LiBatBmsProfile.FormatValue(reg, row.Value);
            row.Comment =
                LiBatBmsProfile.BuildComment(reg, row.Value);
        }

        /// <summary>
        /// LiBat FC03 okumasını ham sayı listesi yerine insanın anlayacağı
        /// register isimleri ve ölçeklenmiş değerlerle özetler.
        /// </summary>
        private static string BuildLiBatReadSummary(
            int logicalStartAddress,
            ushort[] values)
        {
            if (values.Length == 0)
                return "LiBat cevabı boş.";

            var parts = new System.Collections.Generic.List<string>();
            int maxShown = Math.Min(values.Length, 6);

            for (int i = 0; i < maxShown; i++)
            {
                int logicalAddress = logicalStartAddress + i;
                ushort raw = values[i];

                if (LiBatBmsProfile.TryGetByLogicalAddress(
                        logicalAddress,
                        out BmsRegister reg))
                {
                    parts.Add(
                        $"{logicalAddress} {reg.Name} = " +
                        $"{LiBatBmsProfile.FormatValue(reg, raw)} (raw {raw})");
                }
                else
                {
                    parts.Add($"{logicalAddress} = {raw}");
                }
            }

            if (values.Length > maxShown)
                parts.Add($"... +{values.Length - maxShown} register");

            return "LiBat: " + string.Join(" | ", parts);
        }

        /// <summary>
        /// Paket Alanları / Response tablosuna LiBat register anlamlarını ekler.
        /// </summary>
        private void AddLiBatResponseDetails(
            int logicalStartAddress,
            ushort[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                int logicalAddress = logicalStartAddress + i;
                ushort raw = values[i];

                if (LiBatBmsProfile.TryGetByLogicalAddress(
                        logicalAddress,
                        out BmsRegister reg))
                {
                    AddPacketField(
                        ResponseDetails,
                        $"LiBat {logicalAddress} — {reg.Name}",
                        LiBatBmsProfile.FormatValue(reg, raw),
                        $"Register {reg.ProtocolAddress}; raw {raw} / 0x{raw:X4}; {reg.Description}");
                }
                else
                {
                    AddPacketField(
                        ResponseDetails,
                        $"LiBat {logicalAddress}",
                        raw.ToString(),
                        "Dokümanda Reserved veya ayrı tanımı olmayan register.");
                }
            }
        }

        private static void AddPacketField(
            ObservableCollection<PacketFieldItem> collection,
            string field,
            string value,
            string description)
        {
            collection.Add(
                new PacketFieldItem
                {
                    Field = field,
                    Value = value,
                    Description = description
                });
        }

        private static string GetRegisterName(
            int address)
        {
            return address switch
            {
                0 => "Test Değeri 1",

                1 => "Test Değeri 2",

                2 => "Test Değeri 3",

                _ => $"Register {address}"
            };
        }

        private static string GetBitName(
            int address,
            string bitType)
        {
            return address switch
            {
                0 when bitType == "Coil" => "Test Coil 1",
                1 when bitType == "Coil" => "Test Coil 2",
                2 when bitType == "Coil" => "Test Coil 3",
                0 => "Test Input 1",
                1 => "Test Input 2",
                2 => "Test Input 3",
                _ => $"{bitType} {address}"
            };
        }

        private static string FunctionDescription(
            byte functionCode)
        {
            byte normalCode =
                (byte)(functionCode & 0x7F);

            string name = normalCode switch
            {
                0x01 =>
                    "Read Coils — açık/kapalı çıkış durumlarını oku",

                0x02 =>
                    "Read Discrete Inputs — salt okunur giriş durumlarını oku",

                0x03 =>
                    "Read Holding Registers — sayısal değerleri oku",

                0x04 =>
                    "Read Input Registers — salt okunur ölçümleri oku",

                0x05 =>
                    "Write Single Coil — tek açık/kapalı değer yaz",

                0x06 =>
                    "Write Single Register — tek sayısal değer yaz",

                0x10 =>
                    "Write Multiple Registers — birden fazla register yaz",

                _ =>
                    "Bilinmeyen veya desteklenmeyen işlem"
            };

            return (functionCode & 0x80) != 0
                ? "Hata cevabı: " + name
                : name;
        }

        private static string ExceptionDescription(
            byte code)
        {
            return code switch
            {
                0x01 =>
                    "Illegal Function — işlem desteklenmiyor",

                0x02 =>
                    "Illegal Data Address — adres geçersiz",

                0x03 =>
                    "Illegal Data Value — miktar veya değer geçersiz",

                0x04 =>
                    "Server Device Failure — server işlemi tamamlayamadı",

                _ =>
                    "Bilinmeyen hata kodu"
            };
        }

        private static TEnum ParseEnumOption<TEnum>(
            string text,
            string fieldName)
            where TEnum : struct, Enum
        {
            if (Enum.TryParse(
                    text,
                    ignoreCase: true,
                    out TEnum value))
            {
                return value;
            }

            throw new ArgumentException(
                $"{fieldName} seçimi geçersiz: {text}");
        }

        private static bool ParseCoilValue(string text)
        {
            string normalized = (text ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            return normalized switch
            {
                "1" or "TRUE" or "ON" or "AÇIK" or "ACIK" => true,
                "0" or "FALSE" or "OFF" or "KAPALI" => false,
                _ => throw new ArgumentException(
                    "FC05 Write Value alanına 1/0, ON/OFF veya TRUE/FALSE girin.")
            };
        }

        private static ushort[] ParseRegisterValueList(string text)
        {
            string[] parts = (text ?? string.Empty).Split(
                new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            if (parts.Length < 1 || parts.Length > 123)
            {
                throw new ArgumentException(
                    "FC16 için 1 ile 123 arasında register değeri girilmelidir.");
            }

            ushort[] values = new ushort[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                if (!ushort.TryParse(parts[i], out ushort value))
                {
                    throw new ArgumentException(
                        $"FC16 listesindeki '{parts[i]}' geçerli bir 0-65535 değeri değildir.");
                }

                values[i] = value;
            }

            return values;
        }

        private static int ParseIntInRange(
            string text,
            int minimum,
            int maximum,
            string fieldName)
        {
            if (!int.TryParse(
                    text,
                    out int value))
            {
                throw new ArgumentException(
                    $"{fieldName} sayı olmalıdır.");
            }

            if (value < minimum ||
                value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    fieldName,
                    $"{fieldName} {minimum} ile " +
                    $"{maximum} arasında olmalıdır.");
            }

            return value;
        }

        private static ushort ReadUInt16(
            byte[] data,
            int index)
        {
            return (ushort)(
                (data[index] << 8) |
                data[index + 1]);
        }

        private static string ToHex(
            byte[] data)
        {
            return data.Length == 0
                ? "(boş)"
                : BitConverter
                    .ToString(data)
                    .Replace("-", " ");
        }

        // ================================================================
        // SERVER LOG VE BAĞLANTI DURUMU
        // ================================================================

        /// <summary>
        /// Server tarafından üretilen bütün logları işler.
        /// Client bağlanma/ayrılma durumunu ayrıca ekrandaki göstergelere aktarır.
        /// </summary>
        private void OnServerLog(
            string message)
        {
            AddLog(message);

            // Server client bağlantısını kabul etti.
            if (message.StartsWith(
                    "Client bağlandı:",
                    StringComparison.OrdinalIgnoreCase))
            {
                string endpoint = message
                    .Substring(
                        "Client bağlandı:".Length)
                    .Trim();

                RunOnUiThread(() =>
                {
                    ServerClientStatus =
                        $"Bağlı: {endpoint}";

                    IsServerClientConnected =
                        true;
                });

                return;
            }

            // Server tarafında client bağlantısı kapandı.
            if (message.Contains(
                    "Client bağlantısı kapandı",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunOnUiThread(() =>
                {
                    ServerClientStatus =
                        "İstemci bağlı değil";

                    IsServerClientConnected =
                        false;
                });
            }
        }

        private void AddLog(
            string message)
        {
            string line =
                $"{DateTime.Now:HH:mm:ss.fff}  " +
                message;

            RunOnUiThread(() =>
            {
                LogEntries.Add(line);

                while (LogEntries.Count > 500)
                    LogEntries.RemoveAt(0);
            });
        }

        private static void RunOnUiThread(
            Action action)
        {
            var dispatcher =
                Application.Current?.Dispatcher;

            if (dispatcher != null &&
                !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }
    }
}