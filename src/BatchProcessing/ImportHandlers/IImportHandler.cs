namespace BatchProcessing.ImportHandlers;

public interface IImportHandler
{
    string ImportTypeName { get; }
    Task HandleAsync(Upload upload, RowGroup group, CancellationToken ct);
}
