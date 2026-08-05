using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Modbus.App.Models
{
    /// <summary>Paket içindeki bir alanın ekranda anlaşılır biçimde gösterimi.</summary>
    public sealed class PacketFieldItem
    {
        public string Field { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}