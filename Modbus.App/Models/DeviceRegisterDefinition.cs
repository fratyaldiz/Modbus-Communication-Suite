using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Modbus.App.Models
{
    /// <summary>Register'ın 0x (Holding) mi 3x (Input) mi olduğu.</summary>
    public enum RegisterKind
    {
        Holding,
        Input
    }

    /// <summary>Status/olay ciddiyeti.</summary>
    public enum EventSeverity
    {
        Status,
        Warning,
        Protection,
        Fault
    }

    /// <summary>
    /// Bir cihaz register'ının genel, profilden bağımsız tanımı.
    /// Hem profil (LiBat) register'ları hem de kullanıcının elle eklediği register'lar
    /// bu modelle temsil edilir. Register Memory tablosunun tek satır modelidir.
    ///
    /// RawValue, Modbus hafızasının (ModbusDataStore) aynasıdır; RegisterMemoryService
    /// iki yönlü senkron tutar (tek doğruluk kaynağı).
    /// </summary>
    public sealed class DeviceRegisterDefinition : INotifyPropertyChanged
    {
        /// <summary>0-tabanlı Modbus (PDU) adresi — telde kullanılan.</summary>
        public int PduAddress { get; set; }

        /// <summary>Kullanıcıya gösterilen mantıksal adres, örn. 40111.</summary>
        public int LogicalAddress { get; set; }

        public RegisterKind RegisterType { get; set; } = RegisterKind.Holding;

        public string Name { get; set; } = string.Empty;

        /// <summary>UInt16 / Int16 / UInt32 / Int32 / Float32 / Double64 / String.</summary>
        public string DataType { get; set; } = "UInt16";

        /// <summary>Ham değeri ölçekleme çarpanı (örn. 0.1 → 250 = 25.0).</summary>
        public double Scale { get; set; } = 1.0;

        public string Unit { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>Master FC06/FC16 ile yazılabilir mi?</summary>
        public bool Writable { get; set; } = true;

        /// <summary>Profil register'ı mı (silinemez), yoksa kullanıcı mı ekledi?</summary>
        public bool IsProfileRegister { get; set; }

        /// <summary>Bu değer görülürse "mevcut değil" say (örn. 0xFFFF). Null = yok.</summary>
        public ushort? NoValueSentinel { get; set; }

        private ushort _rawValue;

        /// <summary>16-bit ham değer. Modbus hafızasının aynası.</summary>
        public ushort RawValue
        {
            get => _rawValue;
            set
            {
                if (_rawValue == value)
                    return;

                _rawValue = value;
                _lastUpdated = DateTime.Now;

                OnPropertyChanged();
                OnPropertyChanged(nameof(Hex));
                OnPropertyChanged(nameof(Binary));
                OnPropertyChanged(nameof(DisplayValue));
                OnPropertyChanged(nameof(LastUpdated));
            }
        }

        private DateTime _lastUpdated = DateTime.Now;
        public string LastUpdated => _lastUpdated.ToString("HH:mm:ss.fff");

        public string RegisterTypeText => RegisterType == RegisterKind.Input ? "Input Register" : "Holding Register";
        public string AccessText => Writable ? "R/W" : "R";
        public string SourceText => IsProfileRegister ? "Profile" : "Custom";
        public string Hex => $"0x{RawValue:X4}";
        public string Binary => Convert.ToString(RawValue, 2).PadLeft(16, '0');
        public string PduAddressText => PduAddress.ToString();
        public string LogicalAddressText => LogicalAddress.ToString();

        /// <summary>
        /// Ham değeri veri türü + ölçek + birime göre kullanıcı dostu metne çevirir.
        /// (16-bit; 32/64-bit çoklu register yorumları Master ekranındaki Data Inspector'da.)
        /// </summary>
        public string DisplayValue
        {
            get
            {
                if (NoValueSentinel.HasValue && RawValue == NoValueSentinel.Value)
                    return "—";

                double numeric = DataType == "Int16" ? unchecked((short)RawValue) : RawValue;

                string text = Math.Abs(Scale - 1.0) < 1e-9
                    ? ((long)numeric).ToString(CultureInfo.InvariantCulture)
                    : (numeric * Scale).ToString("0.###", CultureInfo.InvariantCulture);

                return string.IsNullOrWhiteSpace(Unit) ? text : $"{text} {Unit}";
            }
        }

        public DeviceRegisterDefinition Clone() => (DeviceRegisterDefinition)MemberwiseClone();

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
