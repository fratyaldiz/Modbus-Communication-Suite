using System;
using System.Collections.Generic;

using Modbus.Protocol.Functions;
using Modbus.Protocol.Helpers;

namespace Modbus.Protocol.Builders
{
    /// <summary>
    /// Modbus isteğini oluşturan (byte dizisine çeviren) sınıf.
    ///
    /// ÇOK ÖNEMLİ KAVRAM: Modbus'ta paket iki katmandan oluşur.
    ///
    /// 1) PDU (Protocol Data Unit): İçerik. [FonksiyonKodu][Veri...]
    ///    Bu kısım TCP'de de RTU'da da AYNIDIR.
    ///
    /// 2) ADU (Application Data Unit): PDU'nun paketlenmiş hali. Zarf.
    ///    Burası TCP ve RTU'da FARKLIDIR:
    ///      - RTU: [SlaveId][PDU][CRC-Low][CRC-High]           (seri hat / kablo)
    ///      - TCP: [MBAP Header][UnitId][PDU]                  (Ethernet / IP)
    ///
    /// Eski kod TCP için de CRC ekliyordu; bu YANLIŞTI. Modbus TCP'de CRC yoktur,
    /// onun yerine "MBAP Header" vardır. Bu sınıf ikisini doğru şekilde ayırır.
    /// </summary>
    public class PacketBuilder
    {
        // ================================================================
        // ADIM 1: PDU (içerik) oluşturan yardımcılar
        // ================================================================

        /// <summary>
        /// "Oku" tipi istekler için PDU üretir (Read Coils / Read Holding Register vb.).
        /// Yapısı: [FonksiyonKodu][BaşlangıçAdresi Hi][Lo][Adet Hi][Lo]
        /// </summary>
        /// <param name="function">Hangi okuma fonksiyonu (01,02,03,04).</param>
        /// <param name="startAddress">İlk register/coil adresi.</param>
        /// <param name="quantity">Kaç adet okunacak.</param>
        public byte[] BuildReadPdu(ModbusFunctionCode function, ushort startAddress, ushort quantity)
        {
            // List<byte> kullanıyoruz çünkü baytları tek tek eklemek kolay olsun.
            List<byte> pdu = new();

            // 1. byte: fonksiyon kodu (enum'u byte'a çeviriyoruz).
            pdu.Add((byte)function);

            // 2-3. byte: başlangıç adresi. 16 bitlik sayıyı iki byte'a bölüyoruz.
            // ">> 8" = yüksek 8 biti al (High byte). Modbus "Big-Endian" ister:
            // yani önce yüksek byte, sonra düşük byte gelir.
            pdu.Add((byte)(startAddress >> 8)); // High byte
            pdu.Add((byte)(startAddress));      // Low byte  (byte'a çevirince alt 8 bit kalır)

            // 4-5. byte: adet (kaç tane okunacak), yine High + Low.
            pdu.Add((byte)(quantity >> 8));
            pdu.Add((byte)(quantity));

            return pdu.ToArray();
        }

        /// <summary>
        /// "Tek değer yaz" istekleri için PDU üretir (Write Single Register / Write Single Coil).
        /// Yapısı: [FonksiyonKodu][Adres Hi][Lo][Değer Hi][Lo]
        /// (Yazmada son iki byte "adet" değil "yazılacak değer"dir. Yapı aynı, anlam farklı.)
        /// </summary>
        public byte[] BuildWriteSinglePdu(ModbusFunctionCode function, ushort address, ushort value)
        {
            List<byte> pdu = new();
            pdu.Add((byte)function);
            pdu.Add((byte)(address >> 8));
            pdu.Add((byte)(address));
            pdu.Add((byte)(value >> 8));
            pdu.Add((byte)(value));
            return pdu.ToArray();
        }

        /// <summary>
        /// FC16 Write Multiple Registers isteği için PDU üretir.
        /// Yapı:
        /// [0x10][Başlangıç Hi][Lo][Adet Hi][Lo][Byte Count][Değer1 Hi][Lo]...
        /// </summary>
        public byte[] BuildWriteMultipleRegistersPdu(
            ushort startAddress,
            IReadOnlyList<ushort> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            if (values.Count < 1 || values.Count > 123)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(values),
                    "FC16 ile tek istekte 1 ile 123 register yazılabilir.");
            }

            List<byte> pdu = new();
            pdu.Add((byte)ModbusFunctionCode.WriteMultipleRegisters);
            pdu.Add((byte)(startAddress >> 8));
            pdu.Add((byte)startAddress);

            ushort quantity = (ushort)values.Count;
            pdu.Add((byte)(quantity >> 8));
            pdu.Add((byte)quantity);
            pdu.Add((byte)(quantity * 2));

            foreach (ushort value in values)
            {
                pdu.Add((byte)(value >> 8));
                pdu.Add((byte)value);
            }

            return pdu.ToArray();
        }

        // ================================================================
        // ADIM 2: PDU'yu zarfa koy (RTU veya TCP)
        // ================================================================

        /// <summary>
        /// PDU'yu RTU çerçevesine sarar: [SlaveId][PDU][CRC-Low][CRC-High]
        /// Seri port (COM / RS485) haberleşmesinde bu kullanılır.
        /// </summary>
        public byte[] WrapRtu(byte slaveId, byte[] pdu)
        {
            List<byte> frame = new();

            // Başa cihaz adresini koy.
            frame.Add(slaveId);

            // Sonra PDU (içerik).
            frame.AddRange(pdu);

            // CRC'yi şimdiye kadar eklediğimiz TÜM baytlar üzerinden hesapla.
            ushort crc = CRC16.Calculate(frame.ToArray());

            // Modbus RTU CRC'yi "önce düşük byte, sonra yüksek byte" ister (Little-Endian!).
            // Dikkat: adres Big-Endian'dı ama CRC Little-Endian'dır. Standart böyle.
            frame.Add((byte)(crc & 0xFF)); // Low byte
            frame.Add((byte)(crc >> 8));   // High byte

            return frame.ToArray();
        }

        /// <summary>
        /// PDU'yu TCP (MBAP) çerçevesine sarar.
        /// MBAP Header = Modbus Application Protocol header, 7 byte:
        ///   [Transaction Id Hi][Lo]  -> istek/yanıt eşleştirme numarası
        ///   [Protocol Id Hi][Lo]     -> Modbus için her zaman 0
        ///   [Length Hi][Lo]          -> kendinden SONRAKİ byte sayısı (UnitId + PDU)
        ///   [Unit Id]                -> cihaz adresi (RTU'daki SlaveId'nin karşılığı)
        /// Ardından PDU gelir. TCP'de CRC YOKTUR (TCP kendi doğrulamasını yapar).
        /// </summary>
        public byte[] WrapTcp(ushort transactionId, byte unitId, byte[] pdu)
        {
            List<byte> frame = new();

            // Transaction Id (2 byte): yanıt geldiğinde hangi isteğe ait olduğunu anlarız.
            frame.Add((byte)(transactionId >> 8));
            frame.Add((byte)(transactionId));

            // Protocol Id (2 byte): Modbus için sabit 0x0000.
            frame.Add(0x00);
            frame.Add(0x00);

            // Length (2 byte): bu alandan SONRA gelen byte sayısı = 1 (UnitId) + PDU uzunluğu.
            ushort length = (ushort)(1 + pdu.Length);
            frame.Add((byte)(length >> 8));
            frame.Add((byte)(length));

            // Unit Id (1 byte): hedef cihaz.
            frame.Add(unitId);

            // Son olarak içerik (PDU).
            frame.AddRange(pdu);

            return frame.ToArray();
        }
    }
}
