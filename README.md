# Batch Tasks Processing - Competing Consumers Pattern

This repository demonstrates the **Competing Consumers** pattern using a .NET Worker Service and a Microsoft SQL Server database. The solution runs multiple worker instances in parallel, processing background data imports (Uploads) from a shared database without conflict or duplication.

## What is the Competing Consumers Pattern?

The **Competing Consumers** pattern is a messaging design pattern that enables multiple concurrent consumers (e.g., our .NET Worker instances) to pull and process messages or tasks from a single, shared channel.

Instead of a single worker processing everything sequentially, the workload is distributed across a pool of "competing" workers.

### Key Benefits:
- **Scalability**: You can dynamically scale out by simply spinning up more worker instances when the workload spikes, and scale in when idle.
- **Resiliency & High Availability**: If one consumer fails or crashes, other active consumers in the pool simply continue processing the remaining tasks.
- **Load Leveling**: The system naturally handles bursts of tasks, balancing the processing load evenly across all available workers.

While this pattern is traditionally implemented using dedicated message brokers like RabbitMQ, Apache Kafka, or Azure Service Bus, this repository demonstrates how to achieve the same robust concurrent behavior relying entirely on a standard relational database architecture.

## The Problem: Concurrency & Locking

When designing a background processing system where multiple worker instances pull tasks from a central database table, you typically face two major concurrency challenges:

1. **Duplicate Processing (Race Condition)**: Two workers query the database at the exact same millisecond: `SELECT TOP(1) * FROM Uploads WHERE Status = 'Pending'`. They both receive the same upload and process it, resulting in duplicated work.
2. **Deadlocks**: To prevent duplicate processing, you might wrap your `SELECT` and subsequent `UPDATE` in a heavy transaction. This causes the workers to block one another. As workers scale up, database contention skyrockets, queries time out, and the database becomes the bottleneck.

## The Solution: `UPDLOCK, READPAST`

We solve this using SQL Server's native table hints, combined with an atomic `UPDATE ... OUTPUT` statement. This allows us to fetch and claim the pending uploads in a single, atomic operation that skips already locked rows.

### How it works

The core logic is located in `Worker.cs`:

```sql
WITH CTE AS (
    SELECT TOP (1) *
    FROM Uploads WITH (UPDLOCK, READPAST)
    WHERE Status = 'Pending'
    ORDER BY DateOfCreation ASC
)
UPDATE CTE
SET Status = 'Processing', PickedByWorker = @WorkerId, ProcessedAt = GETDATE()
OUTPUT INSERTED.Id, INSERTED.ImportTypeName, INSERTED.DateOfCreation, INSERTED.Metadata, INSERTED.Status, INSERTED.PickedByWorker, INSERTED.ProcessedAt;
```

*   `UPDLOCK`: When a worker selects a row, it immediately places an Update Lock on it. No other worker can place an `UPDLOCK` or `X` (exclusive) lock on that same row.
*   `READPAST`: **This is the magic.** If Worker B tries to read a row that is currently locked by Worker A, Worker B will *not* wait. Instead, SQL Server instructs Worker B to simply skip over the locked row and find the next available unlocked row.
*   `OUTPUT INSERTED.*`: We immediately change the status to `Processing` (committing the claim) and return the claimed rows back to the C# application in one round-trip.

## Data Model

Each upload contains hierarchical CSV data stored in a flat `RowValues` table:

- **Uploads** — represents a single import job with an `ImportTypeName`, `Metadata` (JSON), and processing status.
- **RowValues** — stores individual CSV rows. Rows are organized into collections (`CollectionName`), linked hierarchically via `ParentId`, and grouped by `GroupKey`.

```
Upload (Orders)
  └── RowValues
        ├── GroupKey: "group-1"
        │     ├── orders:      "ORD-001,John,150.00"        (root)
        │     └── order-lines: "Widget,2,25.00"             (child)
        │     └── order-lines: "Gadget,1,100.00"            (child)
        └── GroupKey: "group-2"
              ├── orders:      "ORD-002,Jane,50.00"          (root)
              └── order-lines: "Thing,5,10.00"               (child)
```

