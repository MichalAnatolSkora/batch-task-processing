# Batch Tasks Processing - Competing Consumers Pattern

This repository demonstrates the **Competing Consumers** pattern using a .NET Worker Service and a Microsoft SQL Server database. The solution runs multiple worker instances in parallel, processing background tasks from a shared database table without conflict or duplication.

## 🤝 What is the Competing Consumers Pattern?

The **Competing Consumers** pattern is a messaging design pattern that enables multiple concurrent consumers (e.g., our .NET Worker instances) to pull and process messages or tasks from a single, shared channel (in this case, the `BatchTasks` database table). 

Instead of a single worker processing everything sequentially, the workload is distributed across a pool of "competing" workers.

### Key Benefits:
- **Scalability**: You can dynamically scale out by simply spinning up more worker instances when the workload spikes, and scale in when idle.
- **Resiliency & High Availability**: If one consumer fails or crashes, other active consumers in the pool simply continue processing the remaining tasks.
- **Load Leveling**: The system naturally handles bursts of tasks, balancing the processing load evenly across all available workers.

While this pattern is traditionally implemented using dedicated message brokers like RabbitMQ, Apache Kafka, or Azure Service Bus, this repository demonstrates how to achieve the same robust concurrent behavior relying entirely on a standard relational database architecture.

## ⚠️ The Problem: Concurrency & Locking

When designing a background processing system where multiple worker instances pull tasks from a central database table, you typically face two major concurrency challenges:

1. **Duplicate Processing (Race Condition)**: Two workers query the database at the exact same millisecond: `SELECT TOP(200) * FROM Tasks WHERE Status = 'Pending'`. They both receive the same 200 tasks and process them, resulting in duplicated work (e.g., sending the same email twice, double-charging a customer).
2. **Deadlocks**: To prevent duplicate processing, you might wrap your `SELECT` and subsequent `UPDATE` in a heavy transaction. This causes the workers to block one another. As workers scale up, database contention skyrockets, queries time out, and the database becomes the bottleneck.

## 🛠️ The Solution: `UPDLOCK, READPAST`

We solve this using SQL Server's native table hints, combined with an atomic `UPDATE ... OUTPUT` statement. This allows us to fetch and claim the tasks in a single, atomic operation that skips already locked rows.

### How it works

The core logic is located in `Worker.cs`:

```sql
WITH CTE AS (
    SELECT TOP (200) *
    FROM BatchTasks WITH (UPDLOCK, READPAST)
    WHERE Status = 'Pending'
    ORDER BY CreatedAt ASC
)
UPDATE CTE
SET Status = 'Processing', PickedByWorker = @WorkerId, ProcessedAt = GETDATE()
OUTPUT INSERTED.Id, INSERTED.Payload, INSERTED.Status, INSERTED.CreatedAt, INSERTED.PickedByWorker, INSERTED.ProcessedAt;
```

*   `UPDLOCK`: When a worker selects a row, it immediately places an Update Lock on it. No other worker can place an `UPDLOCK` or `X` (exclusive) lock on that same row.
*   `READPAST`: **This is the magic.** If Worker B tries to read a row that is currently locked by Worker A, Worker B will *not* wait. Instead, SQL Server instructs Worker B to simply skip over the locked row and find the next available unlocked row.
*   `OUTPUT INSERTED.*`: We immediately change the status to `Processing` (committing the claim) and return the claimed rows back to the C# application in one round-trip.

### Why this is powerful

*   **No Blocking**: 10, 20, or 50 workers can query the exact same table simultaneously. Thanks to `READPAST`, they seamlessly slide past each other's locks, grabbing completely distinct chunks of tasks.
*   **Zero Duplicates**: The atomic `UPDATE` guarantees that a task is claimed exclusively by one, and only one, worker.
*   **High Throughput**: We bypass the need for an external queue like RabbitMQ or Redis, leveraging the existing relational database to achieve high-performance message queuing.

## 🚀 Running the Verification Demo

This project includes a `docker-compose.yml` file designed to prove the pattern works.

It spins up:
1.  **SQL Server 2022** instance.
2.  **Database Initializer** that automatically creates the schema and seeds exactly **10,000 pending tasks**.
3.  **10 Replicas** of the .NET Worker Service running simultaneously.

### Requirements
*   Docker Desktop installed and running.

### Steps to Run

1. Open your terminal in the root of the project.
2. Run the compose file:
   ```bash
   docker-compose up --build
   ```
3. Watch the logs. You will see 10 distinct workers (identified by their unique GUIDs) starting up, securely claiming chunks of 200 tasks at a time, and logging their progress.
4. The background tasks will be depleted in a matter of seconds.

### Verifying the Result

Connecting to the database (`localhost,1433`, User: `sa`, Password: `Your_Strong_Password123!`) after the run allows you to verify the distribution of work among the 10 instances:

```sql
SELECT 
    PickedByWorker, 
    COUNT(Id) AS NumberOfTasksProcessed 
FROM BatchDb.dbo.BatchTasks 
GROUP BY PickedByWorker;
```
*You should see 10 distinct worker GUIDs, each having processed a subset of tasks, with the sum adding up precisely to 10,000.*
