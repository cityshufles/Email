IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TourGuideAssignments]') AND type in (N'U'))
BEGIN
	CREATE TABLE [dbo].[TourGuideAssignments](
		[Id] [int] IDENTITY(1,1) NOT NULL,
		[TourDate] [date] NOT NULL,
		[TourName] [nvarchar](255) NOT NULL,
		[TourTime] [nvarchar](50) NULL,
		[GuideId] [int] NOT NULL,
    [MeetingPlace] [nvarchar](255) NULL,
    [MeetingTime] [nvarchar](50) NULL,
    [MeetingInstructions] [nvarchar](max) NULL,
		[CreatedAt] [datetime] NOT NULL DEFAULT (getutcdate()),
		[UpdatedAt] [datetime] NOT NULL DEFAULT (getutcdate()),
	 CONSTRAINT [PK_TourGuideAssignments] PRIMARY KEY CLUSTERED 
	(
		[Id] ASC
	)
	) ON [PRIMARY]
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[TourGuideAssignments]') AND name = N'IX_TourGuideAssignments_Lookups')
BEGIN
	CREATE NONCLUSTERED INDEX [IX_TourGuideAssignments_Lookups] ON [dbo].[TourGuideAssignments]
	(
		[TourDate] ASC,
		[TourName] ASC
	)
END
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_TourGuideAssignments_Guides]') AND parent_object_id = OBJECT_ID(N'[dbo].[TourGuideAssignments]'))
BEGIN
	ALTER TABLE [dbo].[TourGuideAssignments]  WITH CHECK ADD  CONSTRAINT [FK_TourGuideAssignments_Guides] FOREIGN KEY([GuideId])
	REFERENCES [dbo].[Guides] ([Id])
	
	ALTER TABLE [dbo].[TourGuideAssignments] CHECK CONSTRAINT [FK_TourGuideAssignments_Guides]
END
GO

-- Safe Column Additions (Idempotent)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[TourGuideAssignments]') AND name = 'MeetingPlace')
BEGIN
    ALTER TABLE [dbo].[TourGuideAssignments] ADD [MeetingPlace] [nvarchar](255) NULL
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[TourGuideAssignments]') AND name = 'MeetingTime')
BEGIN
    ALTER TABLE [dbo].[TourGuideAssignments] ADD [MeetingTime] [nvarchar](50) NULL
END
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[TourGuideAssignments]') AND name = 'MeetingInstructions')
BEGIN
    ALTER TABLE [dbo].[TourGuideAssignments] ADD [MeetingInstructions] [nvarchar](max) NULL
END
GO
