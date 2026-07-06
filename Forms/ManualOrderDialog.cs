using System.ComponentModel;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 수동 주문 추가 다이얼로그(비모달).
/// OFS 툴바의 현재 채널을 기반으로 출고 이력 상위 5개 CSKU를 버튼으로 보여주고,
/// 클릭하면 수량 확인 창을 거쳐 즉시 OFS 그리드에 행을 추가합니다.
/// </summary>
public class ManualOrderDialog : Form
{
    private readonly Action<OfsOrderItem> _addItem;
    private readonly OutboundRepository _outboundRepository;
    private readonly ChannelSkuRepository _channelSkuRepository;

    private string _channelCode = string.Empty;
    private string _channelName = string.Empty;

    private Label _channelLabel = new();
    private FlowLayoutPanel _quickPanel = new();

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

    /// <summary>OFS 툴바 채널 콤보 변경 시 호출하여 버튼 목록을 갱신합니다.</summary>
    public void SetChannel(string channelCode, string channelName)
    {
        _channelCode = channelCode;
        _channelName = channelName;
        _channelLabel.Text = $"채널:  {channelName}  ({channelCode})";
        RefreshQuickPanel();
    }

    private void InitializeComponent()
    {
        Text = "수동 주문 추가";
        Size = new Size(420, 380);
        MinimumSize = new Size(360, 280);
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

        var sectionLabel = new Label
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

        Controls.Add(_quickPanel);
        Controls.Add(sectionLabel);
        Controls.Add(_channelLabel);
        Controls.Add(btnPanel);
    }

    private void RefreshQuickPanel()
    {
        _quickPanel.Controls.Clear();

        var topItems = _outboundRepository.GetTopCskusByChannel(_channelCode, 5);
        int panelWidth = _quickPanel.ClientSize.Width - _quickPanel.Padding.Horizontal - 4;
        if (panelWidth < 100) panelWidth = 380;

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
                    Width = panelWidth,
                    Height = 36,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(8, 0, 0, 0),
                    Margin = new Padding(0, 3, 0, 0)
                };
                btn.Click += (s, e) => OnCskuButtonClick(csku, name);
                _quickPanel.Controls.Add(btn);
            }
        }

        var sep = new Label { Height = 12, Width = panelWidth, Dock = DockStyle.None };
        _quickPanel.Controls.Add(sep);

        var btnBlank = new Button
        {
            Text = "+ 빈 행 추가",
            Width = panelWidth,
            Height = 32,
            Margin = new Padding(0, 0, 0, 0)
        };
        btnBlank.Click += (s, e) => _addItem(new OfsOrderItem
        {
            ChannelCode = _channelCode,
            Quantity = 1,
            Status = "수동 추가"
        });
        _quickPanel.Controls.Add(btnBlank);
    }

    private void OnCskuButtonClick(string cskuCode, string productName)
    {
        using var qtyDialog = new ManualOrderQuantityDialog(cskuCode, productName);
        if (qtyDialog.ShowDialog(this) != DialogResult.OK) return;

        var invoiceDisplayName = _channelSkuRepository
            .GetAllByChannel(_channelCode)
            .FirstOrDefault(c => c.CskuCode.Equals(cskuCode, StringComparison.OrdinalIgnoreCase))
            ?.InvoiceDisplayName;

        _addItem(new OfsOrderItem
        {
            ChannelCode = _channelCode,
            MappedSku = cskuCode,
            ProductName = productName,
            Quantity = qtyDialog.Quantity,
            Status = "수동 추가",
            InvoiceDisplayName = string.IsNullOrWhiteSpace(invoiceDisplayName) ? null : invoiceDisplayName
        });
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // 패널 너비가 바뀌면 버튼 너비 재조정
        int w = _quickPanel.ClientSize.Width - _quickPanel.Padding.Horizontal - 4;
        if (w < 100) return;
        foreach (Control ctrl in _quickPanel.Controls)
        {
            if (ctrl is Button or Label && ctrl.Dock == DockStyle.None)
                ctrl.Width = w;
        }
    }
}
