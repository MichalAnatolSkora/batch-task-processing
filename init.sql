CREATE DATABASE BatchDb;
GO

USE BatchDb;
GO

CREATE TABLE BatchTasks (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Payload NVARCHAR(MAX),
    Status VARCHAR(20) DEFAULT 'Pending',
    CreatedAt DATETIME DEFAULT GETDATE(),
    PickedByWorker UNIQUEIDENTIFIER NULL,
    ProcessedAt DATETIME NULL
);
GO

-- Seed 10,000 tasks
SET NOCOUNT ON;
DECLARE @i INT = 1;
WHILE @i <= 10000
BEGIN
    INSERT INTO BatchTasks (Payload, Status, CreatedAt)
    VALUES ('Task payload ' + CAST(@i AS VARCHAR), 'Pending', GETDATE());
    SET @i = @i + 1;
END
GO
