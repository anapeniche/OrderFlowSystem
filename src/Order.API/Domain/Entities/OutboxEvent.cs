using System;

namespace Order.API.Domain.Entities
{
    public class OutboxEvent
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool Published { get; set; } = false;
    }
}