### Batched Processing with GroupKey

The Worker fetches `RowValues` in configurable batches (keyset pagination via `Id > @LastId`), groups them by `GroupKey`, and dispatches each `RowGroup` to the appropriate handler. This keeps memory usage bounded for large uploads.

## Import Handlers (Strategy Pattern)

Import processing is pluggable via the `IImportHandler` interface and .NET 8+ keyed services. Each handler receives a `RowGroup` containing all rows for a given `GroupKey` and processes them in-memory.

```csharp
public interface IImportHandler
{
    string ImportTypeName { get; }
    Task HandleAsync(Upload upload, RowGroup group, CancellationToken ct);
}
```

Built-in sample handlers:
- **OrdersImportHandler** (`"Orders"`) — parses order headers + order lines, submits to an external API via `IOrdersApiClient`.
- **ProductsImportHandler** (`"Products"`) — parses products + product variants.

Adding a new import type: implement `IImportHandler`, register with `AddKeyedScoped<IImportHandler, YourHandler>("YourTypeName")` in `Program.cs`.

## Project Structure

```
src/
  BatchProcessing/                  — Worker, domain models, import handlers
  BatchProcessing.OrdersApi/        — External data source for Orders (IOrdersApiClient + mock)
tests/
  BatchProcessing.Tests/            — Integration tests with Testcontainers
```

External data sources are separated into dedicated projects per source. This allows clean decommissioning — remove the project, its reference, and the DI registration.

### BatchProcessing.OrdersApi

Demonstrates the per-data-source project pattern:
- `IOrdersApiClient` — interface + DTOs for the external Orders API
- `MockOrdersApiClient` — mock implementation simulating API calls
- `ServiceCollectionExtensions.AddOrdersApi()` — DI registration

## Running the Verification Demo

This project includes a `docker-compose.yml` file designed to run the environment.

It spins up:
1.  **SQL Server 2022** instance.
2.  **Database Initializer** that automatically creates the `Uploads` and `RowValues` schema.
3.  **10 Replicas** of the .NET Worker Service running simultaneously.

### Requirements
*   Docker Desktop installed and running.

### Steps to Run

1. Open your terminal in the root of the project.
2. Run the compose file:
   ```bash
   docker-compose up --build
   ```
3. The database schema will be prepared. If you insert records into the `Uploads` and `RowValues` tables, the workers will competitively claim and process the uploads, routing them to the correct `IImportHandler`.

## Integration Tests with Testcontainers

This project includes a robust suite of integration tests in the `BatchProcessing.Tests` project to prove that the concurrency control works flawlessly.

We use [Testcontainers for .NET](https://dotnet.testcontainers.org/) to automatically spin up a real Microsoft SQL Server 2022 instance inside a Docker container during test execution.

The tests demonstrate:
- A single worker can successfully claim and process an upload utilizing the correct keyed service handler.
- **Multiple (5) concurrent workers** running simultaneously against the exact same table process 10 pending uploads without a single duplication, collision, or deadlock.
- Unknown import types correctly fail the upload status.

To run the integration tests yourself:
1. Ensure Docker Desktop is running.
2. Execute `dotnet test` from the root directory.

## Recommended Reading & Resources

### Competing Consumers Pattern
*   [Competing Consumers Pattern - Microsoft Cloud Design Patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/competing-consumers)
*   [Enterprise Integration Patterns: Competing Consumers](https://www.enterpriseintegrationpatterns.com/patterns/messaging/CompetingConsumers.html)

### SQL Server as a Message Queue
*   [Using SQL Server as a Message Queue](https://rusanu.com/2010/03/26/using-tables-as-queues/)
*   [Table Hints (Transact-SQL)](https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table)

### .NET Worker Services
*   [Background tasks with hosted services in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
