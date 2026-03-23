CREATE DATABASE BatchDb;
GO

USE BatchDb;
GO

CREATE TABLE Uploads (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ImportTypeName NVARCHAR(255) NOT NULL,
    DateOfCreation DATETIME NOT NULL DEFAULT GETDATE(),
    Metadata NVARCHAR(MAX) NOT NULL DEFAULT '{}',
    Status VARCHAR(20) NOT NULL DEFAULT 'Pending',
    PickedByWorker UNIQUEIDENTIFIER NULL,
    ProcessedAt DATETIME NULL
);
GO

CREATE TABLE RowValues (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,
    UploadId INT NOT NULL,
    ParentId BIGINT NULL,
    CollectionName NVARCHAR(255) NOT NULL,
    GroupKey NVARCHAR(255) NOT NULL,
    Value NVARCHAR(MAX) NOT NULL,
    CONSTRAINT FK_RowValues_Upload FOREIGN KEY (UploadId) REFERENCES Uploads(Id),
    CONSTRAINT FK_RowValues_Parent FOREIGN KEY (ParentId) REFERENCES RowValues(Id)
);
GO

CREATE INDEX IX_RowValues_UploadId ON RowValues(UploadId);
CREATE INDEX IX_RowValues_ParentId ON RowValues(ParentId);
CREATE INDEX IX_RowValues_GroupKey ON RowValues(GroupKey);
GO
