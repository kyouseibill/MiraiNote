-- 清理 LifeLog 中存储的错误图片路径格式
-- 将物理路径格式转换为相对 URL 路径格式

-- 备份原始数据（以防需要回滚）
-- SELECT Id, ImagePath FROM LifeLog WHERE ImagePath LIKE 'D:\%' OR ImagePath LIKE 'D:%' ORDER BY Id;

-- 修正图片路径：D:\webroot\MiraiNote\fileservice\uploads\{userId}\images\{filename}
-- 转为：/uploads/{userId}/images/{filename}

UPDATE LifeLog
SET ImagePath = 
    CASE 
        -- 处理 Windows 路径格式：D:\webroot\MiraiNote\fileservice\uploads\11\images\...
        WHEN ImagePath LIKE 'D:\webroot\MiraiNote\fileservice\uploads\%' THEN
            '/' + REPLACE(
                SUBSTRING(ImagePath, LEN('D:\webroot\MiraiNote\fileservice\') + 1),
                '\',
                '/'
            )
        -- 处理其他可能的路径变体
        WHEN ImagePath LIKE 'D:%' THEN
            '/' + REPLACE(
                SUBSTRING(ImagePath, CHARINDEX('uploads', ImagePath)),
                '\',
                '/'
            )
        ELSE ImagePath
    END
WHERE ImagePath IS NOT NULL 
  AND (ImagePath LIKE 'D:\%' OR ImagePath LIKE 'D:%')
  AND ImagePath NOT LIKE '/uploads/%';

-- 验证修正结果
SELECT Id, CreatedAt, ImagePath FROM LifeLog 
WHERE ImagePath IS NOT NULL 
  AND ImagePath NOT LIKE '' 
ORDER BY CreatedAt DESC;

-- 统计修正数量
SELECT COUNT(*) AS ModifiedCount FROM LifeLog 
WHERE ImagePath LIKE '/uploads/%' AND CreatedAt >= CAST(GETDATE() AS date);
