using System;

namespace Modbus.Communication.Server
{
    /// <summary>
    /// Server tarafının register, coil ve discrete input hafızası.
    /// Ekran ve Modbus server aynı veri deposunu kullandığı için bir tarafta yapılan
    /// değişiklik diğer tarafta anında görünür.
    /// </summary>
    public class ModbusDataStore
    {
        public ushort[] HoldingRegisters { get; }
        public ushort[] InputRegisters { get; }
        public bool[] Coils { get; }
        public bool[] DiscreteInputs { get; }

        public event Action<int, ushort>? HoldingRegisterChanged;
        public event Action<int, ushort>? InputRegisterChanged;
        public event Action<int, bool>? CoilChanged;
        public event Action<int, bool>? DiscreteInputChanged;

        public ModbusDataStore(int registerCount = 256, int bitCount = 100)
        {
            if (registerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(registerCount));

            if (bitCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(bitCount));

            // Tüm hafıza alanları BOŞ/sıfır olarak oluşturulur. Eski eğitim/test
            // değerleri (1234, 5678, 42, Float 123.456 vb.) YÜKLENMEZ.
            //
            // İçeriği ne dolduracak? App katmanındaki RegisterMemoryService, seçilen
            // cihaz profiline (LiBat BMS / Empty) göre. Böylece "Empty / Custom Device"
            // seçilince hafıza gerçekten boş kalır ve backend belirli bir cihaza bağlı olmaz.
            HoldingRegisters = new ushort[registerCount];
            InputRegisters = new ushort[registerCount];
            Coils = new bool[bitCount];
            DiscreteInputs = new bool[bitCount];
        }

        public ushort GetHoldingRegister(int address)
        {
            ValidateRegisterAddress(address);
            return HoldingRegisters[address];
        }

        public void SetHoldingRegister(int address, ushort value)
        {
            ValidateRegisterAddress(address);

            if (HoldingRegisters[address] == value)
                return;

            HoldingRegisters[address] = value;
            HoldingRegisterChanged?.Invoke(address, value);
        }

        public ushort GetInputRegister(int address)
        {
            ValidateInputRegisterAddress(address);
            return InputRegisters[address];
        }

        public void SetInputRegister(int address, ushort value)
        {
            ValidateInputRegisterAddress(address);

            if (InputRegisters[address] == value)
                return;

            InputRegisters[address] = value;
            InputRegisterChanged?.Invoke(address, value);
        }

        public bool GetCoil(int address)
        {
            ValidateCoilAddress(address);
            return Coils[address];
        }

        public void SetCoil(int address, bool value)
        {
            ValidateCoilAddress(address);

            if (Coils[address] == value)
                return;

            Coils[address] = value;
            CoilChanged?.Invoke(address, value);
        }

        public bool GetDiscreteInput(int address)
        {
            ValidateDiscreteInputAddress(address);
            return DiscreteInputs[address];
        }

        /// <summary>
        /// Gerçek cihazda discrete input salt okunurdur. Bu setter yalnızca
        /// emülatörün sensör girişini taklit edebilmesi için vardır.
        /// </summary>
        public void SetDiscreteInput(int address, bool value)
        {
            ValidateDiscreteInputAddress(address);

            if (DiscreteInputs[address] == value)
                return;

            DiscreteInputs[address] = value;
            DiscreteInputChanged?.Invoke(address, value);
        }

        public bool IsValidRegisterRange(int startAddress, int quantity)
        {
            return startAddress >= 0 &&
                   quantity > 0 &&
                   startAddress + quantity <= HoldingRegisters.Length;
        }

        public bool IsValidInputRegisterRange(int startAddress, int quantity)
        {
            return startAddress >= 0 &&
                   quantity > 0 &&
                   startAddress + quantity <= InputRegisters.Length;
        }

        public bool IsValidCoilRange(int startAddress, int quantity)
        {
            return startAddress >= 0 &&
                   quantity > 0 &&
                   startAddress + quantity <= Coils.Length;
        }

        public bool IsValidDiscreteInputRange(int startAddress, int quantity)
        {
            return startAddress >= 0 &&
                   quantity > 0 &&
                   startAddress + quantity <= DiscreteInputs.Length;
        }

        private void ValidateInputRegisterAddress(int address)
        {
            if (address < 0 || address >= InputRegisters.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(address),
                    "Input register adresi veri deposu sınırları dışında.");
            }
        }

        private void ValidateRegisterAddress(int address)
        {
            if (address < 0 || address >= HoldingRegisters.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(address),
                    "Register adresi veri deposu sınırları dışında.");
            }
        }

        private void ValidateCoilAddress(int address)
        {
            if (address < 0 || address >= Coils.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(address),
                    "Coil adresi veri deposu sınırları dışında.");
            }
        }

        private void ValidateDiscreteInputAddress(int address)
        {
            if (address < 0 || address >= DiscreteInputs.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(address),
                    "Discrete input adresi veri deposu sınırları dışında.");
            }
        }
    }
}