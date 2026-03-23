using System.Data;
using BatchProcessing.ImportHandlers;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatchProcessing;

public class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IServiceProvider serviceProvider) : BackgroundService
{
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly Guid _workerId = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker {WorkerId} starting.", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextUploadAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker {WorkerId} error processing upload.", _workerId);
            }

            var delayMs = configuration.GetValue<int>("WorkerDelayMs", 5000);
            await Task.Delay(delayMs, stoppingToken);
        }
    }

    private async Task ProcessNextUploadAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            logger.LogWarning("Worker {WorkerId}: Connection string is not configured.", _workerId);
            return;
        }

        using IDbConnection db = new SqlConnection(_connectionString);

        // Atomically claim one pending upload
        const string claimSql = @"
            WITH CTE AS (
                SELECT TOP (1) *
                FROM Uploads WITH (UPDLOCK, READPAST)
                WHERE Status = 'Pending'
                ORDER BY DateOfCreation ASC
            )
            UPDATE CTE
            SET Status = 'Processing', PickedByWorker = @WorkerId, ProcessedAt = GETDATE()
            OUTPUT INSERTED.Id, INSERTED.ImportTypeName, INSERTED.DateOfCreation, INSERTED.Metadata, INSERTED.Status, INSERTED.PickedByWorker, INSERTED.ProcessedAt;";

        var upload = await db.QuerySingleOrDefaultAsync<Upload>(
            new CommandDefinition(claimSql, new { WorkerId = _workerId }, cancellationToken: stoppingToken));

        if (upload is null)
        {
            logger.LogInformation("No pending uploads found at {Time}.", DateTimeOffset.Now);
            return;
        }

        logger.LogInformation("Worker {WorkerId} claimed upload {UploadId} ({ImportType}).",
            _workerId, upload.Id, upload.ImportTypeName);

        try
        {
            var handler = serviceProvider.GetKeyedService<IImportHandler>(upload.ImportTypeName);

            if (handler is null)
            {
                logger.LogError("No handler registered for import type '{ImportType}'.", upload.ImportTypeName);
                await db.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE Uploads SET Status = 'Failed' WHERE Id = @Id",
                        new { upload.Id },
                        cancellationToken: stoppingToken));
                return;
            }

            await handler.HandleAsync(upload, db, stoppingToken);

            await db.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE Uploads SET Status = 'Completed' WHERE Id = @Id",
                    new { upload.Id },
                    cancellationToken: stoppingToken));

            logger.LogInformation("Upload {UploadId} completed.", upload.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed processing upload {UploadId}.", upload.Id);

            await db.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE Uploads SET Status = 'Failed' WHERE Id = @Id",
                    new { upload.Id },
                    cancellationToken: stoppingToken));
        }
    }
}
