using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 수동 주문 추가 / CSKU 교체 다이얼로그(비모달).
/// <para>추가 모드: OFS 툴바의 현재 채널 기반으로 출고 이력 상위 5 CSKU 버튼을 보여줍니다.
/// 버튼 클릭 → 수량 확인 창 → OFS 그리드에 새 행 추가.</para>
/// <para>교체 모드(<see cref="SetReplaceTarget"/> 호출 후): 선택된 행의 MappedSku를 클릭 한 번으로
/// 교체합니다. 수량 팝업 없이 즉시 적용되며 "빈 행 추가" 버튼은 숨겨집니다.</para>
/// </summary>
public class ManualOrderDialog : Form
{
    private readonly Action<OfsOrderItem> _addItem;
    private readonly OutboundRepository _outboundRepository;
    private readonly ChannelSkuRepository _channelSkuRepository;

    private string _channelCode = string.Empty;
    private string _channelName = string.Empty;

    // 교체 모드에서 업데이트할 대상 행. null이면 추가 모드.
    private OfsOrderItem? _replaceTarget;
    private Action? _onReplaced;

    private Label _channelLabel = new();
    private Label _modeLabel = new();      // 교체 모드 안내문
    private Label _sectionLabel = new();
    private FlowLayoutPanel _quickPanel = new();
    private Button _btnBlank = new();      // 추가 모드에서만 표시

    public ManualOrderDialog(
        Action<OfsOrderItem> addItem,
        string channelCode,
        string channelName,
        OutboundRepository outboundRepository,
        ChannelSkuRepository channelSkuRepository)
    {
        _addItem = addItem;
        _outboundRepository = outboundRepository;
        _channelSkuRepository = channelSkuRepository;
        InitializeComponent();
        SetChannel(channelCode, channelName);
    }

    /// <summary>OFS 툴바 채널 콤보 변경 시 호출 — 버튼 목록을 갱신합니다.</summary>
    public void SetChannel(string channelCode, string channelName)
    {
        _channelCode = channelCode;
        _channelName = channelName;
        _channelLabel.Text = $"채널:  {channelName}  ({channelCode})";
        RefreshQuickPanel();
    }

    /// <summary>
    /// 교체 모드를 설정합니다.
    /// <param name="item">교체 대상 행. null을 전달하면 추가 모드로 돌아갑니다.</param>
    /// <param name="onReplaced">CSKU가 교체된 뒤 그리드 갱신 등을 위해 호출할 콜백.</param>
    /// </summary>
    public void SetReplaceTarget(OfsOrderItem? item, Action? onReplaced = null)
    {
        _replaceTarget = item;
        _onReplaced = onReplaced;

        if (item != null)
        {
            var rowDesc = !string.IsNullOrWhiteSpace(item.ProductName) ? item.ProductName : "(빈 행)";
            Text = "CSKU 교체";
            _modeLabel.Text = $"▶ 선택 행: {rowDesc}  —  클릭하면 즉시 교체됩니다";
            _modeLabel.Visible = true;
            _sectionLabel.Text = "교체할 CSKU 선택  (최근 출고 이력 기준)";
            _btnBlank.Visible = false;
        }
        else
        {
            Text = "수동 주문 추가";
            _modeLabel.Visible = false;
            _sectionLabel.Text = "자주 사용한 품목  (최근 출고 이력 기준)";
            _btnBlank.Visible = true;
        }
    }

    private void InitializeComponent()
    {
        Text = "수동 주문 추가";
        Size = new Size(420, 400);
        MinimumSize = new Size(360, 300);
        StartPosition = FormStartPosition.Manual;
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
        btnPanel.Controls.Add(btnClose);
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

        var topItems = _outboundRepository.GetTopCskusByChannel(_channelCode, 5);
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

        // 빈 행 추가 버튼 (추가 모드에서만 보임)
        _quickPanel.Controls.Add(new Label { Height = 12, Width = w });
        _btnBlank = new Button
        {
            Text = "+ 빈 행 추가",
            Width = w,
            Height = 32,
            Visible = _replaceTarget == null
        };
        _btnBlank.Click += (s, e) => _addItem(new OfsOrderItem
        {
            ChannelCode = _channelCode,
            Quantity = 1,
            Status = "수동 추가"
        });
        _quickPanel.Controls.Add(_btnBlank);
    }

    private void OnCskuButtonClick(string cskuCode, string productName)
    {
        var invoiceDisplayName = _channelSkuRepository
            .GetAllByChannel(_channelCode)
            .FirstOrDefault(c => c.CskuCode.Equals(cskuCode, StringComparison.OrdinalIgnoreCase))
            ?.InvoiceDisplayName;
        var displayName = string.IsNullOrWhiteSpace(invoiceDisplayName) ? null : invoiceDisplayName;

        if (_replaceTarget != null)
        {
            // 교체 모드: 수량 팝업 없이 즉시 적용
            _replaceTarget.MappedSku = cskuCode;
            _replaceTarget.InvoiceDisplayName = displayName;
            _replaceTarget.Status = "수동 매핑";
            if (string.IsNullOrWhiteSpace(_replaceTarget.ProductName))
                _replaceTarget.ProductName = productName;
            _onReplaced?.Invoke();
            return;
        }

        // 추가 모드: 수량 확인 팝업
        using var qtyDialog = new ManualOrderQuantityDialog(cskuCode, productName);
        if (qtyDialog.ShowDialog(this) != DialogResult.OK) return;

        _addItem(new OfsOrderItem
        {
            ChannelCode = _channelCode,
            MappedSku = cskuCode,
            ProductName = productName,
            Quantity = qtyDialog.Quantity,
            Status = "수동 추가",
            InvoiceDisplayName = displayName
        });
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
