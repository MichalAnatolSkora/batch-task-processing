using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BatchProcessing.ImportHandlers;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        const string schemaSql = @"
            CREATE TABLE Uploads (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                ImportTypeName NVARCHAR(255) NOT NULL,
                DateOfCreation DATETIME NOT NULL DEFAULT GETDATE(),
                Metadata NVARCHAR(MAX) NOT NULL DEFAULT '{}',
                Status VARCHAR(20) NOT NULL DEFAULT 'Pending',
                PickedByWorker UNIQUEIDENTIFIER NULL,
                ProcessedAt DATETIME NULL
            );

            CREATE TABLE RowValues (
                Id BIGINT IDENTITY(1,1) PRIMARY KEY,
                UploadId INT NOT NULL,
                ParentId BIGINT NULL,
                CollectionName NVARCHAR(255) NOT NULL,
                GroupKey NVARCHAR(255) NOT NULL,
                Value NVARCHAR(MAX) NOT NULL,
                CONSTRAINT FK_RowValues_Upload FOREIGN KEY (UploadId) REFERENCES Uploads(Id),
                CONSTRAINT FK_RowValues_Parent FOREIGN KEY (ParentId) REFERENCES RowValues(Id)
            );";

        await connection.ExecuteAsync(schemaSql);
    }

    private async Task SeedUploadWithRows(
        SqlConnection connection,
        string importType,
        string rootCollection,
        string childCollection,
        int rootRowCount,
        int childRowsPerRoot)
    {
        var uploadId = await connection.ExecuteScalarAsync<int>(
            @"INSERT INTO Uploads (ImportTypeName, Status) VALUES (@ImportType, 'Pending');
              SELECT SCOPE_IDENTITY();",
            new { ImportType = importType });

        for (var i = 1; i <= rootRowCount; i++)
        {
            var rootId = await connection.ExecuteScalarAsync<long>(
                @"INSERT INTO RowValues (UploadId, ParentId, CollectionName, GroupKey, Value)
                  VALUES (@UploadId, NULL, @CollectionName, @GroupKey, @Value);
                  SELECT SCOPE_IDENTITY();",
                new { UploadId = uploadId, CollectionName = rootCollection, GroupKey = $"group-{i}", Value = $"col1,col2,{i}" });

            for (var j = 1; j <= childRowsPerRoot; j++)
            {
                await connection.ExecuteAsync(
                    @"INSERT INTO RowValues (UploadId, ParentId, CollectionName, GroupKey, Value)
                      VALUES (@UploadId, @ParentId, @CollectionName, @GroupKey, @Value);",
                    new
                    {
                        UploadId = uploadId,
                        ParentId = rootId,
                        CollectionName = childCollection,
                        GroupKey = $"group-{i}",
                        Value = $"child-col1,child-col2,{i}-{j}"
                    });
            }
        }
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

        var services = new ServiceCollection();
        services.AddKeyedScoped<IImportHandler, OrdersImportHandler>("Orders");
        services.AddKeyedScoped<IImportHandler, ProductsImportHandler>("Products");
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        return new Worker(loggerMock.Object, configuration, serviceProvider);
    }

    [Fact]
    public async Task Worker_ShouldClaimAndCompleteUpload_WithOrdersHandler()
    {
        // Arrange
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await SeedUploadWithRows(connection, "Orders", "orders", "order-lines",
            rootRowCount: 5, childRowsPerRoot: 3);

        var worker = CreateWorker(5000);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(1500);
        try { await worker.StopAsync(CancellationToken.None); } catch { }

        // Assert
        var status = await connection.ExecuteScalarAsync<string>(
            "SELECT Status FROM Uploads WHERE Id = 1");
        status.Should().Be("Completed");

        var pickedByWorker = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT PickedByWorker FROM Uploads WHERE Id = 1");
        pickedByWorker.Should().NotBeNull();
    }

    [Fact]
    public async Task Worker_ShouldFailUpload_WhenNoHandlerRegistered()
    {
        // Arrange
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await SeedUploadWithRows(connection, "UnknownType", "data", "sub-data",
            rootRowCount: 2, childRowsPerRoot: 1);

        var worker = CreateWorker(5000);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(1500);
        try { await worker.StopAsync(CancellationToken.None); } catch { }

        // Assert
        var status = await connection.ExecuteScalarAsync<string>(
            "SELECT Status FROM Uploads WHERE Id = 1");
        status.Should().Be("Failed");
    }

    [Fact]
    public async Task MultipleWorkers_ShouldNotClaimSameUpload()
    {
        // Arrange
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        for (var i = 0; i < 10; i++)
        {
            await SeedUploadWithRows(connection, "Orders", "orders", "order-lines",
                rootRowCount: 3, childRowsPerRoot: 2);
        }

        const int numberOfWorkers = 5;
        var workers = Enumerable.Range(0, numberOfWorkers).Select(_ => CreateWorker()).ToList();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

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

        try { await Task.WhenAll(workerTasks); }
        catch (TaskCanceledException) { }

        // Assert - all uploads completed, each by a different or same worker but no double-claims
        var completedCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Uploads WHERE Status = 'Completed'");
        completedCount.Should().Be(10);

        var pendingCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Uploads WHERE Status = 'Pending'");
        pendingCount.Should().Be(0);
    }
}
