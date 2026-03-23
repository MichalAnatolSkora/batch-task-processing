using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatchProcessing;

public class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
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
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker {WorkerId} error processing batch.", _workerId);
            }

            // Wait before checking for more tasks
            var delayMs = configuration.GetValue<int>("WorkerDelayMs", 5000);
            await Task.Delay(delayMs, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            logger.LogWarning("Worker {WorkerId}: Connection string is not configured.", _workerId);
            return;
        }

        using IDbConnection dbConnection = new SqlConnection(_connectionString);
        // Use UPDLOCK and READPAST to allow multiple workers to run concurrently without deadlocks
        // and atomically claim up to 200 tasks by changing their status to 'Processing'
        const string query = @"
            WITH CTE AS (
                SELECT TOP (200) *
                FROM BatchTasks WITH (UPDLOCK, READPAST)
                WHERE Status = 'Pending'
                ORDER BY CreatedAt ASC
            )
            UPDATE CTE
            SET Status = 'Processing', PickedByWorker = @WorkerId, ProcessedAt = GETDATE()
            OUTPUT INSERTED.Id, INSERTED.Payload, INSERTED.Status, INSERTED.CreatedAt, INSERTED.PickedByWorker, INSERTED.ProcessedAt;";

        var tasks = await dbConnection.QueryAsync<BatchTask>(new CommandDefinition(query, new { WorkerId = _workerId }, cancellationToken: stoppingToken));

        var batchTasks = tasks.ToList();

        if (batchTasks.Count == 0)
        {
            logger.LogInformation("No pending tasks found at {time}", DateTimeOffset.Now);
            return;
        }

        logger.LogInformation("Fetched {Count} tasks for processing.", batchTasks.Count);

        foreach (var task in batchTasks)
        {
            // Simulate processing
            logger.LogInformation("Processing task {TaskId}...", task.Id);

            // In real scenario, execute update after processing:
            // await dbConnection.ExecuteAsync("UPDATE BatchTasks SET Status = 'Completed' WHERE Id = @Id", new { task.Id });
        }
    }
}
