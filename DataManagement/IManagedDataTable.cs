using System.Data;

namespace MiniERP2.DataManagement;

/// <summary>
/// 데이터 관리창이 다루는 한 종류의 DB 테이블(마스터SKU/CSKU/매핑규칙 등)을 추상화합니다.
/// 화면은 <see cref="System.Data.DataTable"/>을 통해 조회/편집/스테이징하고, 실제 DB 반영은
/// <see cref="ManagedTableChangeApplier"/>가 RowState(Added/Modified/Deleted)에 따라 이 어댑터의
/// Insert/Update/Delete를 호출해 수행합니다.
/// </summary>
public interface IManagedDataTable
{
    string DisplayName { get; }

    /// <summary>중복/매칭 판단에 쓰는 자연키 컬럼 이름들입니다(예: ["ChannelCode", "Key"]).</summary>
    string[] KeyColumns { get; }

    /// <summary>현재 DB 상태를 컬럼이 정의된 빈 DataTable에 채워서 반환합니다(AcceptChanges 호출됨).</summary>
    DataTable LoadCurrent();

    /// <summary>새 행을 추가합니다(row의 현재 값 기준).</summary>
    void Insert(DataRow row);

    /// <summary>기존 행을 키 컬럼 값으로 찾아 갱신합니다(row의 현재 값 기준, 키 컬럼은 바뀌지 않은 것으로 가정).</summary>
    void Update(DataRow row);

    /// <summary>기존 행을 키 컬럼 값으로 찾아 삭제합니다(row[col, DataRowVersion.Original] 기준).</summary>
    void Delete(DataRow row);
}
