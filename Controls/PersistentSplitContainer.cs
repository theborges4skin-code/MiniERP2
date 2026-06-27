using System.ComponentModel;
using MiniERP2.Config;

namespace MiniERP2.Controls;

/// <summary>
/// 사용자가 조절한 분할 위치(SplitterDistance)를 기억하는 SplitContainer입니다.
/// PersistenceKey를 지정하면 저장된 위치를 불러오고, 사용자가 분할선을 옮길 때마다 자동으로 저장합니다.
/// </summary>
public class PersistentSplitContainer : SplitContainer
{
    private const int MaxApplyRetries = 10;

    private readonly SplitterSettingsService _settingsService = new();
    private string _persistenceKey = string.Empty;
    private int? _pendingDistance;
    private int _applyRetryCount;

    [Category("Behavior")]
    [Description("분할 위치를 저장하고 불러오는 데 사용할 고유 키입니다.")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PersistenceKey
    {
        get => _persistenceKey;
        set
        {
            _persistenceKey = value;
            if (string.IsNullOrEmpty(_persistenceKey) || DesignMode) return;

            _pendingDistance = _settingsService.LoadDistance(_persistenceKey);
            _applyRetryCount = 0;
            if (IsHandleCreated) ScheduleApplyPendingDistance();
        }
    }

    public PersistentSplitContainer()
    {
        HandleCreated += (s, e) => ScheduleApplyPendingDistance();
        SplitterMoved += (s, e) => SaveDistance();
    }

    /// <summary>
    /// 핸들이 생성된 시점에도 TableLayoutPanel/Dock 레이아웃이 아직 최종 크기로 정착하지 않았을 수
    /// 있어, SplitterDistance를 그 자리에서 바로 적용하면 이후 레이아웃이 한 번 더 일어날 때
    /// SplitContainer의 기본 동작(분할 비율을 유지하려는 자동 재계산)에 의해 값이 어긋난다.
    /// 메시지 루프가 한 번 돌고 레이아웃이 정착한 뒤(BeginInvoke)에 적용해야 정확하다.
    /// </summary>
    private void ScheduleApplyPendingDistance()
    {
        if (_pendingDistance is null) return;
        BeginInvoke(new Action(TryApplyPendingDistance));
    }

    private void TryApplyPendingDistance()
    {
        if (_pendingDistance is null || IsDisposed) return;

        try
        {
            SplitterDistance = _pendingDistance.Value;
        }
        catch (ArgumentOutOfRangeException)
        {
            // 컨트롤이 여전히 충분히 크지 않으면 다음 메시지 루프 틱에서 다시 시도한다(무한 재시도 방지를 위해 횟수 제한).
            if (++_applyRetryCount < MaxApplyRetries)
            {
                BeginInvoke(new Action(TryApplyPendingDistance));
            }
            return;
        }

        _pendingDistance = null; // 한 번 적용되면 이후 사용자가 조절한 값을 덮어쓰지 않는다.
    }

    private void SaveDistance()
    {
        if (string.IsNullOrEmpty(_persistenceKey) || DesignMode) return;
        _settingsService.SaveDistance(_persistenceKey, SplitterDistance);
    }
}
