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

    /// <summary>
    /// 엑셀 내보내기 맨 첫 데이터행에 넣을 예시 행(선택 구현). 기본값 null이면 예시 행 없이
    /// 기존과 동일하게 동작한다. 구현하면 DB에 저장된 실제 데이터 위에 이 예시 행이 항상 먼저
    /// 나온다 — DB가 비어 있어도 양식 역할을 하고, 데이터가 있어도 그 위에 이어 붙는다.
    /// </summary>
    DataRow? CreateSampleRow(DataTable table) => null;

    /// <summary>
    /// 엑셀 불러오기 시 이 행을 예시 행으로 보고 자동 제외할지 판단한다(선택 구현). 사용자가
    /// 예시 행을 지우지 않고 그대로 업로드해도 실제 데이터로 반영되지 않게 하기 위함이다.
    /// </summary>
    bool IsSampleRow(IReadOnlyDictionary<string, string?> rawRow) => false;
}
