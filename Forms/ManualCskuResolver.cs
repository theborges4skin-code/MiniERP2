using MiniERP2.Database;
using MiniERP2.UI;
using MiniERP2.Utils;

namespace MiniERP2.Forms;

/// <summary>
/// 수동 주문/CSKU 배정에서 "이 채널에서 쓸 CSKU"를 고르는 공통 흐름. 수동 주문 창과 여러 품목 배정
/// 창이 같은 규칙(다른 채널 CSKU·마스터SKU를 고르면 이 채널용 CSKU를 그 자리에서 등록)을 쓰도록
/// 한 곳에 모았다.
/// </summary>
public class ManualCskuResolver
{
    private readonly ChannelSkuRepository _channelSkuRepository;

    public string ChannelCode { get; }
    public string ChannelName { get; }

    public ManualCskuResolver(string channelCode, string channelName, ChannelSkuRepository channelSkuRepository)
    {
        ChannelCode = channelCode;
        ChannelName = channelName;
        _channelSkuRepository = channelSkuRepository;
    }

    /// <summary>
    /// 출고 이력이 없는 신규 품목은 "자주 사용한 품목" 목록에 나타나지 않아 전부 수기입력해야
    /// 했다 — 전체 채널의 CSKU를 검색해 고를 수 있게 한다(다른 채널 품목을 이 채널 주문에 그대로
    /// 가져다 쓰는 경우가 실제로 있어 제한하지 않음). 다만 현재 채널의 CSKU를 목록 맨 위로 올리고
    /// 채널 콤보도 "(전체)"로 시작해 바로 다른 채널까지 훑어볼 수 있게 한다.
    /// </summary>
    public (string CskuCode, string ProductName)? PickBySearch(IWin32Window owner)
    {
        using var picker = new CskuPickerDialog(ChannelCode, preselectChannelFilter: false);
        if (FormManager.ShowDialogSafe(picker, owner) != DialogResult.OK || picker.SelectedCskuCode == null) return null;

        var cskuCode = picker.SelectedCskuCode;
        var productName = picker.SelectedItemName ?? cskuCode;

        // 다른 채널에서 고른 품목이면 그 CSKU 코드는 이 채널에 등록된 게 아니므로(CSKU는
        // (채널, 코드) 복합키), 같은 마스터SKU/납품가/송장표시명으로 이 채널용 CSKU를 새로
        // 등록해서 써야 매핑/출고 단계에서 정상적으로 해석된다.
        if (!string.Equals(picker.SelectedChannelCode, ChannelCode, StringComparison.OrdinalIgnoreCase))
        {
            var newCskuCode = CskuCodeGenerator.BuildDefault(ChannelName, picker.SelectedMsku ?? cskuCode);
            _channelSkuRepository.CreateIfNew(ChannelCode, newCskuCode, picker.SelectedMsku ?? cskuCode, picker.SelectedUnitPrice, productName);
            cskuCode = newCskuCode;
        }

        return (cskuCode, productName);
    }

    /// <summary>
    /// 마스터SKU를 검색해 고른다(샘플발송이력관리 §2 D4). CSKU가 없으면 그 자리에서 자동 생성한다
    /// (납품가 0, 송장표시명은 마스터 상품명) — CreateIfNew는 이미 있으면 그대로 재사용하므로
    /// 같은 마스터SKU를 여러 번 골라도 중복 생성되지 않는다.
    /// </summary>
    public (string CskuCode, string ProductName)? PickByMasterSku(IWin32Window owner)
    {
        using var picker = new MasterSkuPickerDialog();
        if (FormManager.ShowDialogSafe(picker, owner) != DialogResult.OK || picker.SelectedSku == null) return null;

        var masterSku = picker.SelectedSku;
        var itemName = picker.SelectedItemName ?? masterSku;
        var cskuCode = CskuCodeGenerator.BuildDefault(ChannelName, masterSku);
        _channelSkuRepository.CreateIfNew(ChannelCode, cskuCode, masterSku, 0m, itemName);
        return (cskuCode, itemName);
    }

    /// <summary>채널별 송장표시명(설정이 없으면 null — 송장에는 상품명이 대신 쓰인다).</summary>
    public string? GetInvoiceDisplayName(string cskuCode)
    {
        var name = _channelSkuRepository
            .GetAllByChannel(ChannelCode)
            .FirstOrDefault(c => c.CskuCode.Equals(cskuCode, StringComparison.OrdinalIgnoreCase))
            ?.InvoiceDisplayName;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public MultiProductAssigner.ProductSlot MakeSlot(string cskuCode, string productName, int? quantity) =>
        new(cskuCode, productName, GetInvoiceDisplayName(cskuCode), quantity);
}
