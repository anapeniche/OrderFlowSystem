namespace Stock.API.Domain;

public class ProcessedMessage
{
    public int Id { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}
