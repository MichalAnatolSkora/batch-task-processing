namespace BatchProcessing;

public class RowGroup
{
    public string GroupKey { get; set; } = string.Empty;
    public List<RowValue> Rows { get; set; } = [];
}
