namespace Modbus.App.Services
{
    /// <summary>
    /// PLC (mantıksal, 4xxxx) ile PDU (0-tabanlı tel adresi) arasındaki dönüşümü
    /// TEK bir yerde yönetir. ViewModel'e dağılmış "if address > 40000" mantığı yok.
    ///
    /// Aktif profil tabanı kadar çıkarma yapar (LiBat: 40000, yani 40111 → 111).
    /// Kullanıcı küçük bir sayı (örn. 111) yazarsa doğrudan PDU olarak kabul eder.
    /// </summary>
    public sealed class AddressTranslationService
    {
        /// <summary>Adresleme tabanı (aktif profile göre ayarlanır).</summary>
        public int Base { get; set; } = 40000;

        /// <summary>Girilen adres mantıksal (4xxxx) mı görünüyor?</summary>
        public bool LooksLogical(int address) => address >= Base;

        /// <summary>Girilen adresi PDU'ya çevirir. 40111 → 111; 111 → 111.</summary>
        public int ToPdu(int address) => address >= Base ? address - Base : address;

        /// <summary>PDU adresini mantıksal adrese çevirir. 111 → 40111.</summary>
        public int ToLogical(int pduAddress) => Base + pduAddress;
    }
}
