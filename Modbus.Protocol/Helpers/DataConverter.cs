using System;
using System.Globalization;
using System.Text;

namespace Modbus.Protocol.Helpers
{
    /// <summary>
    /// Modbus register'ları 16 bitlik kutulardır. 32 veya 64 bitlik bir sayıyı
    /// (Long / Float / Double) taşımak için birden fazla register yan yana dizilir.
    ///
    /// Ama hangi register önce gelir, her register'ın hangi byte'ı önce gelir?
    /// Bunun tek bir standardı YOKTUR; cihaz üreticisine göre değişir.
    /// Bu yüzden Modbus araçları 4 olasılığı birden gösterir.
    ///
    /// İsimlendirme: register'ların ham byte'larına sırayla A, B, C, D... denir.
    ///   Register0 = [A][B]     Register1 = [C][D]
    ///
    ///   ABCD : hiç değiştirme. (Big-Endian / "yüksek word önce")
    ///   CDAB : word'leri (register'ları) ters çevir.
    ///   BADC : her word'ün İÇİNDEKİ iki byte'ı ters çevir.
    ///   DCBA : hepsini tamamen ters çevir. (Little-Endian)
    ///
    /// 64 bitte de aynı kural geçerlidir; sadece isim uzar:
    ///   AB CD EF GH / GH EF CD AB / BA DC FE HG / HG FE DC BA
    /// </summary>
    public enum RegisterByteOrder
    {
        ABCD = 0,
        CDAB = 1,
        BADC = 2,
        DCBA = 3
    }

    /// <summary>
    /// Ham register değerlerini insanın okuyabileceği veri tiplerine çevirir
    /// ve IEEE 754 kayan nokta sayılarının bit yapısını açıklar.
    /// </summary>
    public static class DataConverter
    {
        /// <summary>Ekranda gösterilecek byte sırası seçenekleri (32 bit için).</summary>
        public static readonly string[] ByteOrderNames32 = { "AB CD", "CD AB", "BA DC", "DC BA" };

        /// <summary>Ekranda gösterilecek byte sırası seçenekleri (64 bit için).</summary>
        public static readonly string[] ByteOrderNames64 =
        {
            "AB CD EF GH", "GH EF CD AB", "BA DC FE HG", "HG FE DC BA"
        };

