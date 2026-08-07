using System;
using System.Globalization;

using Modbus.App.Models;

namespace Modbus.App.ViewModels
{
    /// <summary>
    /// Add/Edit Register diyaloğunun ViewModel'i. Alanları toplar, temel doğrulamayı
    /// yapar ve bir DeviceRegisterDefinition üretir. Adres çakışması/aralık kontrolü
    /// RegisterMemoryService tarafında yapılır.
    /// </summary>
    public sealed class AddEditRegisterViewModel : ViewModelBase
    {
        private readonly int _addressBase;

        public AddEditRegisterViewModel(int addressBase, DeviceRegisterDefinition? existing = null)
        {
            _addressBase = addressBase;

            if (existing != null)
            {
                Title = "Edit Register";
                RegisterTypeIndex = existing.RegisterType == RegisterKind.Input ? 1 : 0;
                _plcAddress = existing.LogicalAddress.ToString();
                _pduAddress = existing.PduAddress.ToString();
                Name = existing.Name;
                DataType = existing.DataType;
                RawValue = (existing.DataType == "Int16"
                    ? unchecked((short)existing.RawValue)
                    : (int)existing.RawValue).ToString(CultureInfo.InvariantCulture);
                Scale = existing.Scale.ToString(CultureInfo.InvariantCulture);
                Unit = existing.Unit;
                Description = existing.Description;
                Writable = existing.Writable;
            }
            else
            {
                Title = "Add Register";
            }
        }

        public string Title { get; }

        public string[] DataTypeOptions { get; } =
            { "UInt16", "Int16", "UInt32", "Int32", "Float32", "Double64", "String" };

        private int _registerTypeIndex; // 0=Holding, 1=Input
        public int RegisterTypeIndex { get => _registerTypeIndex; set { _registerTypeIndex = value; OnPropertyChanged(); } }

        private string _plcAddress = "40160";
        public string PlcAddress
        {
            get => _plcAddress;
            set
            {
                _plcAddress = value;
                OnPropertyChanged();
                // PLC girilince PDU'yu otomatik hesapla (base'e göre).
                if (int.TryParse(value, out int plc) && plc >= _addressBase)
                {
                    _pduAddress = (plc - _addressBase).ToString();
                    OnPropertyChanged(nameof(PduAddress));
                }
            }
        }

        private string _pduAddress = "160";
        public string PduAddress { get => _pduAddress; set { _pduAddress = value; OnPropertyChanged(); } }

        public string Name { get; set; } = string.Empty;
        private string _dataType = "UInt16";
        public string DataType { get => _dataType; set { _dataType = value; OnPropertyChanged(); } }

        public string RawValue { get; set; } = "0";
        public string Scale { get; set; } = "1";
        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Writable { get; set; } = true;

        /// <summary>Alanları doğrulayıp bir register tanımı üretir.</summary>
        public bool TryBuild(out DeviceRegisterDefinition def, out string error)
        {
            def = null!;
            error = string.Empty;

            if (!int.TryParse(PlcAddress, out int plc)) { error = "PLC / Logical Address sayı olmalı."; return false; }
            if (!int.TryParse(PduAddress, out int pdu)) { error = "PDU / Register Address sayı olmalı."; return false; }
            if (pdu < 0) { error = "PDU adresi negatif olamaz."; return false; }

            if (!TryParseRaw(RawValue, DataType, out ushort raw, out error)) return false;

            if (!double.TryParse(Scale, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) &&
                !double.TryParse(Scale, NumberStyles.Float, CultureInfo.CurrentCulture, out scale))
            {
                error = "Scale sayı olmalı (örn. 0.1).";
                return false;
            }

            def = new DeviceRegisterDefinition
            {
                LogicalAddress = plc,
                PduAddress = pdu,
                RegisterType = RegisterTypeIndex == 1 ? RegisterKind.Input : RegisterKind.Holding,
                Name = string.IsNullOrWhiteSpace(Name) ? $"Register {plc}" : Name.Trim(),
                DataType = DataType,
                Scale = scale == 0 ? 1.0 : scale,
                Unit = Unit?.Trim() ?? string.Empty,
                Description = Description?.Trim() ?? string.Empty,
                Writable = Writable,
                IsProfileRegister = false,
                RawValue = raw
            };
            return true;
        }

        private static bool TryParseRaw(string text, string dataType, out ushort raw, out string error)
        {
            raw = 0; error = string.Empty;
            text = (text ?? "0").Trim();

            if (!int.TryParse(text, out int value))
            {
                error = "Raw Value sayı olmalı.";
                return false;
            }

            if (dataType == "Int16")
            {
                if (value < -32768 || value > 32767) { error = "Int16 aralığı: -32768 .. 32767."; return false; }
                raw = unchecked((ushort)(short)value);
                return true;
            }

            // Diğer türlerde tek register 16-bit yorumlanır.
            if (value < 0 || value > 65535) { error = "UInt16 aralığı: 0 .. 65535."; return false; }
            raw = (ushort)value;
            return true;
        }
    }
}
