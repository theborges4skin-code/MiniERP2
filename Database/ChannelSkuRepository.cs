using System.Globalization;
using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// CSKU(채널별 SKU)에 대한 데이터베이스 작업을 처리합니다. (ChannelCode, CskuCode)가 고유키이며,
/// 같은 Msku(마스터SKU)를 가리키는 CskuCode가 한 채널에 여러 개 있을 수 있습니다(채널 옵션 분화).
/// </summary>
public class ChannelSkuRepository
{
    /// <param name="priceChangeReason">납품가가 실제로 바뀔 때만 ChannelSkuPriceHistory.Reason에 함께
    /// 기록되는 사유(예: "원자재 인상"). 가격조정 자동발행(M6)에서 사용— 값이 없으면 그냥 null로 기록.</param>
    public void Upsert(ChannelSkuModel csku, string? priceChangeReason = null)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var existing = GetByChannelAndCskuCode(connection, csku.ChannelCode, csku.CskuCode);
            var now = DateTime.Now;

            if (existing is not null && existing.SupplyPrice != csku.SupplyPrice)
            {
                using var historyCommand = connection.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = """
                    INSERT INTO ChannelSkuPriceHistory (ChannelCode, Msku, OldPrice, NewPrice, ChangedAt, Reason)
                    VALUES ($channelCode, $cskuCode, $oldPrice, $newPrice, $changedAt, $reason)
                    """;
                historyCommand.Parameters.AddWithValue("$channelCode", csku.ChannelCode);
                historyCommand.Parameters.AddWithValue("$cskuCode", csku.CskuCode);
                historyCommand.Parameters.AddWithValue("$oldPrice", existing.SupplyPrice);
                historyCommand.Parameters.AddWithValue("$newPrice", csku.SupplyPrice);
                historyCommand.Parameters.AddWithValue("$changedAt", now);
                historyCommand.Parameters.AddWithValue("$reason", (object?)priceChangeReason ?? DBNull.Value);
                historyCommand.ExecuteNonQuery();
            }

            if (existing is not null)
            {
                RecordFieldChange(connection, transaction, csku.ChannelCode, csku.CskuCode, "매칭된 마스터SKU", existing.Msku, csku.Msku, now);
                RecordFieldChange(connection, transaction, csku.ChannelCode, csku.CskuCode, "송장표시명", existing.InvoiceDisplayName, csku.InvoiceDisplayName, now);
                RecordFieldChange(connection, transaction, csku.ChannelCode, csku.CskuCode, "비고", existing.Note, csku.Note, now);
                RecordFieldChange(connection, transaction, csku.ChannelCode, csku.CskuCode, "제조원가(개별관리)",
                    FormatCostPriceOverrideForHistory(existing.CostPriceOverride), FormatCostPriceOverrideForHistory(csku.CostPriceOverride), now);
            }

