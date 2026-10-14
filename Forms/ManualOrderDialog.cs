using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 수동 주문 추가 / CSKU 교체 다이얼로그(비모달).
/// <para>추가 모드: OFS 툴바의 현재 채널 기반으로 출고 이력 상위 5 CSKU 버튼을 보여줍니다.
/// 버튼 클릭 → 수량 확인 창 → OFS 그리드에 새 행 추가.</para>
/// <para>교체 모드(<see cref="SetReplaceTargets"/> 호출 후): 선택된 행(드래그로 여러 행 가능)의 MappedSku를
/// 클릭 한 번으로 교체합니다. 수량 팝업 없이 즉시 적용되며 "빈 행 추가" 버튼은 숨겨집니다.</para>
/// <para>추가 모드에서는 각 버튼의 팝업에서 "추가할 주문 수"를 받아 그만큼 한 번에 추가합니다 —
/// 엑셀에서 수취인 정보 여러 줄을 복사해 OFS 그리드에 붙여넣을 빈 행을 미리 만들어 두기 위함(그리드
/// 붙여넣기는 기존 행 범위 안에서만 채운다).</para>
/// </summary>
public class ManualOrderDialog : Form
{
    private readonly Action<IReadOnlyList<OfsOrderItem>> _addItems;
    private readonly Action<IReadOnlyList<OfsOrderItem>, IReadOnlyList<MultiProductAssigner.ProductSlot>> _assignProducts;
    private readonly OutboundRepository _outboundRepository;
    private readonly ChannelSkuRepository _channelSkuRepository;
    private readonly ChannelConfigService _channelConfigService;

    private string _channelCode = string.Empty;
    private string _channelName = string.Empty;
    private Dictionary<StdField, string> _fixedValues = new();
    private ManualCskuResolver _resolver = null!;
    private List<(string CskuCode, string ProductName, int Count)> _topItems = [];

    // 교체 모드에서 업데이트할 대상 행들. 비어 있으면 추가 모드.
    private IReadOnlyList<OfsOrderItem> _replaceTargets = [];

    private Label _channelLabel = new();
    private Label _modeLabel = new();      // 교체 모드 안내문
    private Label _sectionLabel = new();
    private FlowLayoutPanel _quickPanel = new();
    private Panel _blankRow = new();       // 빈 행 추가(줄 수 입력칸 + 버튼), 추가 모드에서만 표시
    private NumericUpDown _blankCountSpinner = new();

    /// <param name="addItems">새 줄들을 OFS 그리드에 추가한다.</param>
    /// <param name="assignProducts">기존 줄들에 CSKU(여러 품목이면 줄 추가 + 합포장)를 배정한다.</param>
    public ManualOrderDialog(
        Action<IReadOnlyList<OfsOrderItem>> addItems,
        Action<IReadOnlyList<OfsOrderItem>, IReadOnlyList<MultiProductAssigner.ProductSlot>> assignProducts,
        string channelCode,
        string channelName,
        OutboundRepository outboundRepository,
        ChannelSkuRepository channelSkuRepository,
        ChannelConfigService channelConfigService)
    {
        _addItems = addItems;
        _assignProducts = assignProducts;
        _outboundRepository = outboundRepository;
        _channelSkuRepository = channelSkuRepository;
        _channelConfigService = channelConfigService;
        InitializeComponent();
        SetChannel(channelCode, channelName);
    }

    /// <summary>OFS 툴바 채널 콤보 변경 시 호출 — 버튼 목록과 고정값을 갱신합니다.</summary>
    public void SetChannel(string channelCode, string channelName)
    {
        _channelCode = channelCode;
        _channelName = channelName;
        _channelLabel.Text = $"채널:  {channelName}  ({channelCode})";
        _fixedValues = LoadFixedValues(channelCode);
        _resolver = new ManualCskuResolver(channelCode, channelName, _channelSkuRepository);
        RefreshQuickPanel();
    }

