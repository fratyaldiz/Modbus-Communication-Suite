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

            HoldingRegisters = new ushort[registerCount];

            // Input Register (3x) AYRI bir adres alanıdır. Holding Register 0 ile
            // Input Register 0 farklı hücrelerdir.
            InputRegisters = new ushort[registerCount];

            // Coil (0x) okunabilir/yazılabilir bit alanıdır.
            Coils = new bool[bitCount];

            // Discrete Input (1x) normalde sadece okunur bit alanıdır.
            // Emülatörde sensör durumunu taklit etmek için UI tarafından değiştirilebilir.
            DiscreteInputs = new bool[bitCount];

            // Uygulamanın ilk çalışmasında görülecek örnek test değerleri.
            if (registerCount >= 3)
            {
                HoldingRegisters[0] = 1234;
                HoldingRegisters[1] = 5678;
                HoldingRegisters[2] = 42;

                InputRegisters[0] = 25;
                InputRegisters[1] = 50;
                InputRegisters[2] = 75;
            }

            // Float32 örneği: 0x42F6 0xE979 = IEEE 754 ile 123.456
            if (registerCount >= 12)
            {
                HoldingRegisters[10] = 0x42F6;
                HoldingRegisters[11] = 0xE979;
            }

            if (bitCount >= 4)
            {
                Coils[0] = true;
                Coils[1] = false;
                Coils[2] = true;
                Coils[3] = false;

                DiscreteInputs[0] = false;
                DiscreteInputs[1] = true;
                DiscreteInputs[2] = true;
                DiscreteInputs[3] = false;
            }

            // LiBat BMS / STM32 simülasyonu için resmi register map örneklerini
            // aynı Holding Register hafızasına yükle. LiBat registerları 88..154
            // aralığında olduğu için varsayılan registerCount artık 256'dır.
            LiBatBmsProfile.Apply(this);
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