        /// <summary>"AB CD" gibi bir metni enum'a çevirir. Tanımadığını ABCD sayar.</summary>
        public static RegisterByteOrder ParseByteOrder(string? text)
        {
            string key = (text ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

            return key switch
            {
                "ABCD" or "ABCDEFGH" => RegisterByteOrder.ABCD,
                "CDAB" or "GHEFCDAB" => RegisterByteOrder.CDAB,
                "BADC" or "BADCFEHG" => RegisterByteOrder.BADC,
                "DCBA" or "HGFEDCBA" => RegisterByteOrder.DCBA,
                _ => RegisterByteOrder.ABCD
            };
        }

        // ================================================================
        // ADIM 1: Register dizisini byte dizisine çevir
        // ================================================================

        /// <summary>
        /// Her register'ı Modbus'ın istediği gibi (önce yüksek byte) 2 byte'a açar.
        /// Örnek: [0x1234, 0xABCD] -> [0x12, 0x34, 0xAB, 0xCD]
        /// </summary>
        public static byte[] ToBigEndianBytes(ushort[] registers)
        {
            if (registers == null)
                return Array.Empty<byte>();

            byte[] bytes = new byte[registers.Length * 2];

            for (int i = 0; i < registers.Length; i++)
            {
                bytes[(i * 2)] = (byte)(registers[i] >> 8);   // yüksek byte
                bytes[(i * 2) + 1] = (byte)(registers[i] & 0xFF); // düşük byte
            }

            return bytes;
        }

        /// <summary>
        /// Byte'ları seçilen sıraya göre yeniden dizer.
        /// Sonuç HER ZAMAN "en anlamlı byte başta" (big-endian) olarak yorumlanır.
        /// </summary>
        public static byte[] Reorder(ushort[] registers, RegisterByteOrder order)
        {
            byte[] bytes = ToBigEndianBytes(registers);

            return order switch
            {
                RegisterByteOrder.ABCD => bytes,
                RegisterByteOrder.CDAB => SwapWords(bytes),
                RegisterByteOrder.BADC => SwapBytesInsideWords(bytes),
                RegisterByteOrder.DCBA => ReverseAll(bytes),
                _ => bytes
            };
        }

        /// <summary>Register (word) sırasını ters çevirir: [AB][CD] -> [CD][AB]</summary>
        private static byte[] SwapWords(byte[] bytes)
        {
            byte[] result = new byte[bytes.Length];
            int wordCount = bytes.Length / 2;

            for (int i = 0; i < wordCount; i++)
            {
                int source = (wordCount - 1 - i) * 2;
                result[i * 2] = bytes[source];
                result[(i * 2) + 1] = bytes[source + 1];
            }

            return result;
        }

        /// <summary>Her word'ün içindeki iki byte'ı takas eder: [AB] -> [BA]</summary>
        private static byte[] SwapBytesInsideWords(byte[] bytes)
        {
            byte[] result = new byte[bytes.Length];

            for (int i = 0; i < bytes.Length; i += 2)
            {
                result[i] = bytes[i + 1];
                result[i + 1] = bytes[i];
            }

            return result;
        }

        /// <summary>Tüm byte dizisini baştan sona ters çevirir.</summary>
        private static byte[] ReverseAll(byte[] bytes)
        {
            byte[] result = (byte[])bytes.Clone();
            Array.Reverse(result);
            return result;
        }

        /// <summary>
        /// BitConverter bu makinenin doğal byte sırasını kullanır (Intel/AMD = little-endian).
        /// Elimizdeki dizi big-endian olduğu için gerekiyorsa ters çeviriyoruz.
        /// </summary>
        private static byte[] ToMachineOrder(byte[] bigEndian)
        {
            if (!BitConverter.IsLittleEndian)
                return bigEndian;

            byte[] copy = (byte[])bigEndian.Clone();
            Array.Reverse(copy);
            return copy;
        }

        // ================================================================
        // ADIM 2: Sayıya çevirme
        // ================================================================

        public static ushort ToUInt16(ushort register) => register;

        public static short ToInt16(ushort register) => unchecked((short)register);

        public static uint ToUInt32(ushort[] registers, RegisterByteOrder order)
        {
            byte[] b = ToMachineOrder(Reorder(registers, order));
            return BitConverter.ToUInt32(b, 0);
        }

        public static int ToInt32(ushort[] registers, RegisterByteOrder order)
        {
            byte[] b = ToMachineOrder(Reorder(registers, order));
            return BitConverter.ToInt32(b, 0);
        }

        public static float ToFloat32(ushort[] registers, RegisterByteOrder order)
        {
            byte[] b = ToMachineOrder(Reorder(registers, order));
            return BitConverter.ToSingle(b, 0);
        }

        public static ulong ToUInt64(ushort[] registers, RegisterByteOrder order)
        {
            byte[] b = ToMachineOrder(Reorder(registers, order));
            return BitConverter.ToUInt64(b, 0);
        }

        public static long ToInt64(ushort[] registers, RegisterByteOrder order)
        {
            byte[] b = ToMachineOrder(Reorder(registers, order));
            return BitConverter.ToInt64(b, 0);
        }

        public static double ToDouble64(ushort[] registers, RegisterByteOrder order)
        {
            byte[] b = ToMachineOrder(Reorder(registers, order));
            return BitConverter.ToDouble(b, 0);
        }

        /// <summary>
        /// Register'ları ASCII metin olarak okur. Yazdırılamayan karakterler nokta olur.
        /// </summary>
        public static string ToAscii(ushort[] registers, RegisterByteOrder order)
        {
            byte[] bytes = Reorder(registers, order);
            StringBuilder text = new(bytes.Length);

            foreach (byte value in bytes)
                text.Append(value >= 32 && value <= 126 ? (char)value : '.');

            return text.ToString();
        }

        // ================================================================
        // ADIM 3: Gösterim yardımcıları
        // ================================================================

        /// <summary>0x1234 gibi hex metin üretir.</summary>
        public static string ToHex(ushort register) => $"0x{register:X4}";

        /// <summary>Byte dizisini "12 34 AB CD" biçiminde yazar.</summary>
        public static string ToHex(byte[] bytes) =>
            bytes.Length == 0 ? "(boş)" : BitConverter.ToString(bytes).Replace("-", " ");

        /// <summary>16 bitlik değeri "0000 0000 0000 0000" biçiminde yazar.</summary>
        public static string ToBinary(ushort register)
        {
            string bits = Convert.ToString(register, 2).PadLeft(16, '0');
            return GroupInFours(bits);
        }

        /// <summary>Tek byte'ı "0000 0000" biçiminde yazar.</summary>
        public static string ToBinary(byte value)
        {
            string bits = Convert.ToString(value, 2).PadLeft(8, '0');
            return GroupInFours(bits);
        }

        /// <summary>Bitleri okunabilir olsun diye dörderli gruplar.</summary>
        public static string GroupInFours(string bits)
        {
            StringBuilder result = new(bits.Length + (bits.Length / 4));

            for (int i = 0; i < bits.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                    result.Append(' ');

                result.Append(bits[i]);
            }

            return result.ToString();
        }

        /// <summary>float değerini emülatörlerdeki gibi kısa bilimsel gösterimle yazar.</summary>
        public static string Format(float value)
        {
            if (float.IsNaN(value)) return "NaN";
            if (float.IsPositiveInfinity(value)) return "+Sonsuz";
            if (float.IsNegativeInfinity(value)) return "-Sonsuz";

            return value.ToString("G7", CultureInfo.InvariantCulture);
        }

        /// <summary>double değerini yazar.</summary>
        public static string Format(double value)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "+Sonsuz";
            if (double.IsNegativeInfinity(value)) return "-Sonsuz";

            return value.ToString("G15", CultureInfo.InvariantCulture);
        }