    private Dictionary<StdField, string> LoadFixedValues(string channelCode)
    {
        var result = new Dictionary<StdField, string>();
        if (string.IsNullOrEmpty(channelCode)) return result;
        var config = _channelConfigService.Load().FirstOrDefault(c => c.ChannelCode == channelCode);
        if (config == null) return result;
        foreach (var (field, mapping) in config.OrderFieldMappings)
        {
            if (!string.IsNullOrEmpty(mapping.FixedValue))
                result[field] = mapping.FixedValue;
        }
        return result;
    }

    private void ApplyFixedValues(OfsOrderItem item)
    {
        if (_fixedValues.TryGetValue(StdField.Recipient, out var r)) item.Recipient = r;
        if (_fixedValues.TryGetValue(StdField.Phone, out var p)) item.Phone = p;
        if (_fixedValues.TryGetValue(StdField.Address, out var a)) item.Address = a;
        // 주소를 두 열(주소 + 주소상세)로 고정해둔 채널도 발주서 로드와 같은 규칙으로 합쳐준다.
        if (_fixedValues.TryGetValue(StdField.AddressDetail, out var ad))
            item.Address = OrderLoader.CombineAddress(item.Address, ad);
        if (_fixedValues.TryGetValue(StdField.DeliveryMessage, out var d)) item.DeliveryMessage = d;
    }

    /// <summary>
    /// 교체 모드를 설정합니다. 빈 목록(또는 null)을 전달하면 추가 모드로 돌아갑니다.
    /// </summary>
    public void SetReplaceTargets(IReadOnlyList<OfsOrderItem>? items)
    {
        _replaceTargets = items ?? [];

        if (_replaceTargets.Count > 0)
        {
            var first = _replaceTargets[0];
            var rowDesc = _replaceTargets.Count > 1
                ? $"선택 행 {_replaceTargets.Count}건"
                : $"선택 행: {(!string.IsNullOrWhiteSpace(first.ProductName) ? first.ProductName : "(빈 행)")}";
            Text = "CSKU 교체";
            _modeLabel.Text = $"▶ {rowDesc}  —  클릭하면 즉시 교체됩니다";
            _modeLabel.Visible = true;
            _sectionLabel.Text = "교체할 CSKU 선택  (최근 출고 이력 기준)";
            _blankRow.Visible = false;
        }
        else
        {
            Text = "수동 주문 추가";
            _modeLabel.Visible = false;
            _sectionLabel.Text = "자주 사용한 품목  (최근 출고 이력 기준)";
            _blankRow.Visible = true;
        }
    }

