-- FAQ tables
-- Run once against the production database

-- FaqCategories (folders)
CREATE TABLE [dbo].[FaqCategories] (
    [Id]           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [Name]         NVARCHAR(200) NOT NULL,
    [SortOrder]    INT NOT NULL DEFAULT 0,
    [IsActive]     BIT NOT NULL DEFAULT 1,
    [CreatedAtUtc] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAtUtc] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- FaqItems (questions)
CREATE TABLE [dbo].[FaqItems] (
    [Id]              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [CategoryId]      INT NOT NULL,
    [Question]        NVARCHAR(500) NOT NULL,
    [SortOrder]       INT NOT NULL DEFAULT 0,
    [IsActive]        BIT NOT NULL DEFAULT 1,
    [CreatedByUserId] INT NULL,
    [CreatedAtUtc]    DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAtUtc]    DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- FaqAnswers (answers by guides)
CREATE TABLE [dbo].[FaqAnswers] (
    [Id]           INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [FaqItemId]    INT NOT NULL,
    [AuthorUserId] INT NOT NULL,
    [AuthorName]   NVARCHAR(200) NOT NULL,
    [Content]      NVARCHAR(MAX) NOT NULL,
    [IsAccepted]   BIT NOT NULL DEFAULT 0,
    [IsActive]     BIT NOT NULL DEFAULT 1,
    [CreatedAtUtc] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAtUtc] DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME()
);
