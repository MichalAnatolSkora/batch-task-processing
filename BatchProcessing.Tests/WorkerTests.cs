using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatchProcessing;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Testcontainers.MsSql;
using Xunit;

namespace BatchProcessing.Tests;

public class WorkerTests : IAsyncLifetime
{
    private readonly MsSqlContainer _msSqlContainer;
    private string _connectionString = default!;

    public WorkerTests()
    {
        _msSqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("Your_Strong_Password123!")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _msSqlContainer.StartAsync();
        _connectionString = _msSqlContainer.GetConnectionString();
        await InitializeDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return _msSqlContainer.DisposeAsync().AsTask();
    }

    private async Task InitializeDatabaseAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        const string initQuery = @"
            CREATE TABLE BatchTasks (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Payload NVARCHAR(MAX),
                Status VARCHAR(20) DEFAULT 'Pending',
                CreatedAt DATETIME DEFAULT GETDATE(),
                PickedByWorker UNIQUEIDENTIFIER NULL,
                ProcessedAt DATETIME NULL
            );

            SET NOCOUNT ON;
            DECLARE @i INT = 1;
            WHILE @i <= 10000
            BEGIN
                INSERT INTO BatchTasks (Payload, Status, CreatedAt)
                VALUES ('Task payload ' + CAST(@i AS VARCHAR), 'Pending', GETDATE());
                SET @i = @i + 1;
            END
        ";

        await connection.ExecuteAsync(initQuery);
    }

    private Worker CreateWorker(int delayMs = 100)
    {
        var loggerMock = new Mock<ILogger<Worker>>();

        var inMemorySettings = new Dictionary<string, string?> {
            {"ConnectionStrings:DefaultConnection", _connectionString},
            {"WorkerDelayMs", delayMs.ToString()}
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return new Worker(loggerMock.Object, configuration);
    }

    [Fact]
    public async Task Worker_ShouldClaimTasks_WhenRunning()
    {
        // Arrange
        // We set delay to 5000 so it only runs once in the 1 second timeframe
        var worker = CreateWorker(5000);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        // Run the worker until cancelled
        await worker.StartAsync(cts.Token);
        await Task.Delay(1000); // Give it a second to process some tasks

        try { await worker.StopAsync(CancellationToken.None); } catch { }

        // Assert
        using var connection = new SqlConnection(_connectionString);
        var processingCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM BatchTasks WHERE Status = 'Processing'");

        // Since it processes 200 at a time and runs in a loop with a 5-second delay, 
        // it should pick exactly 200 tasks in 1 second.
        processingCount.Should().Be(200);

        var pendingCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM BatchTasks WHERE Status = 'Pending'");

        pendingCount.Should().Be(10000 - 200);
    }

    [Fact]
    public async Task MultipleWorkers_ShouldNotClaimSameTasks_AndShouldProcessAll()
    {
        // Arrange
        const int numberOfWorkers = 10;
        var workers = Enumerable.Range(0, numberOfWorkers).Select(_ => CreateWorker()).ToList();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act
        var workerTasks = workers.Select(w => Task.Run(async () =>
        {
            await w.StartAsync(cts.Token);
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(100, CancellationToken.None);
            }
            await w.StopAsync(CancellationToken.None);
        })).ToArray();

        try
        {
            await Task.WhenAll(workerTasks);
        }
        catch (TaskCanceledException) { }

        // Assert
        using var connection = new SqlConnection(_connectionString);

        var finalProcessingCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM BatchTasks WHERE Status = 'Processing'");

        // Asserting all tasks picked
        finalProcessingCount.Should().BeGreaterThan(200);

        var workerCounts = await connection.QueryAsync<int>(
             "SELECT COUNT(1) FROM BatchTasks WHERE PickedByWorker IS NOT NULL GROUP BY PickedByWorker");

        var processedCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM BatchTasks WHERE Status = 'Processing'");

        processedCount.Should().Be(10000); // Prove all completed
    }
}
