using System;
using System.Collections.Generic;

using Modbus.Protocol.Functions;

namespace Modbus.Communication.Server
{
    /// <summary>
    /// Bir Modbus SLAVE'in "beyni". Gelen PDU'yu ([FonksiyonKodu][Veri...]) alır,
    /// ortak DataStore üzerinde işlemi yapar ve yanıt PDU'sunu üretir.
    ///
    /// Taşımadan bağımsızdır: RTU server (seri) bu sınıfı kullanır. Kurallar
    /// (fonksiyon kodları, sınır/uzunluk kontrolleri, exception kodları) mevcut
    /// TCP server ile birebir aynıdır; böylece iki taşıma da aynı davranır.
    ///
    /// Not: Zarf (RTU CRC veya TCP MBAP) ekleme/çıkarma işi çağıran server'ındır.
    /// </summary>
    public sealed class ModbusRequestHandler
    {
        private readonly ModbusDataStore _dataStore;
        private readonly Action<string>? _log;

        public ModbusRequestHandler(ModbusDataStore dataStore, Action<string>? log = null)
        {
            _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
            _log = log;
        }

        /// <summary>Gelen PDU'yu işleyip yanıt PDU'sunu döndürür.</summary>
        public byte[] Process(byte[] pdu)
        {
            if (pdu == null || pdu.Length == 0)
                return BuildException(0, 0x03); // Illegal Data Value

            byte function = pdu[0];

            return function switch
            {
                (byte)ModbusFunctionCode.ReadHoldingRegisters => ReadHoldingRegisters(pdu, function),
                (byte)ModbusFunctionCode.ReadInputRegisters => ReadInputRegisters(pdu, function),
                (byte)ModbusFunctionCode.ReadCoils => ReadCoils(pdu, function),
                (byte)ModbusFunctionCode.ReadDiscreteInputs => ReadDiscreteInputs(pdu, function),
                (byte)ModbusFunctionCode.WriteSingleRegister => WriteSingleRegister(pdu, function),
                (byte)ModbusFunctionCode.WriteSingleCoil => WriteSingleCoil(pdu, function),
                (byte)ModbusFunctionCode.WriteMultipleRegisters => WriteMultipleRegisters(pdu, function),
                _ => BuildException(function, 0x01) // Illegal Function
            };
        }

        private byte[] ReadHoldingRegisters(byte[] pdu, byte function)
        {
            if (pdu.Length != 5) return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);

            if (quantity is < 1 or > 125) return BuildException(function, 0x03);
            if (!_dataStore.IsValidRegisterRange(startAddress, quantity)) return BuildException(function, 0x02);

            List<byte> response = new(2 + quantity * 2) { function, (byte)(quantity * 2) };
            for (int i = 0; i < quantity; i++)
            {
                ushort value = _dataStore.GetHoldingRegister(startAddress + i);
                response.Add((byte)(value >> 8));
                response.Add((byte)value);
            }

            _log?.Invoke($"[RTU Slave] FC03: adres {startAddress}, adet {quantity} okundu.");
            return response.ToArray();
        }

        private byte[] ReadInputRegisters(byte[] pdu, byte function)
        {
            if (pdu.Length != 5) return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);

            if (quantity is < 1 or > 125) return BuildException(function, 0x03);
            if (!_dataStore.IsValidInputRegisterRange(startAddress, quantity)) return BuildException(function, 0x02);

            List<byte> response = new(2 + quantity * 2) { function, (byte)(quantity * 2) };
            for (int i = 0; i < quantity; i++)
            {
                ushort value = _dataStore.GetInputRegister(startAddress + i);
                response.Add((byte)(value >> 8));
                response.Add((byte)value);
            }

