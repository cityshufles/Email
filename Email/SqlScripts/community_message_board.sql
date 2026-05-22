-- Community Message Board table
-- Run once against the production database

CREATE TABLE [dbo].[CommunityPosts] (
    [Id]              INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [AuthorUserId]    INT NOT NULL,
    [AuthorName]      NVARCHAR(200) NOT NULL,
    [Content]         NVARCHAR(MAX) NOT NULL,
    [IsPinned]        BIT NOT NULL DEFAULT 0,
    [IsHighlighted]   BIT NOT NULL DEFAULT 0,
    [IsAnnouncement]  BIT NOT NULL DEFAULT 0,
    [AnnouncementEmailSentAt] DATETIME2(7) NULL,
    [ParentPostId]    INT NULL,
    [Depth]           INT NOT NULL DEFAULT 0,
    [IsActive]        BIT NOT NULL DEFAULT 1,
    [CreatedAtUtc]    DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAtUtc]    DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME()
);

-- ParentPostId = NULL means top-level post
-- ParentPostId = another post Id means nested reply
-- Depth tracks nesting level (0 = top, 1 = reply, 2 = reply-to-reply, etc.)
