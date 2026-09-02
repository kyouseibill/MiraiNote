-- WelcomeGreeting：建表 + 种子（原 40 + 新 40，SortOrder 1..80）
-- 幂等：表已存在则跳过 CREATE；Content 已存在则跳过 INSERT。
-- 执行账户需具备建表与写入权限（建议 MigrationConnection / db_owner）。

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.WelcomeGreeting', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WelcomeGreeting
    (
        Id         INT            NOT NULL IDENTITY(1, 1) CONSTRAINT PK_WelcomeGreeting PRIMARY KEY,
        Content    NVARCHAR(60)   NOT NULL,
        IsActive   BIT            NOT NULL CONSTRAINT DF_WelcomeGreeting_IsActive DEFAULT (1),
        SortOrder  INT            NOT NULL CONSTRAINT DF_WelcomeGreeting_SortOrder DEFAULT (0),
        IsDeleted  BIT            NOT NULL CONSTRAINT DF_WelcomeGreeting_IsDeleted DEFAULT (0),
        CreatedAt  DATETIME2      NOT NULL,
        CreatedBy  INT            NOT NULL CONSTRAINT DF_WelcomeGreeting_CreatedBy DEFAULT (1),
        UpdatedAt  DATETIME2      NOT NULL,
        UpdatedBy  INT            NOT NULL CONSTRAINT DF_WelcomeGreeting_UpdatedBy DEFAULT (1)
    );

    CREATE UNIQUE INDEX IX_WelcomeGreeting_Content
        ON dbo.WelcomeGreeting (Content)
        WHERE IsDeleted = 0;

    CREATE INDEX IX_WelcomeGreeting_IsActive_SortOrder
        ON dbo.WelcomeGreeting (IsActive, SortOrder);
END
GO

-- 种子：仅插入尚不存在的 Content（含已软删同文也会跳过，避免唯一冲突后再插）
DECLARE @now DATETIME2 = SYSUTCDATETIME();

;WITH Seed(Content, SortOrder) AS
(
    SELECT * FROM (VALUES
    (N'今天，安静地推进', 1),
    (N'今天，只把一件重要的事做好', 2),
    (N'慢慢来，但不要停', 3),
    (N'先写下一行，再谈后面的事', 4),
    (N'今天的节奏，由你自己定', 5),
    (N'把注意力收回到眼前这一步', 6),
    (N'不必赶完所有，完成最要紧的就好', 7),
    (N'深呼吸一次，然后开始', 8),
    (N'今天适合稳步向前', 9),
    (N'小事做好，也是前进', 10),
    (N'给专注留一段不被打断的时间', 11),
    (N'今天，少一点焦虑，多一点行动', 12),
    (N'先开始，完美稍后再说', 13),
    (N'把复杂的事拆小一点', 14),
    (N'今天也值得认真对待', 15),
    (N'安静工作，比匆忙更有效', 16),
    (N'记住你为什么开始', 17),
    (N'今天，把能量用在刀刃上', 18),
    (N'完成比完美更靠近目标', 19),
    (N'允许自己按自己的速度走', 20),
    (N'先清理桌面，再清理思绪', 21),
    (N'今天，留下一点可见的进展', 22),
    (N'不必一次走完，迈出下一步即可', 23),
    (N'把今天过成自己能复盘的一天', 24),
    (N'少开几个标签页，多做一件实事', 25),
    (N'今天适合把拖延换成开始', 26),
    (N'专注当下这一刻就够了', 27),
    (N'温柔对待自己，认真对待工作', 28),
    (N'今天，写清楚再动手', 29),
    (N'进展不一定很大，但要真实', 30),
    (N'把干扰先放到一边', 31),
    (N'今天，做能积累的事', 32),
    (N'慢一点，也要把路走对', 33),
    (N'先兑现对自己的一个小承诺', 34),
    (N'今天的你，比昨天多一点清晰', 35),
    (N'把待办收束到三件以内', 36),
    (N'安静里，更容易想明白', 37),
    (N'今天，给重要的事留出主场', 38),
    (N'做完一件，再打开下一件', 39),
    (N'今天，也请好好照顾自己的节奏', 40),
    (N'今天，把心静下来再动手', 41),
    (N'一步够实，就胜过十步慌张', 42),
    (N'先做能完成的那一小块', 43),
    (N'今天适合把杂念清出去', 44),
    (N'把时间留给真正重要的人与事', 45),
    (N'不必证明什么，做好眼前即可', 46),
    (N'今天，让进度看得见', 47),
    (N'休息也是为了走得更远', 48),
    (N'先排列优先级，再打开待办', 49),
    (N'今天的努力，明天会记得', 50),
    (N'少抱怨一点，多推进一点', 51),
    (N'把难事放到精力最好的时段', 52),
    (N'今天，对未完成的事温柔一点', 53),
    (N'专注像肌肉，用了才更强', 54),
    (N'先写标题，思路就会来', 55),
    (N'今天适合收尾，也适合开始', 56),
    (N'把干扰名单列出来，然后关掉它们', 57),
    (N'小胜利也值得记一笔', 58),
    (N'今天，做让未来轻松一点的事', 59),
    (N'慢工出细活，细活出信任', 60),
    (N'把「以后」改成「今天先做一点」', 61),
    (N'今天的空气里，也有前进的空间', 62),
    (N'先问自己：什么最不能拖', 63),
    (N'把完美主义先请出房间', 64),
    (N'今天，留下清晰的下一步', 65),
    (N'做事时在场，比做得快更重要', 66),
    (N'把压力拆成可执行的步骤', 67),
    (N'今天适合复查，也适合创新', 68),
    (N'先兑现一个小目标，再谈更大的', 69),
    (N'把注意力从焦虑挪到行动上', 70),
    (N'今天，也请对自己诚实', 71),
    (N'完成闭环，比开很多头更有力', 72),
    (N'把灵感记下来，别让它溜走', 73),
    (N'今天适合整理，好让明天轻装', 74),
    (N'先把最难的那一下迈出去', 75),
    (N'把比较关掉，把标准立起来', 76),
    (N'今天，用结果说话', 77),
    (N'安静完成一件事，是最好的节奏', 78),
    (N'把「想太多」换成「做一点」', 79),
    (N'今天结束时，请留下一点满足感', 80)
    ) AS v(Content, SortOrder)
)
INSERT INTO dbo.WelcomeGreeting
    (Content, IsActive, SortOrder, IsDeleted, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
SELECT
    s.Content,
    1,
    s.SortOrder,
    0,
    @now,
    1,
    @now,
    1
FROM Seed s
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.WelcomeGreeting w
    WHERE w.Content = s.Content
);
GO
