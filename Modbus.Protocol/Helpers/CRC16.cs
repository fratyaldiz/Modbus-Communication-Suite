using System;

namespace Modbus.Protocol.Helpers
{
    /// <summary>
    /// Modbus RTU çerçevelerinde kullanılan CRC-16 hesaplayıcı.
    /// CRC = "Cyclic Redundancy Check" (Döngüsel Artıklık Denetimi).
    /// Amacı: gönderilen paketin yolda bozulup bozulmadığını anlamak.
    /// Gönderen taraf CRC hesaplar ve pakete ekler; alan taraf aynı hesabı
    /// yapar, sonuç aynıysa paket sağlamdır.
    ///
    /// "static" sınıf: nesne oluşturmadan CRC16.Calculate(...) diye çağırılır.
    /// </summary>
    public static class CRC16
    {
        /// <summary>
        /// Verilen byte dizisi için Modbus CRC-16 değerini hesaplar.
        /// </summary>
        public static ushort Calculate(byte[] data)
        {
            // Modbus standardı CRC'yi 0xFFFF (tüm bitler 1) ile başlatmayı söyler.
            ushort crc = 0xFFFF;

            // Paketteki her byte'ı tek tek işliyoruz.
            foreach (byte b in data)
            {
                // XOR: byte'ı CRC'nin alt 8 bitine karıştır.
                crc ^= b;

                // Her byte için 8 bit boyunca kaydırma yapıyoruz.
                for (int i = 0; i < 8; i++)
                {
                    // En düşük bit (LSB) 1 mi? (crc & 1)
                    if ((crc & 1) != 0)
                    {
                        // 1 ise: bir bit sağa kaydır, sonra Modbus polinomu 0xA001 ile XOR'la.
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        // 0 ise: sadece bir bit sağa kaydır.
                        crc >>= 1;
                    }
                }
            }

            // Sonuç 16 bitlik CRC. Modbus RTU'da paketin sonuna
            // önce düşük byte (Low), sonra yüksek byte (High) olarak eklenir.
            return crc;
        }
    }
}
