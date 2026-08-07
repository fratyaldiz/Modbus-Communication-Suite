# Modbus Communication Suite

Modbus **Master (Client)** ve **Slave (Server)** emülatörü. Uygulama açılışta bir
**rol seçim ekranı** gösterir; kullanıcı MASTER veya SLAVE seçer ve yalnızca o role ait
çalışma penceresi açılır. Modbus TCP ve Modbus RTU (seri port / RS485) desteklenir.
WPF (.NET 9) + MVVM ile yazılmıştır.

Amaç; gerçek bir PLC/BMS/kart olmadan Modbus haberleşmesini test etmek, bir cihazı
(örn. LiBat BMS / STM32) simüle etmek, gelen-giden paketleri byte seviyesinde incelemek
ve register değerlerini farklı veri tiplerinde (IEEE 754 float dahil) yorumlamaktır.

## Genel akış

```
Açılış → Role Selection
          ├── MASTER / CLIENT  → MasterWindow
          └── SLAVE  / SERVER  → SlaveWindow
```

TCP testinde uygulama iki ayrı örnek (instance) olarak açılır: biri Slave, biri Master.
RTU testinde tek örnek, seçilen role göre RTU Master ya da RTU Slave olarak çalışır
(aynı COM portu ikisi tarafından aynı anda açılamaz).

## Özellikler

**Genel**
- Rol bazlı ayrı Master / Slave pencereleri (tek ekranda karışık kontrol yok).
- TCP + RTU; FC01, FC02, FC03, FC04, FC05, FC06, FC16.
- CRC doğrulama, timeout yönetimi, Modbus exception çözümleme.
- Byte-byte Communication Traffic (HEX / DEC / BINARY / anlam) ve profesyonel zaman damgalı log.
- Ayarlar `%LocalAppData%/ModbusCommunicationSuite/settings.json` içinde saklanır (System.Text.Json).

**Master / Client**
- Bağlantı (TCP: IP/Port, RTU: COM/Baud/Data/Parity/Stop/Timeout), profesyonel durum metinleri
  (Disconnected, Connecting, Connected, COM Port Open — Device Not Verified, Waiting Response,
  Device Responded, Timeout, CRC Error, Modbus Exception, Connection Error).
- Modbus Request (Unit / Function / Address / Quantity / Write Value / FC16 Values) + **sürekli Polling**.
- Client Register Watch, Client Bit Watch, Data Inspector (Unsigned/Signed/Hex/Binary/UInt32/Int32/Float32/Double64).
- Status / Active Events ve Tx / Rx / Err / Timeout sayaçları.
- Adres alanına doğrudan `40111` yazılır (PLC adresi → PDU otomatik).

**Slave / Server**
- **Dinamik Register Memory**: `+ Add Register`, `Edit`, `Delete`, `Load Profile`, `Clear Custom`.
- **Device Profile** sistemi: `LiBat BMS / STM32` (hazır register haritası) ve `Empty / Custom Device` (boş başlar).
- **Tek doğruluk kaynağı**: UI'daki değer ile Modbus server hafızası iki yönlü senkron.
  Slave'de değeri değiştirince Master anında okur; Master FC06/FC16 ile yazınca Slave UI anında güncellenir.
- **Status / Active Events**: profilden gelen bit tanımları (LiBat 64-bit Battery Status → Bit / State / Severity / Description, "Show Active Only").
- TCP (Listen Port) ve RTU (COM/Baud/.../Unit ID) server.

**LiBat BMS desteği**
- Register haritası 40088..40154 (kaynak: https://wiki.li-bat.com/comm/modbus/register-map/).
- Adresleme: PDU = PLC − 40000 (40111 → 111). Ölçek/birim dönüşümü (271 → 27.1 °C), `0xFFFF` = "mevcut değil".
- 40154 (Unit ID) RTU slave çalışırken FC06 ile değiştirilince cihaz adresi anında güncellenir.

## Mimari

| Katman | Sorumluluk |
|--------|-----------|
| `Modbus.Core` | Ortak arayüzler (`IModbusClient`) |
| `Modbus.Protocol` | Paket oluşturma/çözme, CRC-16, fonksiyon kodları, veri dönüşümü (`DataConverter`, IEEE 754) |
| `Modbus.Communication` | TCP/RTU client + server, `ModbusDataStore`, `ModbusRequestHandler` |
| `Modbus.App` | WPF/MVVM: Views, ViewModels, Models, Profiles, Services |
| `Modbus.Devices` | Cihaz tanımları için ayrılmış |

`Modbus.App` iç yapısı:

```
Views/       RoleSelectionWindow, MasterWindow, SlaveWindow, AddEditRegisterWindow
ViewModels/  MasterViewModel, SlaveViewModel, AddEditRegisterViewModel, ViewModelBase
Models/      DeviceRegisterDefinition, StatusBitDefinition, ActiveEventItem, BitItem, FrameByteItem, ...
Profiles/    IDeviceProfile, LiBatDeviceProfile, EmptyDeviceProfile
Services/    RegisterMemoryService (tek doğruluk kaynağı), AddressTranslationService, SettingsService, FrameAnalyzer
```

## Gereksinimler

- .NET 9 SDK
- Windows (WPF)
- RTU için: seri port veya USB-RS485 dönüştürücü (isteğe bağlı)

## Derleme ve Çalıştırma

```bash
dotnet run --project Modbus.App/Modbus.App.csproj
```

Visual Studio: `ModbusCommunicationSuite.slnx` açılır, `Modbus.App` başlangıç projesi seçilir, F5.

## Kullanım

### TCP (iki örnek ile)

1. Uygulamayı çalıştır → **SLAVE / SERVER** seç → Device Profile `LiBat BMS / STM32` → Load Profile → Protocol `TCP` → Listen Port `1502` → **Start Server**.
2. Uygulamayı ikinci kez çalıştır → **MASTER / CLIENT** → Protocol `TCP` → IP `127.0.0.1` → Port `1502` → **Connect**.
3. Master: Function `03 Read Holding Registers`, Address `40111`, Quantity `1` → **Send Request** → `271` (= 27.1 °C).

### Özel register (Custom Device)

1. Slave → Device Profile `Empty / Custom Device` → Load Profile.
2. `+ Add Register`: PLC `40160`, Holding, Name `Test Temperature`, Raw `250`, Scale `0.1`, Unit `°C` → Save.
3. Master → FC03 Address `40160` Quantity `1` → `250` (tanım Slave tarafında `25.0 °C`).

### RTU (gerçek kart)

- Kart = slave cihaz ise: **Master** rolü, Protocol `RTU`, kartın COM/baud/parity/Unit ID değerleri → Connect → Send/Poll.
- Kart = master ise: **Slave** rolü, Protocol `RTU`, COM/baud/Unit ID → Start Server.

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
