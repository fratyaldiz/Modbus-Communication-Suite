using System;

namespace Modbus.Communication.RTU
{
    /// <summary>
    /// Seri bağlantıda kullanılan parity seçenekleri.
    /// Kartın kullanım kılavuzundaki ayarla aynı olmalıdır.
    /// </summary>
    public enum RtuParity
    {
        None,
        Odd,
        Even,
        Mark,
        Space
    }

    /// <summary>
    /// Seri bağlantıda kullanılan stop bit seçenekleri.
    /// </summary>
    public enum RtuStopBits
    {
        One,
        OnePointFive,
        Two
    }

    /// <summary>
    /// Modbus RTU (COM / RS485 / seri port) bağlantı ayarları.
    /// Bu değerlerin tamamı kartın ayarlarıyla aynı olmalıdır.
    /// </summary>
    public sealed class RtuConnectionSettings
    {
        /// <summary>Seri port adı. Örnek: COM3.</summary>
        public string PortName { get; set; } = "COM3";

        /// <summary>İletişim hızı. Örnek: 9600 veya 115200.</summary>
        public int BaudRate { get; set; } = 9600;

        /// <summary>Bir karakterdeki veri biti sayısı. Modbus RTU'da genellikle 8.</summary>
        public int DataBits { get; set; } = 8;

        /// <summary>Parity ayarı. Örnek: None (8N1) veya Even (8E1).</summary>
        public RtuParity Parity { get; set; } = RtuParity.None;

        /// <summary>Stop bit ayarı. Genellikle One.</summary>
        public RtuStopBits StopBits { get; set; } = RtuStopBits.One;

        /// <summary>Okuma ve yazma zaman aşımı, milisaniye.</summary>
        public int Timeout { get; set; } = 3000;

        /// <summary>
        /// Ayarları port açılmadan önce kontrol eder ve anlaşılır hata üretir.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(PortName))
                throw new ArgumentException("COM port seçilmelidir.", nameof(PortName));

            if (BaudRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(BaudRate), "Baud rate sıfırdan büyük olmalıdır.");

            if (DataBits < 5 || DataBits > 8)
                throw new ArgumentOutOfRangeException(nameof(DataBits), "Data bits 5 ile 8 arasında olmalıdır.");

            if (Timeout < 100 || Timeout > 120_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Timeout),
                    "Timeout 100 ile 120000 ms arasında olmalıdır.");
            }
        }

        /// <summary>Örnek gösterim: COM5, 9600, 8N1, 3000 ms.</summary>
        public string ToShortText()
        {
            string parityLetter = Parity switch
            {
                RtuParity.None => "N",
                RtuParity.Odd => "O",
                RtuParity.Even => "E",
                RtuParity.Mark => "M",
                RtuParity.Space => "S",
                _ => "?"
            };

            string stopText = StopBits switch
            {
                RtuStopBits.One => "1",
                RtuStopBits.OnePointFive => "1.5",
                RtuStopBits.Two => "2",
                _ => "?"
            };

            return $"{PortName}, {BaudRate}, {DataBits}{parityLetter}{stopText}, {Timeout} ms";
        }
    }
}
