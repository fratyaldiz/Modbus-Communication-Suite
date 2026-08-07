using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Modbus.Protocol.Helpers;

namespace Modbus.App.Models
{
    /// <summary>
    /// Register tablosunda görünen tek bir satırı temsil eder.
    ///
    /// Bir Modbus register'ı 16 bittir. Ama 32 bitlik (Float/Long) veya
    /// 64 bitlik (Double) bir sayı göstermek istiyorsak yandaki register'lara da
    /// bakmamız gerekir. Bunu <see cref="RegisterReader"/> ile yapıyoruz:
    /// ViewModel bu satıra "komşu register'ları şuradan okuyabilirsin" diyor.
    /// </summary>
    public sealed class RegisterItem : INotifyPropertyChanged
    {
        /// <summary>Komşu register bulunamadığında gösterilecek metin.</summary>
        private const string NotAvailable = "— (komşu register yok)";

        private string _registerType;
        private string _alias;
        private ushort _value;
        private string _dataType;
        private string _byteOrder;
        private string _comment;
        private string? _displayValueOverride;
        private DateTime _lastUpdated;
        private int _changeDirection;

        /// <summary>
        /// Register Type sütununda gösterilecek seçenekler.
        /// Modbus'ta bunlar AYRI adres alanlarıdır: 4x holding, 3x input.
        /// </summary>
        public static IReadOnlyList<string> RegisterTypeOptions { get; } =
            new[]
            {
                "Holding Register",
                "Input Register"
            };

        /// <summary>
        /// Data Type sütununda gösterilecek seçenekler.
        /// </summary>
        public static IReadOnlyList<string> DataTypeOptions { get; } =
            new[]
            {
                "UInt16",
                "Int16",
                "UInt32",
                "Int32",
                "Float32",
                "Double64",
                "String"
            };

        /// <summary>
        /// Byte sırası seçenekleri. Cihaz üreticisine göre değiştiği için
        /// kullanıcının seçebilmesi gerekir.
        /// </summary>
        public static IReadOnlyList<string> ByteOrderOptions { get; } =
            new[]
            {
                "AB CD",
                "CD AB",
                "BA DC",
                "DC BA"
            };

        public RegisterItem(int address, string alias, ushort value)
        {
            Address = address;

            _registerType = "Holding Register";
            _alias = alias;
            _value = value;
            _dataType = "UInt16";
            _byteOrder = "AB CD";
            _comment = GetDefaultComment(address);
            _lastUpdated = DateTime.Now;
        }

        /// <summary>
        /// Bu satırın komşu register'ları okuyabilmesi için ViewModel'in verdiği erişim.
        /// Adres tabloda yoksa null döner.
        /// </summary>
        public Func<int, ushort?>? RegisterReader { get; set; }

        /// <summary>
        /// Registerın adresi. Örneğin 0, 1, 2.
        /// </summary>
        public int Address { get; }

        /// <summary>
        /// Adresin tabloda gösterilecek biçimi.
        /// Generic Modbus adresleri hex gösterilir. LiBat profilinde kullanıcı
        /// dokümandaki 4xxxx adresini görmek istediği için 40088..40154 doğrudan
        /// onluk olarak gösterilir.
        /// </summary>
        public string AddressHex =>
            Address >= 40000
                ? Address.ToString()
                : $"0x{Address:X4}";

        /// <summary>
        /// Registerın türü. Holding (4x, oku/yaz) veya Input (3x, salt okunur).
        /// </summary>
        public string RegisterType
        {
            get => _registerType;
            set
            {
                if (_registerType == value)
                    return;

                _registerType = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Kullanıcının registera verdiği anlaşılır isim.
        /// </summary>
        public string Alias
        {
            get => _alias;
            set
            {
                if (_alias == value)
                    return;

                _alias = value;
                OnPropertyChanged();

                // Eski kodlarda Name kullanılıyorsa uyumluluk devam etsin.
                OnPropertyChanged(nameof(Name));
            }
        }

        /// <summary>
        /// Eski MainViewModel koduyla uyumluluk için tutuluyor.
        /// Name ile Alias aynı değeri ifade eder.
        /// </summary>
        public string Name
        {
            get => Alias;
            set => Alias = value;
        }

        /// <summary>
        /// Register içinde bulunan gerçek 16 bit ham değer.
        /// </summary>
        public ushort Value
        {
            get => _value;
            set
            {
                if (_value == value)
                    return;

                // Emülatörlerdeki gibi "arttı / azaldı" okunu gösterebilmek için
                // değişimin yönünü saklıyoruz.
                _changeDirection = value > _value ? 1 : -1;

                _value = value;
                _lastUpdated = DateTime.Now;

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChanged));
                OnPropertyChanged(nameof(ChangeArrow));
                RefreshDerived();
            }
        }

