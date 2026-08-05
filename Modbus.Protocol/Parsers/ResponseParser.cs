using System;
using System.Collections.Generic;

using Modbus.Protocol.Helpers;
using Modbus.Protocol.Packets;

namespace Modbus.Protocol.Parsers
{
    /// <summary>
    /// Karşı cihazdan gelen ham yanıt baytlarını "anlamlı" hale getiren sınıf.
    ///
    /// İstek gönderdik, cihaz bize byte dizisi döndü. Ama bu baytlar kendiliğinden
    /// bir şey ifade etmez. Burada onları çözüyoruz (parse ediyoruz):
    ///  - hata var mı? (exception response)
    ///  - okunan register değerleri neler?
    /// </summary>
    public class ResponseParser
    {
        /// <summary>
        /// TCP yanıtını çözer. TCP yanıtı da MBAP header ile başlar:
        /// [TxId Hi][Lo][Proto Hi][Lo][Len Hi][Lo][UnitId][FunctionCode][Veri...]
        /// Yani ilk 6 byte header, 7. byte UnitId, 8. byte fonksiyon kodudur.
        /// </summary>
        public ModbusPacket ParseTcpResponse(byte[] response)
        {
            // En az 8 byte olmalı (7 MBAP + en az 1 fonksiyon kodu). Yoksa bozuk.
            if (response == null || response.Length < 8)
                throw new Exception("TCP yanıtı çok kısa / bozuk.");

            ModbusPacket packet = new();
            packet.RawData = response;

            packet.TransactionId = (ushort)((response[0] << 8) | response[1]);
            packet.ProtocolId = (ushort)((response[2] << 8) | response[3]);
            packet.Length = (ushort)((response[4] << 8) | response[5]);

            if (packet.ProtocolId != 0)
                throw new Exception("Geçersiz Modbus TCP Protocol ID.");

            if (packet.Length != response.Length - 6)
                throw new Exception("Modbus TCP Length alanı ile gerçek paket uzunluğu uyuşmuyor.");

            // MBAP'te UnitId 7. bytetır (index 6).
            packet.SlaveId = response[6];

            // Fonksiyon kodu 8. bytetır (index 7).
            packet.FunctionCode = response[7];

            // 8. byteten sonrası "veri" kısmıdır. Onu ayrı diziye kopyalıyoruz.
            int dataLength = response.Length - 8;
            packet.Data = new byte[dataLength];
            Array.Copy(response, 8, packet.Data, 0, dataLength);

            return packet;
        }

        /// <summary>
        /// RTU yanıtını çözer. RTU yanıtı:
        /// [SlaveId][FunctionCode][Veri...][CRC-Low][CRC-High]
        /// İlk byte adres, ikinci byte fonksiyon, sondaki 2 byte CRC.
        /// </summary>
        public ModbusPacket ParseRtuResponse(byte[] response)
        {
            // En kısa geçerli Modbus RTU hata cevabı 5 bytedır:
            // [Unit][Function|0x80][Exception][CRC Low][CRC High]
            if (response == null || response.Length < 5)
                throw new Exception("RTU yanıtı çok kısa / bozuk.");

            int payloadLength = response.Length - 2;
            byte[] withoutCrc = new byte[payloadLength];
            Array.Copy(response, withoutCrc, payloadLength);

            ushort calculatedCrc = CRC16.Calculate(withoutCrc);
            ushort receivedCrc = (ushort)(response[^2] | (response[^1] << 8));

            if (calculatedCrc != receivedCrc)
            {
                throw new Exception(
                    $"RTU CRC hatası. Hesaplanan: 0x{calculatedCrc:X4}, " +
                    $"gelen: 0x{receivedCrc:X4}.");
            }

            ModbusPacket packet = new();
            packet.RawData = response;
            packet.SlaveId = response[0];
            packet.FunctionCode = response[1];

            // Veri = baştan 2 (adres+fonksiyon), sondan 2 (CRC) çıkar.
            int dataLength = response.Length - 4;
            packet.Data = new byte[dataLength];
            Array.Copy(response, 2, packet.Data, 0, dataLength);

            return packet;
        }

