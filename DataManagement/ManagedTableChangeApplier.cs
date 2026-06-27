using System.Data;

namespace MiniERP2.DataManagement;

/// <summary>변경내역 저장 시 결과 건수입니다.</summary>
public record ApplyResult(int Inserted, int Updated, int Deleted)
{
    public int Total => Inserted + Updated + Deleted;
}

/// <summary>
/// 스테이징된 DataTable의 변경분(GetChanges)을 어댑터의 Insert/Update/Delete로 실제 DB에 반영합니다.
/// 자연키 컬럼 값이 바뀐 수정(이름변경)은 어댑터가 Update만으로 처리할 수 없으므로(매칭 대상이
/// 사라짐), 키가 바뀌었으면 옛 키로 삭제 후 새 키로 삽입하는 두 단계로 자동 분리합니다.
/// </summary>
public static class ManagedTableChangeApplier
{
    public static ApplyResult Apply(IManagedDataTable adapter, DataTable table)
    {
        var changes = table.GetChanges();
        if (changes == null) return new ApplyResult(0, 0, 0);

        int inserted = 0, updated = 0, deleted = 0;
        foreach (DataRow row in changes.Rows)
        {
            switch (row.RowState)
            {
                case DataRowState.Added:
                    adapter.Insert(row);
                    inserted++;
                    break;
                case DataRowState.Deleted:
                    adapter.Delete(row);
                    deleted++;
                    break;
                case DataRowState.Modified:
                    if (KeyChanged(adapter, row))
                    {
                        adapter.Delete(row);
                        adapter.Insert(row);
                        deleted++;
                        inserted++;
                    }
                    else
                    {
                        adapter.Update(row);
                        updated++;
                    }
                    break;
            }
        }

        table.AcceptChanges();
        return new ApplyResult(inserted, updated, deleted);
    }

    private static bool KeyChanged(IManagedDataTable adapter, DataRow row)
    {
        foreach (var column in adapter.KeyColumns)
        {
            if (!Equals(row[column, DataRowVersion.Original], row[column, DataRowVersion.Current])) return true;
        }
        return false;
    }
}
