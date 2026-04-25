IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[TourSchedules]') AND type in (N'U'))
BEGIN
	CREATE TABLE [dbo].[TourSchedules](
		[Id] [int] IDENTITY(1,1) NOT NULL,
		[TourId] [int] NOT NULL,
		[ScheduleName] [nvarchar](255) NOT NULL,
		[StartDate] [date] NOT NULL,
		[EndDate] [date] NOT NULL,
		[MeetingPlace] [nvarchar](500) NULL,
		[MeetingTime] [nvarchar](50) NULL,
		[MeetingInstructions] [nvarchar](max) NULL,
		[VendorLink] [nvarchar](500) NULL,
		[DayAssignmentsJson] [nvarchar](max) NULL,
		[TimeSlotsJson] [nvarchar](max) NULL,
		[IsActive] [bit] NOT NULL DEFAULT ((1)),
		[CreatedAt] [datetime] NOT NULL DEFAULT (getutcdate()),
		[UpdatedAt] [datetime] NOT NULL DEFAULT (getutcdate()),
	 CONSTRAINT [PK_TourSchedules] PRIMARY KEY CLUSTERED 
	(
		[Id] ASC
	)
	) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO
	
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_TourSchedules_Tours]') AND parent_object_id = OBJECT_ID(N'[dbo].[TourSchedules]'))
BEGIN
	ALTER TABLE [dbo].[TourSchedules]  WITH CHECK ADD  CONSTRAINT [FK_TourSchedules_Tours] FOREIGN KEY([TourId])
	REFERENCES [dbo].[Tours] ([Id])
	ON DELETE CASCADE
END
GO

ALTER TABLE [dbo].[TourSchedules] CHECK CONSTRAINT [FK_TourSchedules_Tours]
GO