            _log?.Invoke($"[RTU Slave] FC04: input register {startAddress}, adet {quantity} okundu.");
            return response.ToArray();
        }

        private byte[] ReadCoils(byte[] pdu, byte function)
        {
            if (pdu.Length != 5) return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);

            if (quantity is < 1 or > 2000) return BuildException(function, 0x03);
            if (!_dataStore.IsValidCoilRange(startAddress, quantity)) return BuildException(function, 0x02);

            int byteCount = (quantity + 7) / 8;
            byte[] coilBytes = new byte[byteCount];
            for (int i = 0; i < quantity; i++)
            {
                if (_dataStore.GetCoil(startAddress + i))
                    coilBytes[i / 8] |= (byte)(1 << (i % 8));
            }

            List<byte> response = new(2 + byteCount) { function, (byte)byteCount };
            response.AddRange(coilBytes);
            _log?.Invoke($"[RTU Slave] FC01: adres {startAddress}, adet {quantity} okundu.");
            return response.ToArray();
        }

        private byte[] ReadDiscreteInputs(byte[] pdu, byte function)
        {
            if (pdu.Length != 5) return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);

            if (quantity is < 1 or > 2000) return BuildException(function, 0x03);
            if (!_dataStore.IsValidDiscreteInputRange(startAddress, quantity)) return BuildException(function, 0x02);

            int byteCount = (quantity + 7) / 8;
            byte[] inputBytes = new byte[byteCount];
            for (int i = 0; i < quantity; i++)
            {
                if (_dataStore.GetDiscreteInput(startAddress + i))
                    inputBytes[i / 8] |= (byte)(1 << (i % 8));
            }

            List<byte> response = new(2 + byteCount) { function, (byte)byteCount };
            response.AddRange(inputBytes);
            _log?.Invoke($"[RTU Slave] FC02: discrete input {startAddress}, adet {quantity} okundu.");
            return response.ToArray();
        }

        private byte[] WriteSingleRegister(byte[] pdu, byte function)
        {
            if (pdu.Length != 5) return BuildException(function, 0x03);

            ushort address = ReadUInt16(pdu, 1);
            ushort value = ReadUInt16(pdu, 3);

            if (!_dataStore.IsValidRegisterRange(address, 1)) return BuildException(function, 0x02);

            _dataStore.SetHoldingRegister(address, value);
            _log?.Invoke($"[RTU Slave] FC06: Register[{address}] = {value} yazıldı.");
            return pdu; // yazma cevabı isteğin aynısıdır (echo)
        }

        private byte[] WriteSingleCoil(byte[] pdu, byte function)
        {
            if (pdu.Length != 5) return BuildException(function, 0x03);

            ushort address = ReadUInt16(pdu, 1);
            ushort rawValue = ReadUInt16(pdu, 3);

            if (rawValue != 0xFF00 && rawValue != 0x0000) return BuildException(function, 0x03);
            if (!_dataStore.IsValidCoilRange(address, 1)) return BuildException(function, 0x02);

            bool value = rawValue == 0xFF00;
            _dataStore.SetCoil(address, value);
            _log?.Invoke($"[RTU Slave] FC05: Coil[{address}] = {value} yazıldı.");
            return pdu;
        }

        private byte[] WriteMultipleRegisters(byte[] pdu, byte function)
        {
            if (pdu.Length < 8) return BuildException(function, 0x03);

            ushort startAddress = ReadUInt16(pdu, 1);
            ushort quantity = ReadUInt16(pdu, 3);
            int byteCount = pdu[5];

            if (quantity is < 1 or > 123) return BuildException(function, 0x03);
            if (byteCount != quantity * 2) return BuildException(function, 0x03);
            if (pdu.Length != 6 + byteCount) return BuildException(function, 0x03);
            if (!_dataStore.IsValidRegisterRange(startAddress, quantity)) return BuildException(function, 0x02);

            for (int i = 0; i < quantity; i++)
            {
                ushort value = ReadUInt16(pdu, 6 + (i * 2));
                _dataStore.SetHoldingRegister(startAddress + i, value);
            }

            _log?.Invoke($"[RTU Slave] FC16: adres {startAddress}, adet {quantity} register yazıldı.");

            return new byte[]
            {
                function,
                (byte)(startAddress >> 8), (byte)startAddress,
                (byte)(quantity >> 8), (byte)quantity
            };
        }

        private static byte[] BuildException(byte function, byte exceptionCode)
            => new[] { (byte)(function | 0x80), exceptionCode };

        private static ushort ReadUInt16(byte[] data, int index)
            => (ushort)((data[index] << 8) | data[index + 1]);
    }
}
