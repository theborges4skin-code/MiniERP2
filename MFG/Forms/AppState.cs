using MiniERP2.MFG.Config;
using MiniERP2.MFG.Integration;

namespace MiniERP2.MFG.Forms;

/// <summary>탭들이 공유하는 상태: VAT 보기 모드, 설정, 데이터 변경 알림.</summary>
public class AppState
{
    public MfgSettings Settings { get; } = MfgSettings.Load();

    public bool ShowVatIncluded
    {
        get => Settings.ShowVatIncluded;
        set
        {
            if (Settings.ShowVatIncluded == value) return;
            Settings.ShowVatIncluded = value;
            Settings.Save();
            VatModeChanged?.Invoke();
        }
    }

    public string VatLabel => Domain.Vat.ModeLabel(ShowVatIncluded);

    public event Action? VatModeChanged;

    /// <summary>어느 탭에서든 데이터를 저장하면 호출 → 다른 탭이 다음에 보일 때 다시 읽는다.</summary>
    public event Action? DataChanged;

    public void NotifyDataChanged() => DataChanged?.Invoke();

    public MiniErp2Bridge Bridge => new(Settings.ResolvedMiniErp2DatabasePath);
}

/// <summary>탭 공통: 보일 때 다시 읽기.</summary>
public interface IMfgTab
{
    void RefreshData();
}
