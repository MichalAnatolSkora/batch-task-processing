namespace BatchProcessing;

public class Upload
{
    public int Id { get; set; }
    public string ImportTypeName { get; set; } = string.Empty;
    public DateTime DateOfCreation { get; set; }
    public string Metadata { get; set; } = "{}";
    public string Status { get; set; } = "Pending";
    public Guid? PickedByWorker { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
