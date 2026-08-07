namespace Modbus.App.Models
{
    /// <summary>
    /// Bir status/olay bitinin tanımı. Profil bunları sağlar; MainViewModel'e
    /// hard-code EDİLMEZ. LiBat için 64-bit Battery Status'un her biti bir tanımdır.
    /// </summary>
    public sealed class StatusBitDefinition
    {
        public int Bit { get; }
        public string Description { get; }
        public EventSeverity Severity { get; }

        public StatusBitDefinition(int bit, string description, EventSeverity severity)
        {
            Bit = bit;
            Description = description;
            Severity = severity;
        }
    }
}
