using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Modbus.App.Models
{
    /// <summary>
    /// Coil veya Discrete Input tablosundaki tek bir açık/kapalı satırı temsil eder.
    /// </summary>
    public sealed class BitItem : INotifyPropertyChanged
    {
        private string _bitType;
        private string _alias;
        private bool _state;
        private string _comment;
        private bool _isChanged;
        private DateTime _lastUpdated;

        public BitItem(
            int address,
            string bitType,
            string alias,
            bool state,
            string comment = "")
        {
            Address = address;
            _bitType = bitType;
            _alias = alias;
            _state = state;
            _comment = comment;
            _lastUpdated = DateTime.Now;
        }

        public int Address { get; }

        public string AddressHex => $"0x{Address:X4}";

        public string BitType
        {
            get => _bitType;
            set
            {
                if (_bitType == value)
                    return;

                _bitType = value;
                OnPropertyChanged();
            }
        }

        public string Alias
        {
            get => _alias;
            set
            {
                if (_alias == value)
                    return;

                _alias = value;
                OnPropertyChanged();
            }
        }

        public bool State
        {
            get => _state;
            set
            {
                if (_state == value)
                    return;

                _state = value;
                _isChanged = true;
                _lastUpdated = DateTime.Now;

                OnPropertyChanged();
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(NumericValue));
                OnPropertyChanged(nameof(IsChanged));
                OnPropertyChanged(nameof(ChangeArrow));
                OnPropertyChanged(nameof(LastUpdated));
            }
        }

        public int NumericValue => State ? 1 : 0;

        public string StateText => State ? "ON / TRUE" : "OFF / FALSE";

        public string ChangeArrow => IsChanged ? "●" : string.Empty;

        public bool IsChanged => _isChanged;

        public string LastUpdated => _lastUpdated.ToString("HH:mm:ss.fff");

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

        public void ResetChangeState()
        {
            if (!_isChanged)
                return;

            _isChanged = false;
            OnPropertyChanged(nameof(IsChanged));
            OnPropertyChanged(nameof(ChangeArrow));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