        /// <summary>
        /// Ham register değerinin nasıl yorumlanacağını belirtir.
        /// </summary>
        public string DataType
        {
            get => _dataType;
            set
            {
                if (_dataType == value)
                    return;

                _dataType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayValue));
            }
        }

        /// <summary>
        /// 32/64 bitlik değerlerde register ve byte sırası.
        /// </summary>
        public string ByteOrder
        {
            get => _byteOrder;
            set
            {
                if (_byteOrder == value)
                    return;

                _byteOrder = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayValue));
            }
        }

        /// <summary>
        /// Register hakkında kullanıcı açıklaması.
        /// </summary>
        public string Comment
        {
            get => _comment;
            set
            {
                if (_comment == value)
                    return;

                _comment = value;
                OnPropertyChanged();
            }
        }

        /// <summary>16 bitlik değerin işaretsiz (0..65535) hali.</summary>
        public ushort UnsignedValue => _value;

        /// <summary>16 bitlik değerin işaretli (-32768..32767) hali.</summary>
        public short SignedValue => DataConverter.ToInt16(_value);

        /// <summary>
        /// Cihaz profili ham register değerine özel ölçek/birim uygulamak istiyorsa
        /// buraya hazır gösterim metni yazabilir. Örneğin LiBat 40111 için
        /// ham 271 değeri "27.1 °C" olarak gösterilir. null/boş ise normal
        /// DataType hesaplaması kullanılır.
        /// </summary>
        public string? DisplayValueOverride
        {
            get => _displayValueOverride;
            set
            {
                if (_displayValueOverride == value)
                    return;

                _displayValueOverride = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayValue));
            }
        }

        /// <summary>
        /// Seçilen veri türüne göre yorumlanmış değer.
        ///
        /// UInt16 / Int16           -> tek register
        /// UInt32 / Int32 / Float32 -> bu register + bir sonraki
        /// Double64                 -> bu register + sonraki üç register
        /// </summary>
        public string DisplayValue
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_displayValueOverride))
                    return _displayValueOverride;

                RegisterByteOrder order = DataConverter.ParseByteOrder(_byteOrder);

                switch (_dataType)
                {
                    case "UInt16":
                        return _value.ToString();

                    case "Int16":
                        return DataConverter.ToInt16(_value).ToString();

                    case "UInt32":
                        return TryReadRegisters(2, out ushort[] u32)
                            ? DataConverter.ToUInt32(u32, order).ToString()
                            : NotAvailable;

                    case "Int32":
                        return TryReadRegisters(2, out ushort[] i32)
                            ? DataConverter.ToInt32(i32, order).ToString()
                            : NotAvailable;

                    case "Float32":
                        return TryReadRegisters(2, out ushort[] f32)
                            ? DataConverter.Format(DataConverter.ToFloat32(f32, order))
                            : NotAvailable;

                    case "Double64":
                        return TryReadRegisters(4, out ushort[] d64)
                            ? DataConverter.Format(DataConverter.ToDouble64(d64, order))
                            : NotAvailable;

                    case "String":
                        return "\"" + DataConverter.ToAscii(new[] { _value }, order) + "\"";

                    default:
                        return _value.ToString();
                }
            }
        }

        /// <summary>
        /// Değerin hexadecimal gösterimi.
        /// </summary>
        public string HexValue => DataConverter.ToHex(_value);

        /// <summary>
        /// Değerin 16 bit binary gösterimi (dörderli gruplanmış).
        /// </summary>
        public string BinaryValue => DataConverter.ToBinary(_value);

        /// <summary>
        /// Değerin son değiştiği saat.
        /// </summary>
        public string LastUpdated => _lastUpdated.ToString("HH:mm:ss.fff");

        /// <summary>Bu satırın değeri en son "Reset Colors"tan bu yana değişti mi?</summary>
        public bool IsChanged => _changeDirection != 0;

        /// <summary>Değişim yönünü gösteren ok. Emülatördeki yeşil satırların karşılığı.</summary>
        public string ChangeArrow
        {
            get
            {
                if (_changeDirection > 0)
                    return "▲";

                if (_changeDirection < 0)
                    return "▼";

                return string.Empty;
            }
        }

        /// <summary>Renk/ok işaretlerini temizler.</summary>
        public void ResetChangeState()
        {
            if (_changeDirection == 0)
                return;

            _changeDirection = 0;
            OnPropertyChanged(nameof(IsChanged));
            OnPropertyChanged(nameof(ChangeArrow));
        }

        /// <summary>
        /// Komşu register değiştiğinde bu satırın hesaplanan alanlarını tazeler.
        /// (Bir Float32 satırı, yanındaki register değişince güncellenmelidir.)
        /// </summary>
        public void RefreshDerived()
        {
            OnPropertyChanged(nameof(UnsignedValue));
            OnPropertyChanged(nameof(SignedValue));
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(HexValue));
            OnPropertyChanged(nameof(BinaryValue));
            OnPropertyChanged(nameof(LastUpdated));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Bu adresten başlayarak istenen sayıda register'ı okur.
        /// Biri bile yoksa false döner (tabloda o adres tanımlı değildir).
        /// </summary>
        private bool TryReadRegisters(int count, out ushort[] registers)
        {
            registers = Array.Empty<ushort>();

            if (RegisterReader == null)
                return false;

            ushort[] buffer = new ushort[count];

            for (int i = 0; i < count; i++)
            {
                ushort? neighbour = RegisterReader(Address + i);
                if (neighbour == null)
                    return false;

                buffer[i] = neighbour.Value;
            }

            registers = buffer;
            return true;
        }

        private static string GetDefaultComment(int address)
        {
            return address switch
            {
                0 => "İlk örnek test registerı",
                1 => "İkinci örnek test registerı",
                2 => "Üçüncü örnek test registerı",
                _ => string.Empty
            };
        }

        private void OnPropertyChanged(
            [CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}
