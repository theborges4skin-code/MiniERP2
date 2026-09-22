using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>
/// 배송지 주소록(AddressBookTable + AddressChannelTagTable)을 데이터 관리창에서 다룰 수 있게
/// 합니다. 채널 태그는 다대다 관계라 엑셀 한 행에 담기 위해 "ChannelTags" 한 열에 쉼표로 구분한
/// 채널코드 목록으로 직렬화합니다(비어있으면 모든 채널 공용).
/// </summary>
public class AddressBookManagedTable : IManagedDataTable
{
    private readonly AddressBookRepository _repository = new();

    // 엑셀 내보내기 맨 위에 항상 붙는 예시 행의 표식. 사용자가 이 행을 지우지 않고 그대로
    // 업로드해도 Label 값이 이 상수와 정확히 같은 행은 실제 데이터로 반영되지 않는다.
    private const string SampleLabel = "(예시) 이 줄은 지우지 않아도 자동으로 제외됩니다";

    public string DisplayName => "주소록";
    public string[] KeyColumns => ["AddressId"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        var idColumn = table.Columns.Add("AddressId", typeof(int));
        idColumn.ReadOnly = true; // 자동 발급되는 내부 식별자 — 그리드에서 직접 편집 금지.
        table.Columns.Add("Label", typeof(string));
        table.Columns.Add("ReceiverName", typeof(string));
        table.Columns.Add("Phone", typeof(string));
        table.Columns.Add("Address", typeof(string));
        table.Columns.Add("Memo", typeof(string));
        table.Columns.Add("IsActive", typeof(bool));
        table.Columns.Add("DisplayOrder", typeof(int));
        table.Columns.Add("ChannelTags", typeof(string));
        table.PrimaryKey = [idColumn];

        foreach (var entry in _repository.GetAll())
        {
            table.Rows.Add(entry.AddressId, entry.Label, entry.ReceiverName, entry.Phone, entry.Address,
                entry.Memo, entry.IsActive, entry.DisplayOrder, string.Join(",", entry.ChannelTags));
        }
        table.AcceptChanges();
        return table;
    }

    // 신규 행은 AddressId가 0(미발급)인 채로 들어오는데, Repository.Upsert가 그 경우를 신규
    // 등록으로 인식해 처리한다(AddressBookRepository.Upsert 참고).
    public void Insert(DataRow row) => _repository.Upsert(ToEntry(row, addressId: 0));

    public void Update(DataRow row)
    {
        var addressId = Convert.ToInt32(row["AddressId", DataRowVersion.Original]);
        _repository.Upsert(ToEntry(row, addressId));
    }

    public void Delete(DataRow row)
    {
        var addressId = Convert.ToInt32(row["AddressId", DataRowVersion.Original]);
        _repository.Delete(addressId);
    }

    public DataRow CreateSampleRow(DataTable table)
    {
        var row = table.NewRow();
        row["Label"] = SampleLabel;
        row["ReceiverName"] = "홍길동";
        row["Phone"] = "010-1234-5678";
        row["Address"] = "서울시 강남구 테헤란로 000";
        row["Memo"] = "실제 주소는 이 아래 행부터 입력하세요";
        row["IsActive"] = true;
        row["DisplayOrder"] = 0;
        row["ChannelTags"] = "";
        return row;
    }

    public bool IsSampleRow(IReadOnlyDictionary<string, string?> rawRow) =>
        rawRow.TryGetValue("Label", out var label) && label == SampleLabel;

    private static AddressBookEntry ToEntry(DataRow row, int addressId) => new()
    {
        AddressId = addressId,
        Label = row["Label"] as string ?? string.Empty,
        ReceiverName = row["ReceiverName"] as string ?? string.Empty,
        Phone = row["Phone"] as string ?? string.Empty,
        Address = row["Address"] as string ?? string.Empty,
        Memo = row["Memo"] as string ?? string.Empty,
        // 빈 셀(엑셀에서 사용여부를 비워둔 경우)은 AddressBookEntry 기본값과 맞춰 "사용"으로 취급한다.
        IsActive = row["IsActive"] is DBNull || Convert.ToBoolean(row["IsActive"]),
        DisplayOrder = row["DisplayOrder"] is DBNull ? 0 : Convert.ToInt32(row["DisplayOrder"]),
        ChannelTags = (row["ChannelTags"] as string ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList(),
    };
}