            using var upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText = """
                INSERT INTO ChannelSkuTable (ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName, Note, UpdatedAt, Unit, Packing, CostPriceOverride)
                VALUES ($channelCode, $cskuCode, $msku, $supplyPrice, $invoiceDisplayName, $note, $updatedAt, $unit, $packing, $costPriceOverride)
                ON CONFLICT(ChannelCode, CskuCode) DO UPDATE SET
                    Msku = excluded.Msku,
                    SupplyPrice = excluded.SupplyPrice,
                    InvoiceDisplayName = excluded.InvoiceDisplayName,
                    Note = excluded.Note,
                    UpdatedAt = excluded.UpdatedAt,
                    Unit = excluded.Unit,
                    Packing = excluded.Packing,
                    CostPriceOverride = excluded.CostPriceOverride
                """;
            upsertCommand.Parameters.AddWithValue("$channelCode", csku.ChannelCode);
            upsertCommand.Parameters.AddWithValue("$cskuCode", csku.CskuCode);
            upsertCommand.Parameters.AddWithValue("$msku", csku.Msku);
            upsertCommand.Parameters.AddWithValue("$supplyPrice", csku.SupplyPrice);
            upsertCommand.Parameters.AddWithValue("$invoiceDisplayName", csku.InvoiceDisplayName ?? (object)DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$note", csku.Note ?? (object)DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$updatedAt", now);
            upsertCommand.Parameters.AddWithValue("$unit", csku.Unit);
            upsertCommand.Parameters.AddWithValue("$packing", csku.Packing ?? (object)DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$costPriceOverride", csku.CostPriceOverride ?? (object)DBNull.Value);
            upsertCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>변경된 필드만 <see cref="ChannelSkuFieldHistory"/>에 한 줄씩 기록한다(값이 같으면 기록하지 않음).</summary>
    private static void RecordFieldChange(SqliteConnection connection, SqliteTransaction transaction,
        string channelCode, string cskuCode, string fieldName, string? oldValue, string? newValue, DateTime changedAt)
    {
        if (string.Equals(oldValue ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal)) return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ChannelSkuFieldHistory (ChannelCode, CskuCode, FieldName, OldValue, NewValue, ChangedAt)
            VALUES ($channelCode, $cskuCode, $fieldName, $oldValue, $newValue, $changedAt)
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$cskuCode", cskuCode);
        command.Parameters.AddWithValue("$fieldName", fieldName);
        command.Parameters.AddWithValue("$oldValue", (object?)oldValue ?? DBNull.Value);
        command.Parameters.AddWithValue("$newValue", (object?)newValue ?? DBNull.Value);
        command.Parameters.AddWithValue("$changedAt", changedAt);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// CostPriceOverride를 이력 문자열로 바꾼다. NULL을 그냥 넘기면 RecordFieldChange가 빈 문자열로
    /// 정규화해 "연동 상태"와 "빈 값"이 구분되지 않으므로, 괄호로 시작해 어떤 숫자 표기와도 겹치지
    /// 않는 전용 문구로 표기한다(§4.3). 숫자는 로케일 의존을 피하려 InvariantCulture로 변환한다.
    /// </summary>
    private static string FormatCostPriceOverrideForHistory(decimal? value) =>
        value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : "(마스터 연동)";

    /// <summary>
    /// CSKU 코드 자체를 바꾼다("임시 SKU 등록"으로 급하게 만든 CSKU에 정식 코드를 부여하는 흐름
    /// 전용). CskuCode는 (ChannelCode, CskuCode) 기본키의 일부라 일반 Upsert로 새 코드를 저장하면
    /// 옛 코드 행이 그대로 남아 고아가 된다 — 여기서는 기본키를 직접 UPDATE하고, 가격/필드 변경
    /// 이력도 새 코드로 함께 옮겨 이력이 끊기지 않게 한다. 새 코드가 이미 다른 CSKU로 존재하면
    /// 예외를 던진다(호출 측에서 사전에 걸러도 되지만, 동시성 대비로 여기서도 한 번 더 확인한다).
    /// 매핑 규칙(RuleExact 등)이 옛 코드를 가리키고 있다면 별도로 MappingRepository.RetargetRules로
    /// 함께 옮겨야 한다(이 메서드는 CSKU 테이블만 책임진다).
    /// </summary>
    public void RenameCsku(string channelCode, string oldCskuCode, ChannelSkuModel updated)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var codeChanged = !string.Equals(oldCskuCode, updated.CskuCode, StringComparison.Ordinal);
            if (codeChanged && GetByChannelAndCskuCode(connection, channelCode, updated.CskuCode) != null)
                throw new InvalidOperationException($"CSKU 코드 '{updated.CskuCode}'가 이미 존재합니다.");

            var existing = GetByChannelAndCskuCode(connection, channelCode, oldCskuCode)
                ?? throw new InvalidOperationException($"CSKU '{oldCskuCode}'를 찾을 수 없습니다.");
            var now = DateTime.Now;

            if (existing.SupplyPrice != updated.SupplyPrice)
            {
                using var historyCommand = connection.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = """
                    INSERT INTO ChannelSkuPriceHistory (ChannelCode, Msku, OldPrice, NewPrice, ChangedAt, Reason)
                    VALUES ($channelCode, $cskuCode, $oldPrice, $newPrice, $changedAt, $reason)
                    """;
                historyCommand.Parameters.AddWithValue("$channelCode", channelCode);
                historyCommand.Parameters.AddWithValue("$cskuCode", updated.CskuCode);
                historyCommand.Parameters.AddWithValue("$oldPrice", existing.SupplyPrice);
                historyCommand.Parameters.AddWithValue("$newPrice", updated.SupplyPrice);
                historyCommand.Parameters.AddWithValue("$changedAt", now);
                historyCommand.Parameters.AddWithValue("$reason", DBNull.Value);
                historyCommand.ExecuteNonQuery();
            }

            if (codeChanged)
            {
                RecordFieldChange(connection, transaction, channelCode, oldCskuCode, "CSKU 코드", oldCskuCode, updated.CskuCode, now);

                using var movePriceHistoryCmd = connection.CreateCommand();
                movePriceHistoryCmd.Transaction = transaction;
                movePriceHistoryCmd.CommandText = "UPDATE ChannelSkuPriceHistory SET Msku = $newCode WHERE ChannelCode = $channelCode AND Msku = $oldCode";
                movePriceHistoryCmd.Parameters.AddWithValue("$newCode", updated.CskuCode);
                movePriceHistoryCmd.Parameters.AddWithValue("$channelCode", channelCode);
                movePriceHistoryCmd.Parameters.AddWithValue("$oldCode", oldCskuCode);
                movePriceHistoryCmd.ExecuteNonQuery();

                using var moveFieldHistoryCmd = connection.CreateCommand();
                moveFieldHistoryCmd.Transaction = transaction;
                moveFieldHistoryCmd.CommandText = "UPDATE ChannelSkuFieldHistory SET CskuCode = $newCode WHERE ChannelCode = $channelCode AND CskuCode = $oldCode";
                moveFieldHistoryCmd.Parameters.AddWithValue("$newCode", updated.CskuCode);
                moveFieldHistoryCmd.Parameters.AddWithValue("$channelCode", channelCode);
                moveFieldHistoryCmd.Parameters.AddWithValue("$oldCode", oldCskuCode);
                moveFieldHistoryCmd.ExecuteNonQuery();
            }

            RecordFieldChange(connection, transaction, channelCode, updated.CskuCode, "매칭된 마스터SKU", existing.Msku, updated.Msku, now);
            RecordFieldChange(connection, transaction, channelCode, updated.CskuCode, "송장표시명", existing.InvoiceDisplayName, updated.InvoiceDisplayName, now);
            RecordFieldChange(connection, transaction, channelCode, updated.CskuCode, "비고", existing.Note, updated.Note, now);

            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = """
                UPDATE ChannelSkuTable
                SET CskuCode = $newCskuCode, Msku = $msku, SupplyPrice = $supplyPrice, InvoiceDisplayName = $invoiceDisplayName,
                    Note = $note, UpdatedAt = $updatedAt, Unit = $unit, Packing = $packing, CostPriceOverride = $costPriceOverride
                WHERE ChannelCode = $channelCode AND CskuCode = $oldCskuCode
                """;
            updateCommand.Parameters.AddWithValue("$newCskuCode", updated.CskuCode);
            updateCommand.Parameters.AddWithValue("$msku", updated.Msku);
            updateCommand.Parameters.AddWithValue("$supplyPrice", updated.SupplyPrice);
            updateCommand.Parameters.AddWithValue("$invoiceDisplayName", updated.InvoiceDisplayName ?? (object)DBNull.Value);
            updateCommand.Parameters.AddWithValue("$note", updated.Note ?? (object)DBNull.Value);
            updateCommand.Parameters.AddWithValue("$updatedAt", now);
            updateCommand.Parameters.AddWithValue("$unit", updated.Unit);
            updateCommand.Parameters.AddWithValue("$packing", updated.Packing ?? (object)DBNull.Value);
            updateCommand.Parameters.AddWithValue("$costPriceOverride", updated.CostPriceOverride ?? (object)DBNull.Value);
            updateCommand.Parameters.AddWithValue("$channelCode", channelCode);
            updateCommand.Parameters.AddWithValue("$oldCskuCode", oldCskuCode);
            updateCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void Delete(string channelCode, string cskuCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. Delete price/field history first.
            using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = "DELETE FROM ChannelSkuPriceHistory WHERE ChannelCode = $channelCode AND Msku = $cskuCode";
            historyCommand.Parameters.AddWithValue("$channelCode", channelCode);
            historyCommand.Parameters.AddWithValue("$cskuCode", cskuCode);
            historyCommand.ExecuteNonQuery();

            using var fieldHistoryCommand = connection.CreateCommand();
            fieldHistoryCommand.Transaction = transaction;
            fieldHistoryCommand.CommandText = "DELETE FROM ChannelSkuFieldHistory WHERE ChannelCode = $channelCode AND CskuCode = $cskuCode";
            fieldHistoryCommand.Parameters.AddWithValue("$channelCode", channelCode);
            fieldHistoryCommand.Parameters.AddWithValue("$cskuCode", cskuCode);
            fieldHistoryCommand.ExecuteNonQuery();

            // 2. Delete the channel SKU.
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ChannelSkuTable WHERE ChannelCode = $channelCode AND CskuCode = $cskuCode";
            deleteCommand.Parameters.AddWithValue("$channelCode", channelCode);
            deleteCommand.Parameters.AddWithValue("$cskuCode", cskuCode);
            deleteCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// (채널, CSKU 코드)로 CSKU 1건을 조회합니다. 매핑 규칙의 TargetSku/OfsOrderItem.MappedSku가
    /// 실제로 가리키는 값이 CskuCode이므로, 매핑/출고/정산 전반에서 이 메서드가 1차 조회 지점이 됩니다.
    /// </summary>
    public ChannelSkuModel? GetByChannelAndCskuCode(string channelCode, string cskuCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        return GetByChannelAndCskuCode(connection, channelCode, cskuCode);
    }

    /// <summary>
    /// 매핑 규칙의 TargetSku(=code)가 가리키는 실제 마스터SKU를 찾습니다. CSKU로 등록되어 있으면
    /// 그 CSKU의 Msku를 반환하고, CSKU 레코드가 없으면(과거 방식으로 마스터SKU를 그대로 TargetSku로
    /// 쓴 단순 1:1/임시 규칙) code 자체를 마스터SKU로 간주해 그대로 반환합니다.
    /// </summary>
    public string ResolveMasterSku(string channelCode, string code)
    {
        var csku = GetByChannelAndCskuCode(channelCode, code);
        return csku?.Msku ?? code;
    }

    /// <summary>
    /// 지정된 마스터SKU를 가리키는 모든 CSKU를 채널 불문하고 가져옵니다.
    /// </summary>
    public List<ChannelSkuModel> GetAllByMsku(string msku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName, Note, UpdatedAt, Unit, Packing, CostPriceOverride
            FROM ChannelSkuTable
            WHERE Msku = $msku
            """;
        command.Parameters.AddWithValue("$msku", msku);

        var cskus = new List<ChannelSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cskus.Add(ReadChannelSku(reader));
        }
        return cskus;
    }

    public List<ChannelSkuPriceHistoryModel> GetPriceHistory(string channelCode, string cskuCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, Msku, OldPrice, NewPrice, ChangedAt, Reason, Note
            FROM ChannelSkuPriceHistory
            WHERE ChannelCode = $channelCode AND Msku = $cskuCode
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$cskuCode", cskuCode);

        var history = new List<ChannelSkuPriceHistoryModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            history.Add(new ChannelSkuPriceHistoryModel
            {
                Id = reader.GetInt64(0),
                ChannelCode = reader.GetString(1),
                CskuCode = reader.GetString(2),
                OldPrice = reader.GetDecimal(3),
                NewPrice = reader.GetDecimal(4),
                ChangedAt = reader.GetDateTime(5),
                Reason = reader.IsDBNull(6) ? null : reader.GetString(6),
                Note = reader.IsDBNull(7) ? null : reader.GetString(7),
            });
        }
        return history;
    }

    /// <summary>CSKU의 마스터SKU/송장표시명/비고 변경 이력을 시간순으로 가져옵니다(납품가 변경은 GetPriceHistory 참고).</summary>
    public List<ChannelSkuFieldHistoryModel> GetFieldHistory(string channelCode, string cskuCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, CskuCode, FieldName, OldValue, NewValue, ChangedAt
            FROM ChannelSkuFieldHistory
            WHERE ChannelCode = $channelCode AND CskuCode = $cskuCode
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$cskuCode", cskuCode);

        var history = new List<ChannelSkuFieldHistoryModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            history.Add(new ChannelSkuFieldHistoryModel
            {
                Id = reader.GetInt64(0),
                ChannelCode = reader.GetString(1),
                CskuCode = reader.GetString(2),
                FieldName = reader.GetString(3),
                OldValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                NewValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                ChangedAt = reader.GetDateTime(6),
            });
        }
        return history;
    }

    /// <summary>
    /// 지정된 채널의 모든 CSKU(채널별 SKU 설정: 납품가/송장표시명)를 한 번에 가져옵니다.
    /// SkuMapper가 매핑 시마다 따로 조회하지 않도록 채널 단위로 묶어서 제공합니다.
    /// </summary>
    public List<ChannelSkuModel> GetAllByChannel(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName, Note, UpdatedAt, Unit, Packing, CostPriceOverride FROM ChannelSkuTable WHERE ChannelCode = $channelCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);

        var cskus = new List<ChannelSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cskus.Add(ReadChannelSku(reader));
        }
        return cskus;
    }

    /// <summary>
    /// 해당 (채널, CSKU코드) 조합이 아직 없으면 새로 생성하고 true를 반환한다.
    /// 이미 존재하면 아무것도 하지 않고 false를 반환한다. 두 화면(MappingForm,
    /// OrderSkuMappingDialog)의 중복된 "존재 확인 후 Upsert" 로직을 한 곳으로 모은 것.
    /// </summary>
    public bool CreateIfNew(string channelCode, string cskuCode, string masterSku,
        decimal supplyPrice, string? invoiceDisplayName)
    {
        if (GetByChannelAndCskuCode(channelCode, cskuCode) != null) return false;
        Upsert(new ChannelSkuModel
        {
            ChannelCode = channelCode,
            CskuCode = cskuCode,
            Msku = masterSku,
            SupplyPrice = supplyPrice,
            InvoiceDisplayName = string.IsNullOrWhiteSpace(invoiceDisplayName) ? null : invoiceDisplayName.Trim(),
        });
        return true;
    }

    /// <summary>채널 불문하고 모든 CSKU를 가져옵니다(데이터 관리창의 전체 내보내기/조회용).</summary>
    public List<ChannelSkuModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName, Note, UpdatedAt, Unit, Packing, CostPriceOverride FROM ChannelSkuTable";

        var cskus = new List<ChannelSkuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cskus.Add(ReadChannelSku(reader));
        }
        return cskus;
    }

    private static ChannelSkuModel? GetByChannelAndCskuCode(SqliteConnection connection, string channelCode, string cskuCode)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName, Note, UpdatedAt, Unit, Packing, CostPriceOverride FROM ChannelSkuTable WHERE ChannelCode = $channelCode AND CskuCode = $cskuCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$cskuCode", cskuCode);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadChannelSku(reader) : null;
    }

    private static ChannelSkuModel ReadChannelSku(SqliteDataReader reader) => new()
    {
        ChannelCode = reader.GetString(0),
        CskuCode = reader.GetString(1),
        Msku = reader.GetString(2),
        SupplyPrice = reader.GetDecimal(3),
        InvoiceDisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
        Note = reader.IsDBNull(5) ? null : reader.GetString(5),
        UpdatedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        Unit = reader.IsDBNull(7) ? "kg" : reader.GetString(7),
        Packing = reader.IsDBNull(8) ? null : reader.GetString(8),
        CostPriceOverride = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
    };
}
