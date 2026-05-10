namespace WarrantApi.Domain.Enums;

/// <summary>
/// 權證類型。
/// CALL：認購權證（看多），PUT：認售權證（看空）。
/// 對應資料庫 Warrant_Type VARCHAR(4)。
/// </summary>
public enum WarrantType
{
    CALL,
    PUT
}
