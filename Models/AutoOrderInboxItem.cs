namespace MiniERP2.Models;

/// <summary>
/// 자동발주처리(Gmail 자동화) — Drive manifest.json에서 감지한 항목의 로컬 처리상태.
/// 상품/수취인 등 업무·개인정보는 담지 않는다(그건 xlsx 안에만 존재) — 02_자동발주처리_
/// MiniERP2연동_설계.md §3.
/// </summary>
public class AutoOrderInboxItem
{
    /// <summary>manifest item.id(수신시각_해시 형태) — 전역 유일, 로컬 중복 알림 방지 키.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>이메일 제목 일부(알림 표시용).</summary>
    public string SubjectSnip { get; set; } = string.Empty;

    /// <summary>이메일 수신시각. 발주일 미기재 시(파일 01 §3) 이 값으로 대체한다.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>Drive 상 경로(pending/{id}.xlsx).</summary>
    public string XlsxPath { get; set; } = string.Empty;

    /// <summary>manifest에 기록된 무결성 해시 — 다운로드한 파일과 대조해 변조/부분업로드를 걸러낸다.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public int RowCount { get; set; }

    /// <summary>ok | partial | failed.</summary>
    public string ParseStatus { get; set; } = "ok";

    /// <summary>new | downloaded | imported | dismissed.</summary>
    public string Status { get; set; } = "new";

    /// <summary>다운로드해 저장한 로컬 경로(다운로드 전이면 null).</summary>
    public string? LocalFilePath { get; set; }

    /// <summary>이 항목을 로컬에서 최초로 감지한 시각.</summary>
    public DateTime SeenAt { get; set; }

    /// <summary>"발주 파일 로드로 열기"로 실제 임포트된 시각(임포트 전이면 null).</summary>
    public DateTime? ImportedAt { get; set; }
}