        /// <summary>
        /// Bir "Read Holding/Input Register" yanıtındaki register değerlerini döndürür.
        ///
        /// Bu yanıtın "Data" kısmının yapısı şöyledir:
        ///   [ByteCount][Reg1 Hi][Reg1 Lo][Reg2 Hi][Reg2 Lo]...
        /// İlk byte kaç byte veri geldiğini söyler; sonra her register 2 byte'tır.
        /// </summary>
        public ushort[] ReadRegisterValues(ModbusPacket packet)
        {
            // Data boşsa okunacak bir şey yok.
            if (packet.Data.Length < 1)
                return Array.Empty<ushort>();

            // İlk byte: toplam veri byte sayısı.
            int byteCount = packet.Data[0];

            if (byteCount == 0 || byteCount % 2 != 0)
                throw new Exception("Register cevabındaki Byte Count geçersiz.");

            if (packet.Data.Length != byteCount + 1)
            {
                throw new Exception(
                    $"Register cevabı eksik veya fazla veri içeriyor. " +
                    $"Byte Count: {byteCount}, gerçek veri: {packet.Data.Length - 1} byte.");
            }

            // Her register 2 byte olduğu için register sayısı = byteCount / 2.
            int registerCount = byteCount / 2;
            ushort[] values = new ushort[registerCount];

            for (int i = 0; i < registerCount; i++)
            {
                // Data[0] byteCount olduğu için gerçek veriler index 1'den başlar.
                int hiIndex = 1 + (i * 2);     // yüksek byte
                int loIndex = hiIndex + 1;     // düşük byte

                // İki byte'ı 16 bitlik tek sayıya birleştir (Big-Endian):
                // yüksek byte'ı 8 bit sola kaydır, düşük byte'ı ekle.
                values[i] = (ushort)((packet.Data[hiIndex] << 8) | packet.Data[loIndex]);
            }

            return values;
        }

        /// <summary>
        /// FC01/FC02 bit okuma cevabını bool dizisine çevirir.
        /// Bitler Modbus kuralına göre her byte içinde düşük bitten yüksek bite doğru dizilir.
        /// </summary>
        public bool[] ReadBitValues(ModbusPacket packet, int requestedQuantity)
        {
            if (requestedQuantity < 1 || requestedQuantity > 2000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedQuantity),
                    "Bit okuma miktarı 1 ile 2000 arasında olmalıdır.");
            }

            if (packet.Data.Length < 1)
                throw new Exception("Bit okuma cevabında Byte Count alanı yok.");

            int byteCount = packet.Data[0];
            int expectedByteCount = (requestedQuantity + 7) / 8;

            if (byteCount != expectedByteCount)
            {
                throw new Exception(
                    $"Bit cevabındaki Byte Count geçersiz. " +
                    $"Beklenen: {expectedByteCount}, gelen: {byteCount}.");
            }

            if (packet.Data.Length != byteCount + 1)
            {
                throw new Exception(
                    $"Bit cevabı eksik veya fazla veri içeriyor. " +
                    $"Byte Count: {byteCount}, gerçek veri: {packet.Data.Length - 1} byte.");
            }

            bool[] values = new bool[requestedQuantity];

            for (int i = 0; i < requestedQuantity; i++)
            {
                int byteIndex = 1 + (i / 8);
                int bitIndex = i % 8;
                values[i] = (packet.Data[byteIndex] & (1 << bitIndex)) != 0;
            }

            return values;
        }

        /// <summary>
        /// FC05/FC06 cevaplarındaki adres ve değeri doğrular.
        /// </summary>
        public (ushort Address, ushort Value) ReadWriteSingleConfirmation(ModbusPacket packet)
        {
            if (packet.Data.Length != 4)
                throw new Exception("Tekli yazma onay cevabının veri uzunluğu 4 byte olmalıdır.");

            ushort address = (ushort)((packet.Data[0] << 8) | packet.Data[1]);
            ushort value = (ushort)((packet.Data[2] << 8) | packet.Data[3]);
            return (address, value);
        }

        /// <summary>
        /// FC16 cevabındaki başlangıç adresi ve yazılan register sayısını döndürür.
        /// </summary>
        public (ushort StartAddress, ushort Quantity) ReadWriteMultipleConfirmation(
            ModbusPacket packet)
        {
            if (packet.Data.Length != 4)
                throw new Exception("FC16 onay cevabının veri uzunluğu 4 byte olmalıdır.");

            ushort startAddress = (ushort)((packet.Data[0] << 8) | packet.Data[1]);
            ushort quantity = (ushort)((packet.Data[2] << 8) | packet.Data[3]);
            return (startAddress, quantity);
        }

        /// <summary>
        /// Yanıtın bir HATA yanıtı olup olmadığını söyler.
        /// Modbus'ta hata olduğunda cihaz, fonksiyon kodunun en yüksek bitini 1 yapar.
        /// Yani orijinal koda 0x80 eklenir (örn. 0x03 -> 0x83).
        /// </summary>
        public bool IsErrorResponse(ModbusPacket packet)
        {
            // 0x80 = 1000 0000. Bu bit set ise hata var demektir.
            return (packet.FunctionCode & 0x80) != 0;
        }
    }
}