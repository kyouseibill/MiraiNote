-- =====================================================
-- 清理非管理员用户及其关联数据
-- 保留：Id = 1 的 Admin 用户
-- 影响表：EmailVerifyToken、RefreshToken、User
-- 执行账户：Bill.Gong（MigrationConnection）
-- =====================================================

BEGIN TRANSACTION;

-- 1. 删除非管理员用户的邮箱验证/密码重置 Token
DELETE FROM [dbo].[EmailVerifyToken]
WHERE [UserId] != 1;

-- 2. 删除非管理员用户的 RefreshToken
DELETE FROM [dbo].[RefreshToken]
WHERE [UserId] != 1;

-- 3. 删除非管理员用户（包含软删除记录）
DELETE FROM [dbo].[User]
WHERE [Id] != 1;

-- 验证结果
SELECT '清理完成' AS [状态];
SELECT COUNT(*) AS [剩余用户数] FROM [dbo].[User];
SELECT COUNT(*) AS [剩余RefreshToken数] FROM [dbo].[RefreshToken];
SELECT COUNT(*) AS [剩余EmailVerifyToken数] FROM [dbo].[EmailVerifyToken];

COMMIT TRANSACTION;