        // ================================================================
        // ADIM 4: IEEE 754 bit çözümlemesi
        // ================================================================

        /// <summary>
        /// 32 bitlik (single precision) IEEE 754 sayısını parçalarına ayırır.
        /// Bit dizilimi: [1 bit işaret][8 bit üs][23 bit mantis]
        /// </summary>
        public static Ieee754Analysis AnalyzeSingle(ushort[] registers, RegisterByteOrder order)
        {
            byte[] bigEndian = Reorder(registers, order);
            uint bits = 0;

            for (int i = 0; i < 4 && i < bigEndian.Length; i++)
                bits = (bits << 8) | bigEndian[i];

            int sign = (int)(bits >> 31);
            int rawExponent = (int)((bits >> 23) & 0xFF);
            uint mantissa = bits & 0x7FFFFF;

            return Build(
                formatName: "Single (32 bit) — 1 işaret + 8 üs + 23 mantis",
                fullBits: Convert.ToString(bits, 2).PadLeft(32, '0'),
                hex: $"0x{bits:X8}",
                sign: sign,
                rawExponent: rawExponent,
                exponentBitCount: 8,
                maxExponent: 0xFF,
                bias: 127,
                mantissaBits: Convert.ToString(mantissa, 2).PadLeft(23, '0'),
                mantissaRaw: mantissa,
                mantissaDivisor: 8388608.0, // 2^23
                value: Format(ToFloat32(registers, order)));
        }

