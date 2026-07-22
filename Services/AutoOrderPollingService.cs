using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>
/// 자동발주처리 3경로 폴링(시작시1회/30분 타이머/수동 버튼)의 공통 로직
/// (02_자동발주처리_MiniERP2연동_설계.md §4). manifest.items를 순회해 로컬에 없는 Id만
/// AutoOrderInboxTable에 Status=new로 적재하고, 신규 건수를 반환해 알림에 쓰게 한다.
/// </summary>
public class AutoOrderPollingService
{
    private readonly IAutoOrderDriveClient _driveClient;
    private readonly AutoOrderInboxRepository _inboxRepository;

    public AutoOrderPollingService(IAutoOrderDriveClient driveClient, AutoOrderInboxRepository? inboxRepository = null)
    {
        _driveClient = driveClient;
        _inboxRepository = inboxRepository ?? new AutoOrderInboxRepository();
    }

    /// <summary>
    /// manifest를 조회해 신규 항목만 로컬에 적재하고 그 건수를 반환한다.
    /// <paramref name="allowInteractiveAuth"/>가 false인데 캐시된 로그인이 없으면(아직 인증 전)
    /// 조용히 0을 반환한다 — 시작시1회/30분 백그라운드 타이머가 브라우저를 불쑥 띄우지 않게
    /// 하기 위함이다. 캐시된 로그인이 있으면 이 값과 무관하게 조용히 갱신(리프레시)한다.
    /// 사용자가 명시적으로 [자동발주 확인]/[인증하기]를 눌렀을 때만 true로 호출해야 한다.
    /// </summary>
    public async Task<int> PollAsync(bool allowInteractiveAuth, CancellationToken cancellationToken = default)
    {
        if (!allowInteractiveAuth && !_driveClient.HasCachedAuthorization())
        {
            return 0;
        }

        await _driveClient.AuthorizeAsync(cancellationToken);

        var manifest = await _driveClient.FetchManifestAsync(cancellationToken);
        if (manifest == null) return 0;

        var newCount = 0;
        foreach (var item in manifest.Items)
        {
            if (_inboxRepository.Exists(item.Id)) continue;

            _inboxRepository.InsertIfNew(new AutoOrderInboxItem
            {
                Id = item.Id,
                SubjectSnip = item.Subject,
                ReceivedAt = item.ReceivedAt,
                XlsxPath = item.XlsxPath,
                Sha256 = item.XlsxSha256,
                RowCount = item.RowCount,
                ParseStatus = item.ParseStatus,
                SeenAt = DateTime.Now,
            });
            newCount++;
        }
        return newCount;
    }
}
