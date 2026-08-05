namespace Modbus.App.Models
{
    /// <summary>
    /// Ham Modbus paketindeki TEK bir byte'ın ekranda gösterilen hali.
    ///
    /// "Arka planda ne oluyor?" sorusunun en somut cevabı budur: kablodan geçen
    /// her byte'ı sırasıyla, hem hex hem binary hem de anlamıyla birlikte gösterir.
    /// </summary>
    public sealed class FrameByteItem
    {
        /// <summary>Paketin kaçıncı byte'ı (0'dan başlar).</summary>
        public int Offset { get; init; }

        /// <summary>Byte'ın hex gösterimi. Örnek: 0x03</summary>
        public string Hex { get; init; } = string.Empty;

        /// <summary>Byte'ın onluk gösterimi. Örnek: 3</summary>
        public int Decimal { get; init; }

        /// <summary>Byte'ın 8 bitlik binary gösterimi. Örnek: 0000 0011</summary>
        public string Binary { get; init; } = string.Empty;

        /// <summary>Bu byte'ın Modbus protokolündeki görevi.</summary>
        public string Meaning { get; init; } = string.Empty;
    }
}
