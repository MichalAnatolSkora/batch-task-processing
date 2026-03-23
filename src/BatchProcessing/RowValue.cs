namespace BatchProcessing;

public class RowValue
{
    public long Id { get; set; }
    public int UploadId { get; set; }
    public long? ParentId { get; set; }
    public string CollectionName { get; set; } = string.Empty;
    public string GroupKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
