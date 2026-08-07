using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

using Modbus.App.Models;
using Modbus.App.Profiles;
using Modbus.Communication.Server;

namespace Modbus.App.Services
{
    /// <summary>
    /// Slave register hafızasının TEK doğruluk kaynağı.
    ///
    /// - Modbus tel değerleri ModbusDataStore'da tutulur (Server bu store'u okur/yazar).
    /// - UI satırları DeviceRegisterDefinition koleksiyonudur.
    /// - İki taraf İKİ YÖNLÜ senkrondur:
    ///     UI'da değer değişince   → store güncellenir,
    ///     Master FC06/FC16 yazınca → store event'i ile UI güncellenir.
    ///   Böylece "UI hafızası" ile "Modbus hafızası" asla ayrışmaz.
    /// </summary>
    public sealed class RegisterMemoryService
    {
        // Server ile ORTAK kullanılan gerçek Modbus hafızası.
        public ModbusDataStore DataStore { get; }

        public ObservableCollection<DeviceRegisterDefinition> Registers { get; } = new();

        public IDeviceProfile? ActiveProfile { get; private set; }

        private bool _applyingFromStore; // store→UI güncellemesi sırasında geri yazmayı engelle

        public RegisterMemoryService()
        {
            // Geniş adres alanı: custom register'lar için de yer olsun (logical 40000..40999).
            DataStore = new ModbusDataStore(1000, 256);
            DataStore.HoldingRegisterChanged += OnStoreHoldingChanged;
            DataStore.InputRegisterChanged += OnStoreInputChanged;
        }

        public int Capacity => DataStore.HoldingRegisters.Length;

        // ============================================================
        // PROFİL YÜKLEME / TEMİZLEME
        // ============================================================

        public void LoadProfile(IDeviceProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            ClearAll();
            ActiveProfile = profile;

            foreach (DeviceRegisterDefinition def in profile.CreateRegisters())
                AttachAndStore(def);
        }

        /// <summary>Sadece kullanıcının eklediği (profil olmayan) register'ları siler.</summary>
        public void ClearCustom()
        {
            foreach (DeviceRegisterDefinition def in Registers.Where(r => !r.IsProfileRegister).ToList())
                Remove(def);
        }

        public void ClearAll()
        {
            foreach (DeviceRegisterDefinition def in Registers.ToList())
                Remove(def);
        }

        // ============================================================
        // EKLE / DÜZENLE / SİL
        // ============================================================

        public bool TryAdd(DeviceRegisterDefinition def, out string error)
        {
            if (!Validate(def, ignore: null, out error))
                return false;

            AttachAndStore(def);
            error = string.Empty;
            return true;
        }

        public bool TryUpdate(DeviceRegisterDefinition target, DeviceRegisterDefinition edited, out string error)
        {
            if (!Validate(edited, ignore: target, out error))
                return false;

            // Adres/tip değiştiyse eski store hücresini sıfırla.
            if (target.PduAddress != edited.PduAddress || target.RegisterType != edited.RegisterType)
                WriteStore(target, 0);

            target.PduAddress = edited.PduAddress;
            target.LogicalAddress = edited.LogicalAddress;
            target.RegisterType = edited.RegisterType;
            target.Name = edited.Name;
            target.DataType = edited.DataType;
            target.Scale = edited.Scale;
            target.Unit = edited.Unit;
            target.Description = edited.Description;
            target.Writable = edited.Writable;
            target.NoValueSentinel = edited.NoValueSentinel;
            target.RawValue = edited.RawValue;

            WriteStore(target, target.RawValue);
            error = string.Empty;
            return true;
        }

        public void Remove(DeviceRegisterDefinition def)
        {
            def.PropertyChanged -= OnDefinitionChanged;
            Registers.Remove(def);
            WriteStore(def, 0); // hafızadan da temizle
        }

        // ============================================================
        // DOĞRULAMA
        // ============================================================

        public bool Validate(DeviceRegisterDefinition def, DeviceRegisterDefinition? ignore, out string error)
        {
            if (def.PduAddress < 0 || def.PduAddress >= Capacity)
            {
                error = $"PDU adresi 0 ile {Capacity - 1} arasında olmalı.";
                return false;
            }

            bool duplicate = Registers.Any(r =>
                !ReferenceEquals(r, ignore) &&
                r.PduAddress == def.PduAddress &&
                r.RegisterType == def.RegisterType);

            if (duplicate)
            {
                error = $"Bu adres zaten var: {def.LogicalAddress} ({def.RegisterTypeText}). Aynı adres iki kez eklenemez.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(def.Name))
                def.Name = $"Register {def.LogicalAddress}";

            error = string.Empty;
            return true;
        }

        // ============================================================
        // İÇ YARDIMCILAR (iki yönlü senkron)
        // ============================================================

        private void AttachAndStore(DeviceRegisterDefinition def)
        {
            def.PropertyChanged += OnDefinitionChanged;
            Registers.Add(def);
            WriteStore(def, def.RawValue);
        }

        // UI satırındaki RawValue değişince store'a yaz.
        private void OnDefinitionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_applyingFromStore) return;
            if (e.PropertyName != nameof(DeviceRegisterDefinition.RawValue)) return;
            if (sender is not DeviceRegisterDefinition def) return;

            WriteStore(def, def.RawValue);
        }

        private void WriteStore(DeviceRegisterDefinition def, ushort value)
        {
            if (def.PduAddress < 0 || def.PduAddress >= Capacity) return;

            if (def.RegisterType == RegisterKind.Input)
                DataStore.SetInputRegister(def.PduAddress, value);
            else
                DataStore.SetHoldingRegister(def.PduAddress, value);
        }

        // Master yazınca (store event) → UI satırını güncelle. UI thread'e taşınır.
        private void OnStoreHoldingChanged(int address, ushort value)
            => SyncFromStore(address, value, RegisterKind.Holding);

        private void OnStoreInputChanged(int address, ushort value)
            => SyncFromStore(address, value, RegisterKind.Input);

        private void SyncFromStore(int address, ushort value, RegisterKind kind)
        {
            void Apply()
            {
                DeviceRegisterDefinition? def = Registers.FirstOrDefault(
                    r => r.PduAddress == address && r.RegisterType == kind);

                if (def == null) return;

                _applyingFromStore = true;
                def.RawValue = value;
                _applyingFromStore = false;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }
    }
}