    private void InitializeComponent()
    {
        Text = "수동 주문 추가";
        Size = new Size(420, 430);
        MinimumSize = new Size(360, 300);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        _channelLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold)
        };

        _modeLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            ForeColor = Color.DarkSlateBlue,
            Font = new Font(Font.FontFamily, 8.5f, FontStyle.Italic),
            Visible = false
        };

        _sectionLabel = new Label
        {
            Text = "자주 사용한 품목  (최근 출고 이력 기준)",
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            ForeColor = SystemColors.GrayText,
            Font = new Font(Font.FontFamily, 8f)
        };

        _quickPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8, 6, 8, 6)
        };

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 42,
            Padding = new Padding(8)
        };
        var btnClose = new Button { Text = "닫기", Width = 80 };
        btnClose.Click += (s, e) => Close();
        var btnSkuSearch = new Button { Text = "SKU 검색", Width = 90 };
        btnSkuSearch.Click += OnSkuSearchClick;
        btnPanel.Controls.Add(btnClose);
        btnPanel.Controls.Add(btnSkuSearch);
        CancelButton = btnClose;

        // Controls are added in reverse order (DockStyle.Top stacks from bottom up)
        Controls.Add(_quickPanel);
        Controls.Add(_sectionLabel);
        Controls.Add(_modeLabel);
        Controls.Add(_channelLabel);
        Controls.Add(btnPanel);
    }

    private void RefreshQuickPanel()
    {
        _quickPanel.Controls.Clear();

        _topItems = _outboundRepository.GetTopCskusByChannel(_channelCode, 10);
        var topItems = _topItems.Take(5).ToList();
        int w = ButtonWidth();

        if (topItems.Count == 0)
        {
            _quickPanel.Controls.Add(new Label
            {
                Text = "이 채널의 출고 이력이 없습니다.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(2, 10, 0, 0)
            });
        }
        else
        {
            foreach (var (mskuCode, productName, count) in topItems)
            {
                var csku = mskuCode;
                var name = productName;
                var btn = new Button
                {
                    Text = $"{csku}   {name}   ({count}회)",
                    Width = w,
                    Height = 36,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(8, 0, 0, 0),
                    Margin = new Padding(0, 3, 0, 0)
                };
                btn.Click += (s, e) => OnCskuButtonClick(csku, name);
                _quickPanel.Controls.Add(btn);
            }
        }

        // 샘플발송이력관리_개발기획서.md §4.1(a): 출고 이력이 없는 신규 품목도 마스터SKU만
        // 검색해서 CSKU 관리창을 열지 않고 바로 접수할 수 있게 한다.
        _quickPanel.Controls.Add(new Label { Height = 8, Width = w });
        var btnFindByMasterSku = new Button
        {
            Text = "[마스터SKU에서 찾기]",
            Width = w,
            Height = 32,
        };
        btnFindByMasterSku.Click += OnFindByMasterSkuClick;
        _quickPanel.Controls.Add(btnFindByMasterSku);

        // 주문 1건에 품목 여러 개(최대 4)를 함께 보낼 때 — 추가 모드는 새 주문을, 교체 모드는
        // 선택한 줄들을 대상으로 한다.
        _quickPanel.Controls.Add(new Label { Height = 4, Width = w });
        var btnMulti = new Button
        {
            Text = $"[여러 품목 함께 보내기 (최대 {MultiProductAssigner.MaxProductsPerShipment}개)]",
            Width = w,
            Height = 32,
        };
        btnMulti.Click += OnMultiProductClick;
        _quickPanel.Controls.Add(btnMulti);

        // 빈 행 추가 (추가 모드에서만 보임) — 줄 수 입력칸을 버튼 바로 옆에 둬 팝업 단계 없이
        // 바로 추가한다(엑셀에서 여러 줄을 붙여넣을 빈 행 만들기용).
        _quickPanel.Controls.Add(new Label { Height = 12, Width = w });
        _blankCountSpinner = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 1, Width = 60, Margin = new Padding(0, 5, 4, 0) };
        _blankCountSpinner.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            OnBlankRowsClick(s, e);
        };
        var countBox = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 96,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        countBox.Controls.Add(_blankCountSpinner);
        countBox.Controls.Add(new Label { Text = "줄", AutoSize = true, Margin = new Padding(0, 8, 0, 0) });
        var btnBlank = new Button { Text = "+ 빈 행 추가", Dock = DockStyle.Fill };
        btnBlank.Click += OnBlankRowsClick;
        _blankRow = new Panel
        {
            Width = w,
            Height = 32,
            Margin = Padding.Empty,
            Visible = _replaceTargets.Count == 0
        };
        _blankRow.Controls.Add(btnBlank);
        _blankRow.Controls.Add(countBox);
        _quickPanel.Controls.Add(_blankRow);
    }

    /// <summary>빈 행 추가 — 옆 입력칸의 줄 수만큼 한 번에 추가한다(엑셀 붙여넣기용).</summary>
    private void OnBlankRowsClick(object? sender, EventArgs e)
    {
        var count = (int)_blankCountSpinner.Value;
        _addItems(Enumerable.Range(0, count).Select(_ => NewManualItem()).ToList());
    }

    /// <summary>채널 고정값·샘플 구분 프리셋이 적용된 빈 수동 주문 줄.</summary>
    private OfsOrderItem NewManualItem()
    {
        var item = new OfsOrderItem { ChannelCode = _channelCode, Quantity = 1, Status = "수동 추가" };
        ApplyFixedValues(item);
        PresetLineKindIfSampleChannel(item);
        return item;
    }

    /// <summary>출고 이력이 없는 신규 품목도 마스터SKU만 검색해 바로 접수한다(샘플발송이력관리 §4.1(a)).</summary>
    private void OnFindByMasterSkuClick(object? sender, EventArgs e)
    {
        if (_resolver.PickByMasterSku(this) is { } picked)
            OnCskuButtonClick(picked.CskuCode, picked.ProductName);
    }

    /// <summary>선택 후 처리는 빠른 버튼과 동일(추가 모드: 수량 확인 팝업, 교체 모드: 즉시 교체).</summary>
    private void OnSkuSearchClick(object? sender, EventArgs e)
    {
        if (_resolver.PickBySearch(this) is { } picked)
            OnCskuButtonClick(picked.CskuCode, picked.ProductName);
    }

    private void OnCskuButtonClick(string cskuCode, string productName)
    {
        if (_replaceTargets.Count > 0)
        {
            // 교체 모드: 수량 팝업 없이 즉시 적용(수량은 각 줄의 기존 값 유지)
            _assignProducts(_replaceTargets, [_resolver.MakeSlot(cskuCode, productName, quantity: null)]);
            return;
        }

        // 추가 모드: 수량 확인 팝업
        using var qtyDialog = new ManualOrderQuantityDialog(cskuCode, productName);
        if (FormManager.ShowDialogSafe(qtyDialog, this) != DialogResult.OK) return;

        var slot = _resolver.MakeSlot(cskuCode, productName, qtyDialog.Quantity);
        var newItems = Enumerable.Range(0, qtyDialog.OrderCount).Select(_ => NewManualItem()).ToList();
        foreach (var item in newItems) MultiProductAssigner.Apply(item, [slot], "수동 추가");
        _addItems(newItems);
    }

    private void OnMultiProductClick(object? sender, EventArgs e)
    {
        var replacing = _replaceTargets.Count > 0;
        var description = replacing ? $"선택한 주문 {_replaceTargets.Count}건" : "새 주문";
        using var dialog = new MultiProductAssignDialog(
            _resolver, _topItems.Select(t => (t.CskuCode, t.ProductName)), description, askOrderCount: !replacing);
        if (FormManager.ShowDialogSafe(dialog, this) != DialogResult.OK) return;

        if (replacing)
        {
            _assignProducts(_replaceTargets, dialog.Slots);
            return;
        }

        var newItems = new List<OfsOrderItem>();
        for (int i = 0; i < dialog.OrderCount; i++)
        {
            var item = NewManualItem();
            newItems.Add(item);
            newItems.AddRange(MultiProductAssigner.Apply(item, dialog.Slots, "수동 추가"));
        }
        _addItems(newItems);
    }

    /// <summary>
    /// 현재 채널이 샘플등(§2 D3)이면 신규 행의 구분을 "샘플"로 기본 프리셋한다(§4.1(a)).
    /// 사용자는 OFS 그리드의 "구분" 열에서 언제든 CS/기타로 바꾸거나 해제할 수 있다.
    /// </summary>
    private void PresetLineKindIfSampleChannel(OfsOrderItem item)
    {
        if (_channelCode == "SAMPLE") item.LineKind = LineKinds.Sample;
    }

    private int ButtonWidth()
    {
        int w = _quickPanel.ClientSize.Width - _quickPanel.Padding.Horizontal - 4;
        return w < 100 ? 380 : w;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int w = ButtonWidth();
        foreach (Control ctrl in _quickPanel.Controls)
        {
            if (ctrl.Dock == DockStyle.None)
                ctrl.Width = w;
        }
    }
}
