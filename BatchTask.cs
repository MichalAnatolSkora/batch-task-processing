namespace BatchProcessing;

public class BatchTask
{
    public int Id { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public Guid? PickedByWorker { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