        /// <summary>
        /// 64 bitlik (double precision) IEEE 754 sayısını parçalarına ayırır.
        /// Bit dizilimi: [1 bit işaret][11 bit üs][52 bit mantis]
        /// </summary>
        public static Ieee754Analysis AnalyzeDouble(ushort[] registers, RegisterByteOrder order)
        {
            byte[] bigEndian = Reorder(registers, order);
            ulong bits = 0;

            for (int i = 0; i < 8 && i < bigEndian.Length; i++)
                bits = (bits << 8) | bigEndian[i];

            int sign = (int)(bits >> 63);
            int rawExponent = (int)((bits >> 52) & 0x7FF);
            ulong mantissa = bits & 0xFFFFFFFFFFFFF;

            return Build(
                formatName: "Double (64 bit) — 1 işaret + 11 üs + 52 mantis",
                fullBits: Convert.ToString((long)bits, 2).PadLeft(64, '0'),
                hex: $"0x{bits:X16}",
                sign: sign,
                rawExponent: rawExponent,
                exponentBitCount: 11,
                maxExponent: 0x7FF,
                bias: 1023,
                mantissaBits: Convert.ToString((long)mantissa, 2).PadLeft(52, '0'),
                mantissaRaw: mantissa,
                mantissaDivisor: 4503599627370496.0, // 2^52
                value: Format(ToDouble64(registers, order)));
        }

        private static Ieee754Analysis Build(
            string formatName,
            string fullBits,
            string hex,
            int sign,
            int rawExponent,
            int exponentBitCount,
            int maxExponent,
            int bias,
            string mantissaBits,
            ulong mantissaRaw,
            double mantissaDivisor,
            string value)
        {
            // Sayının hangi sınıfa girdiğini belirle.
            string category;
            bool hasHiddenOne;
            int actualExponent;

            if (rawExponent == 0 && mantissaRaw == 0)
            {
                category = "Sıfır (tüm üs ve mantis bitleri 0)";
                hasHiddenOne = false;
                actualExponent = 1 - bias;
            }
            else if (rawExponent == 0)
            {
                category = "Denormal / subnormal (üs 0, gizli 1 yok — çok küçük sayı)";
                hasHiddenOne = false;
                actualExponent = 1 - bias;
            }
            else if (rawExponent == maxExponent && mantissaRaw == 0)
            {
                category = "Sonsuz (üs bitleri tamamen 1, mantis 0)";
                hasHiddenOne = false;
                actualExponent = 0;
            }
            else if (rawExponent == maxExponent)
            {
                category = "NaN — sayı değil (üs bitleri tamamen 1, mantis 0 değil)";
                hasHiddenOne = false;
                actualExponent = 0;
            }
            else
            {
                category = "Normal sayı (gizli 1 var)";
                hasHiddenOne = true;
                actualExponent = rawExponent - bias;
            }

            double fraction = (hasHiddenOne ? 1.0 : 0.0) + (mantissaRaw / mantissaDivisor);

            string formula = rawExponent == maxExponent
                ? "Özel değer — normal formül uygulanmaz."
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "(-1)^{0} x {1} x 2^({2}) = {3}",
                    sign,
                    fraction.ToString("G10", CultureInfo.InvariantCulture),
                    actualExponent,
                    value);

            return new Ieee754Analysis
            {
                FormatName = formatName,
                SignBit = sign.ToString(),
                SignMeaning = sign == 0 ? "Pozitif (+)" : "Negatif (-)",
                ExponentBits = GroupInFours(fullBits.Substring(1, exponentBitCount)),
                RawExponent = rawExponent,
                Bias = bias,
                ActualExponent = actualExponent,
                MantissaBits = GroupInFours(mantissaBits),
                MantissaFraction = fraction.ToString("G10", CultureInfo.InvariantCulture),
                Category = category,
                Formula = formula,
                FullBinary = GroupInFours(fullBits),
                Hex = hex,
                Value = value
            };
        }
    }

    /// <summary>Bir IEEE 754 sayısının bit bit açıklaması.</summary>
    public sealed class Ieee754Analysis
    {
        public string FormatName { get; init; } = string.Empty;
        public string SignBit { get; init; } = string.Empty;
        public string SignMeaning { get; init; } = string.Empty;
        public string ExponentBits { get; init; } = string.Empty;
        public int RawExponent { get; init; }
        public int Bias { get; init; }
        public int ActualExponent { get; init; }
        public string MantissaBits { get; init; } = string.Empty;
        public string MantissaFraction { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Formula { get; init; } = string.Empty;
        public string FullBinary { get; init; } = string.Empty;
        public string Hex { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
    }
}
