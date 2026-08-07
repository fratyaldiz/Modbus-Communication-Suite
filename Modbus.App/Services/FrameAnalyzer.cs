using System.Collections.ObjectModel;

using Modbus.App.Models;
using Modbus.Protocol.Helpers;

namespace Modbus.App.Services
{
    /// <summary>
    /// Bir Modbus çerçevesini byte byte çözümler (Communication Traffic paneli için).
    /// Her byte'ın HEX/DEC/BINARY gösterimini ve protokoldeki ANLAMINI üretir.
    /// Hem Master (TX=istek, RX=cevap) hem Slave (RX=istek, TX=cevap) kullanır.
    /// </summary>
    public static class FrameAnalyzer
    {
        public static void Fill(ObservableCollection<FrameByteItem> target, byte[] frame, bool isTcp, bool isResponse)
        {
            target.Clear();
            if (frame == null) return;

            for (int i = 0; i < frame.Length; i++)
            {
                target.Add(new FrameByteItem
                {
                    Offset = i,
                    Hex = $"0x{frame[i]:X2}",
                    Decimal = frame[i],
                    Binary = DataConverter.ToBinary(frame[i]),
                    Meaning = Describe(i, frame, isTcp, isResponse)
                });
            }
        }

        private static string Describe(int index, byte[] frame, bool isTcp, bool isResponse)
        {
            if (isTcp)
            {
                switch (index)
                {
                    case 0: return "MBAP — Transaction ID (yüksek byte)";
                    case 1: return "MBAP — Transaction ID (düşük byte)";
                    case 2: return "MBAP — Protocol ID (yüksek byte), Modbus'ta 0";
                    case 3: return "MBAP — Protocol ID (düşük byte), Modbus'ta 0";
                    case 4: return "MBAP — Length (yüksek byte)";
                    case 5: return "MBAP — Length (düşük byte), sonraki byte sayısı";
                    case 6: return "MBAP — Unit ID (hedef cihaz)";
                    case 7: return "PDU — Function Code";
                }
                return DescribePdu(index - 8, frame.Length > 7 ? frame[7] : (byte)0, isResponse);
            }

            // RTU: [Slave ID][Function][Veri...][CRC Low][CRC High]
            if (index == 0) return "RTU — Slave ID (hedef cihaz adresi)";
            if (index == 1) return "PDU — Function Code";
            if (index == frame.Length - 2) return "RTU — CRC (düşük byte)";
            if (index == frame.Length - 1) return "RTU — CRC (yüksek byte)";
            return DescribePdu(index - 2, frame.Length > 1 ? frame[1] : (byte)0, isResponse);
        }

        private static string DescribePdu(int dataIndex, byte functionCode, bool isResponse)
        {
            if ((functionCode & 0x80) != 0)
                return dataIndex == 0 ? "Exception Code (hata sebebi)" : "Hata cevabı verisi";

            if (!isResponse)
            {
                if (functionCode == 0x10)
                {
                    return dataIndex switch
                    {
                        0 => "İstek — Başlangıç adresi (yüksek byte)",
                        1 => "İstek — Başlangıç adresi (düşük byte)",
                        2 => "İstek — Yazılacak register adedi (yüksek byte)",
                        3 => "İstek — Yazılacak register adedi (düşük byte)",
                        4 => "İstek — Byte Count (adet × 2)",
                        _ => $"İstek — Register {(dataIndex - 5) / 2} ({(((dataIndex - 5) % 2 == 0) ? "yüksek" : "düşük")} byte)"
                    };
                }

                bool isSingleWrite = functionCode == 0x05 || functionCode == 0x06;
                return dataIndex switch
                {
                    0 => "İstek — Start Address (yüksek byte)",
                    1 => "İstek — Start Address (düşük byte)",
                    2 => isSingleWrite
                        ? (functionCode == 0x05 ? "İstek — Coil değeri (yüksek byte: FF=ON, 00=OFF)" : "İstek — Yazılacak değer (yüksek byte)")
                        : "İstek — Quantity (yüksek byte)",
                    3 => isSingleWrite
                        ? (functionCode == 0x05 ? "İstek — Coil değeri (düşük byte, her zaman 00)" : "İstek — Yazılacak değer (düşük byte)")
                        : "İstek — Quantity (düşük byte)",
                    _ => "İstek verisi"
                };
            }

            if (functionCode == 0x05 || functionCode == 0x06 || functionCode == 0x10)
            {
                return dataIndex switch
                {
                    0 => "Cevap — Adres/başlangıç (yüksek byte)",
                    1 => "Cevap — Adres/başlangıç (düşük byte)",
                    2 => "Cevap — Değer/adet (yüksek byte)",
                    3 => "Cevap — Değer/adet (düşük byte)",
                    _ => "Cevap verisi"
                };
            }

            if (dataIndex == 0) return "Cevap — Byte Count (kaç byte veri geliyor)";

            if (functionCode == 0x01 || functionCode == 0x02)
                return $"Cevap — Paketlenmiş bit verisi (byte {dataIndex - 1})";

            int registerIndex = (dataIndex - 1) / 2;
            bool isHighByte = (dataIndex - 1) % 2 == 0;
            return $"Cevap — Register {registerIndex} ({(isHighByte ? "yüksek" : "düşük")} byte)";
        }
    }
}
