using Microsoft.EntityFrameworkCore;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 只有唯一约束冲突算「已经有人占了这条提醒」。其它数据库错误照常抛出。
/// SQL Server 是 2601 / 2627。测试用的 SQLite 唯一约束是扩展错误 2067，走同一条幂等路径。
/// </summary>
internal static class HouseholdUniqueConflict
{
    public static bool IsExpected(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current != null; current = current.InnerException)
        {
            if (IsSqlServerUnique(current) || IsSqliteUnique(current))
                return true;
        }

        return false;
    }

    internal static bool IsSqlServerUniqueNumber(int number) => number is 2601 or 2627;

    private static bool IsSqlServerUnique(Exception exception)
    {
        if (exception.GetType().FullName != "Microsoft.Data.SqlClient.SqlException")
            return false;

        var number = exception.GetType().GetProperty("Number")?.GetValue(exception);
        return number is int value && IsSqlServerUniqueNumber(value);
    }

    private static bool IsSqliteUnique(Exception exception)
    {
        if (exception.GetType().FullName != "Microsoft.Data.Sqlite.SqliteException")
            return false;

        var extended = exception.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(exception);
        return extended != null && Convert.ToInt32(extended) == 2067;
    }
}
