using System;

namespace Modbus.Protocol.Packets
{
    /// <summary>Çözümlenmiş bir Modbus paketinin temel alanları.</summary>
    public class ModbusPacket
    {
        public ushort TransactionId { get; set; }
        public ushort ProtocolId { get; set; }
        public ushort Length { get; set; }
        public byte SlaveId { get; set; }
        public byte FunctionCode { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public byte[] RawData { get; set; } = Array.Empty<byte>();
    }
}