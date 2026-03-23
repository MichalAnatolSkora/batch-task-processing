using System.Data;

namespace BatchProcessing.ImportHandlers;

public interface IImportHandler
{
    string ImportTypeName { get; }
    Task HandleAsync(Upload upload, IDbConnection db, CancellationToken ct);
}
