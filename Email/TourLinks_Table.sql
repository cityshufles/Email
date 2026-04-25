
-- Created: 2026-02-10 14:48
-- Updated: 2026-02-10 15:12
-- Description: Creates the TourLinks table for storing vendor-specific tour links.

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TourLinks]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[TourLinks](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [Vendor] [nvarchar](100) NOT NULL,
        [TourName] [nvarchar](500) NOT NULL,
        [TourId] [int] NULL, -- Nullable FK to Tours table
        [DaysTimesAvailable] [nvarchar](1000) NULL,
        [TourDay] [int] NULL, -- 0=Sunday ... 6=Saturday
        [TourTime] [nvarchar](50) NULL,
        [ProductId] [nvarchar](100) NULL,
        [ReviewLink] [nvarchar](1000) NULL,
        [TourLink] [nvarchar](1000) NULL,
        [Notes] [nvarchar](max) NULL,
        [CreatedAt] [datetime2](7) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] [datetime2](7) NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_TourLinks] PRIMARY KEY CLUSTERED 
        (
            [Id] ASC
        ),
        CONSTRAINT [FK_TourLinks_Tours] FOREIGN KEY ([TourId]) REFERENCES [dbo].[Tours] ([Id])
    )
    PRINT 'Table TourLinks created successfully.'
END
ELSE
BEGIN
    -- Simple migration check (optional, for dev convenience)
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[TourLinks]') AND name = 'TourId')
    BEGIN
        ALTER TABLE [dbo].[TourLinks] ADD [TourId] [int] NULL;
        ALTER TABLE [dbo].[TourLinks] WITH CHECK ADD CONSTRAINT [FK_TourLinks_Tours] FOREIGN KEY([TourId]) REFERENCES [dbo].[Tours] ([Id]);
        PRINT 'Added TourId column and FK to TourLinks table.';
    END
    ELSE
    BEGIN
        PRINT 'Table TourLinks already exists and has TourId column.';
    END
    
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[TourLinks]') AND name = 'TourDay')
    BEGIN
        ALTER TABLE [dbo].[TourLinks] ADD [TourDay] [int] NULL;
        PRINT 'Added TourDay column to TourLinks table.';
    END
    
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[TourLinks]') AND name = 'TourTime')
    BEGIN
        ALTER TABLE [dbo].[TourLinks] ADD [TourTime] [nvarchar](50) NULL;
        PRINT 'Added TourTime column to TourLinks table.';
    END
END
GO
