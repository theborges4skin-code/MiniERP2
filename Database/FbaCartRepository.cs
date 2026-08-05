using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// FBA 발주 화면의 "미배정 품목" 임시저장(1회) 장바구니를 다룬다. 발주번호와 무관한 단일 슬롯이라
/// 저장할 때마다 전체를 지우고 다시 채운다(ExportSummaryDraftRepository.SaveForMarket과 동일 패턴).
/// </summary>
public class FbaCartRepository
{
    public List<FbaCartItemModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Csku, Qty, ExpiryDate FROM FbaCartItem ORDER BY Id";

        var result = new List<FbaCartItemModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new FbaCartItemModel
            {
                Csku = reader.GetString(0),
                Qty = reader.GetInt32(1),
                ExpiryDate = reader.IsDBNull(2) ? null : reader.GetString(2),
            });
        }
        return result;
    }

    public void SaveAll(IEnumerable<FbaCartItemModel> items)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM FbaCartItem";
            deleteCommand.ExecuteNonQuery();
        }

        using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO FbaCartItem (Csku, Qty, ExpiryDate, SavedAt)
                VALUES ($csku, $qty, $expiryDate, $savedAt)
                """;
            var cskuParam = insertCommand.Parameters.Add("$csku", SqliteType.Text);
            var qtyParam = insertCommand.Parameters.Add("$qty", SqliteType.Integer);
            var expiryParam = insertCommand.Parameters.Add("$expiryDate", SqliteType.Text);
            var savedAtParam = insertCommand.Parameters.Add("$savedAt", SqliteType.Text);

            foreach (var item in items)
            {
                cskuParam.Value = item.Csku;
                qtyParam.Value = item.Qty;
                expiryParam.Value = (object?)item.ExpiryDate ?? DBNull.Value;
                savedAtParam.Value = now;
                insertCommand.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }
}
