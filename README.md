# Modbus Communication Suite

Tek ekran üzerinden hem **Master (Client)** hem **Slave (Server)** rolünü çalıştırabilen
Modbus emülatörü. Modbus TCP ve Modbus RTU (seri port / RS485) desteklenir. Uygulama
WPF (.NET 9) ile MVVM mimarisinde yazılmıştır.

Amaç; gerçek bir PLC/kart olmadan Modbus haberleşmesini test etmek, gelen-giden
paketleri byte seviyesinde incelemek ve register değerlerini farklı veri tiplerinde
(IEEE 754 float dahil) yorumlamaktır.

## Özellikler

- **Master / Client**: TCP veya RTU üzerinden istek gönderir, cevabı çözer ve tabloda gösterir.
- **Slave / Server**: Sanal cihaz olarak dinler ve gelen istekleri karşılar (TCP ve RTU).
- **Desteklenen fonksiyon kodları**: FC01, FC02, FC03, FC04, FC05, FC06, FC16.
- **Ayrı hafıza alanları**: Holding Register, Input Register, Coil, Discrete Input.
- **IEEE 754 çözümleme**: 16/32/64-bit tam sayı, Float32, Double64 ve ASCII dönüşümü;
  ABCD / CDAB / BADC / DCBA byte sıralaması seçimi; float için işaret/üs/mantissa
  bit ayrıştırması.
- **Paket analizi**: Gönderilen ve alınan çerçeve byte-byte (HEX / DEC / BINARY / anlam) gösterilir.
- **Seri port ayarları**: COM port listeleme, baud rate, data bits, parity, stop bits, timeout.
- **Responsive arayüz**: Her çözünürlük ve Windows ölçeklendirmesinde (100/125/150%) içerik
  tam görünür; alan yetmezse kaydırma çubuğu devreye girer.

## Mimari

Katmanlı yapı, her katman tek sorumluluk taşır:

| Proje | Sorumluluk |
|-------|-----------|
| `Modbus.Core` | Ortak arayüzler (`IModbusClient`) |
| `Modbus.Protocol` | Paket oluşturma/çözme, CRC-16, fonksiyon kodları, veri dönüşümü (IEEE 754) |
| `Modbus.Communication` | TCP/RTU client ve server, sanal cihaz veri deposu |
| `Modbus.App` | WPF arayüzü, MVVM (ViewModel, komutlar, modeller) |
| `Modbus.Devices` | Cihaz tanımları için ayrılmış |

## Gereksinimler

- .NET 9 SDK
- Windows (WPF)
- RTU için: seri port veya USB-RS485 dönüştürücü (isteğe bağlı)

## Derleme ve Çalıştırma

Komut satırı:

```bash
dotnet run --project Modbus.App/Modbus.App.csproj
```

Visual Studio: `ModbusCommunicationSuite.slnx` açılır, `Modbus.App` başlangıç projesi
seçilir ve F5 ile çalıştırılır.

## Kullanım

### TCP (donanımsız test)

1. Uygulamayı iki kez açın.
2. Birinci pencere — Server / Slave: Protocol `TCP`, START.
3. İkinci pencere — Client / Master: Protocol `TCP`, IP `127.0.0.1`, Port `1502`, bağlanın.
4. Function `03 Read Holding Registers`, Address `0`, Quantity `3`, SEND REQUEST.
5. Register 10–11 hazır IEEE 754 float örneği içerir (123.456); Float sekmesinden okunabilir.

### RTU (gerçek kart)

- **Kart = slave cihaz ise** uygulama master olur: Client tarafında Protocol `RTU`, COM
  portunu seçin, kartın dokümanındaki baud/parity/data/stop/Unit ID değerlerini girin.
- **Kart = master ise** uygulama slave olur: Server tarafında Protocol `RTU`, COM/baud/Unit
  ID ayarlayın, START.
- Aynı COM portu Client ve Server tarafında aynı anda kullanılamaz.

## Desteklenen Modbus Fonksiyonları

| Kod | İşlem |
|-----|-------|
| FC01 | Read Coils |
| FC02 | Read Discrete Inputs |
| FC03 | Read Holding Registers |
| FC04 | Read Input Registers |
| FC05 | Write Single Coil |
| FC06 | Write Single Register |
| FC16 | Write Multiple Registers |

## Proje Yapısı

```
ModbusCommunicationSuite.slnx
├─ Modbus.Core
│  └─ Interfaces/IModbusClient.cs
├─ Modbus.Protocol
│  ├─ Builders/PacketBuilder.cs
│  ├─ Parsers/ResponseParser.cs
│  ├─ Helpers/CRC16.cs, DataConverter.cs
│  ├─ Functions/ModbusFunctionCode.cs
│  └─ Packets/ModbusPacket.cs
├─ Modbus.Communication
│  ├─ TCP/ModbusTcpClient.cs
│  ├─ RTU/ModbusRtuClient.cs, RtuConnectionSettings.cs
│  └─ Server/ModbusTcpServer.cs, ModbusRtuServer.cs, ModbusRequestHandler.cs, ModbusDataStore.cs
├─ Modbus.App
│  ├─ Views / MainWindow.xaml
│  ├─ ViewModels/MainViewModel.cs
│  ├─ Models/ (RegisterItem, BitItem, FrameByteItem, ...)
│  └─ Commands/RelayCommand.cs
└─ Modbus.Devices
```
