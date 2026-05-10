using Microsoft.Data.SqlClient;
using System.Reflection;

namespace WarrantApi.Tests.Services;

/// <summary>
/// SqlException 建立工具，因 SqlException 無公開建構子，透過反射建立測試用例。
/// 僅供測試使用，不得在生產程式碼引用。
/// </summary>
internal static class SqlExceptionFactory
{
    /// <summary>
    /// 建立指定錯誤編號的 SqlException。
    /// 錯誤編號 2627 = 唯一約束違反；2601 = 唯一索引違反。
    /// </summary>
    public static SqlException Create(int errorNumber)
    {
        var sqlClientAssembly = typeof(SqlException).Assembly;

        // 建立 SqlErrorCollection（private ctor，無參數）
        var collectionType = sqlClientAssembly
            .GetType("Microsoft.Data.SqlClient.SqlErrorCollection")!;
        var collection = Activator.CreateInstance(
            collectionType, nonPublic: true)!;

        // 建立 SqlError（選用 8 參數版本：infoNumber, errorState, errorClass, server, errorMessage, procedure, lineNumber, exception）
        var errorType = sqlClientAssembly
            .GetType("Microsoft.Data.SqlClient.SqlError")!;
        var errorCtor = errorType
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters().Length == 8);

        var sqlError = errorCtor.Invoke(new object?[]
        {
            errorNumber,   // infoNumber
            (byte)0,       // errorState
            (byte)16,      // errorClass（severity）
            "test-server", // server
            "Violation of UNIQUE KEY constraint",  // errorMessage
            "test-proc",   // procedure
            0,             // lineNumber
            (Exception?)null // exception
        });

        // 呼叫 SqlErrorCollection.Add（internal 方法）
        var addMethod = collectionType.GetMethod(
            "Add", BindingFlags.NonPublic | BindingFlags.Instance)!;
        addMethod.Invoke(collection, new[] { sqlError });

        // 建立 SqlException（private ctor：message, SqlErrorCollection, innerException, conId）
        var sqlExCtor = typeof(SqlException)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c =>
            {
                var p = c.GetParameters();
                return p.Length == 4
                    && p[0].ParameterType == typeof(string)
                    && p[1].ParameterType == collectionType;
            });

        return (SqlException)sqlExCtor.Invoke(new object?[]
        {
            "Test SqlException",
            collection,
            (Exception?)null,
            Guid.NewGuid()
        });
    }
}
