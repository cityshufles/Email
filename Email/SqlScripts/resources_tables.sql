-- Resources tables for guide-uploaded photos, videos, PDFs
-- Run once against the production database

CREATE TABLE [dbo].[Resources] (
    [Id]                INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FileName]          NVARCHAR(500) NOT NULL,
    [OriginalFileName]  NVARCHAR(500) NOT NULL,
    [FileType]          NVARCHAR(20) NOT NULL,  -- 'photo','video','pdf','other'
    [FileSizeBytes]     BIGINT NOT NULL DEFAULT 0,
    [RelativePath]      NVARCHAR(1000) NOT NULL,
    [Description]       NVARCHAR(500) NULL,
    [UploadedByUserId]  INT NOT NULL,
    [UploadedByName]    NVARCHAR(200) NOT NULL,
    [IsActive]          BIT NOT NULL DEFAULT 1,
    [CreatedAtUtc]      DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAtUtc]      DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE [dbo].[ResourceTours] (
    [Id]           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [ResourceId]   INT NOT NULL,
    [TourId]       INT NOT NULL,
    [CreatedAtUtc] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE UNIQUE INDEX IX_ResourceTours_Unique ON dbo.ResourceTours(ResourceId, TourId);
