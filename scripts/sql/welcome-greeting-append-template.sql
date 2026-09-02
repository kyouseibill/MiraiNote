-- WelcomeGreeting 每日追加模板
-- 用法：复制下方 INSERT 块，替换 Content / SortOrder；可一次追加多行。
-- 幂等：同一 Content 已存在则跳过（含软删行，避免唯一索引冲突）。
-- SortOrder 建议接续当前最大值：SELECT ISNULL(MAX(SortOrder), 0) + 1 FROM dbo.WelcomeGreeting;

SET NOCOUNT ON;

DECLARE @now DATETIME2 = SYSUTCDATETIME();

;WITH Seed(Content, SortOrder) AS
(
    SELECT * FROM (VALUES
        (N'在此填写新欢迎语（≤60字）', 81)
        -- ,(N'第二条欢迎语', 82)
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
