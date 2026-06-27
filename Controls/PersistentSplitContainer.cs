using System.ComponentModel;
using MiniERP2.Config;

namespace MiniERP2.Controls;

/// <summary>
/// 사용자가 조절한 분할 위치(SplitterDistance)를 기억하는 SplitContainer입니다.
/// PersistenceKey를 지정하면 저장된 위치를 불러오고, 사용자가 분할선을 옮길 때마다 자동으로 저장합니다.
/// </summary>
public class PersistentSplitContainer : SplitContainer
{
    private readonly SplitterSettingsService _settingsService = new();
    private string _persistenceKey = string.Empty;
    private int? _pendingDistance;

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
            TryApplyPendingDistance();
        }
    }

    public PersistentSplitContainer()
    {
        SplitterMoved += (s, e) => SaveDistance();
    }

    /// <summary>
    /// 컨트롤이 아직 부모에 붙기 전(크기가 0)에 PersistenceKey가 설정되면 SplitterDistance를
    /// 바로 적용할 수 없으므로(범위를 벗어나 예외 발생), 실제 크기가 잡힐 때까지 재시도한다.
    /// </summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        TryApplyPendingDistance();
    }

    private void TryApplyPendingDistance()
    {
        if (_pendingDistance is null) return;

        var availableSize = Orientation == Orientation.Horizontal ? Height : Width;
        if (availableSize <= 0) return; // 아직 레이아웃 전 — 다음 SizeChanged에서 재시도

        try
        {
            SplitterDistance = _pendingDistance.Value;
        }
        catch (ArgumentOutOfRangeException)
        {
            return; // 컨트롤 크기가 아직 부족하면 다음 크기 변경에서 다시 시도한다.
        }

        _pendingDistance = null; // 한 번 적용되면 이후 크기 변경에서 사용자가 조절한 값을 덮어쓰지 않는다.
    }

    private void SaveDistance()
    {
        if (string.IsNullOrEmpty(_persistenceKey) || DesignMode) return;
        _settingsService.SaveDistance(_persistenceKey, SplitterDistance);
    }
}
