namespace Modbus.Protocol.Functions
{
    /// <summary>
    /// Modbus "Function Code" (Fonksiyon Kodu) listesi.
    ///
    /// Modbus'ta her istek bir fonksiyon kodu taşır. Bu kod, karşı tarafa
    /// "ne yapmak istiyorum?" der. Örneğin: register oku, coil yaz, vb.
    ///
    /// ": byte" demek: bu enum değerleri hafızada 1 byte tutar.
    /// Bu önemli, çünkü Modbus paketinde fonksiyon kodu tam olarak 1 byte'tır.
    ///
    /// Modbus terimleri:
    /// - Coil            : 1 bitlik OKU/YAZ değer (örn. röle aç/kapa). 0 veya 1.
    /// - Discrete Input  : 1 bitlik SADECE OKU değer (örn. buton durumu).
    /// - Holding Register: 16 bitlik OKU/YAZ değer (örn. set edilen sıcaklık).
    /// - Input Register  : 16 bitlik SADECE OKU değer (örn. ölçülen sıcaklık).
    /// </summary>
    public enum ModbusFunctionCode : byte
    {
        ReadCoils = 1,               // 0x01: Coil'leri oku (bit)
        ReadDiscreteInputs = 2,      // 0x02: Discrete Input oku (bit, salt okunur)
        ReadHoldingRegisters = 3,    // 0x03: Holding Register oku (16 bit)
        ReadInputRegisters = 4,      // 0x04: Input Register oku (16 bit, salt okunur)
        WriteSingleCoil = 5,         // 0x05: Tek bir coil yaz
        WriteSingleRegister = 6,     // 0x06: Tek bir register yaz (16 bit)
        WriteMultipleRegisters = 16  // 0x10: Birden fazla register yaz
    }
}
