using System.ComponentModel;
using System.Text.Json;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 택배사별 출력 양식(엑셀 헤더 → OfsOrderItem 속성) 매핑을 만들고 수정하는 창입니다.
/// 채널 설정과는 독립적으로 관리되며, OFS의 [택배사 양식으로 내보내기]가 이 설정을 사용합니다.
/// </summary>
public class CourierConfigForm : Form
{
    private readonly CourierRepository _courierRepository = new();
    private List<CourierMaster> _couriers = new();
    private string? _selectedCourierName;

    private ListBox _courierListBox = new();
    private TextBox _txtCourierName = new();
    private DataGridView _mappingGrid = new();
    private Label _legendLabel = new();
    private NumericUpDown _numTrackingHeaderRow = new();
    private ComboBox _cmbTrackingRecipientHeader = new();
    private ComboBox _cmbTrackingNoHeader = new();

    private static readonly (string Property, string Label)[] PropertyOptions =
    [
        ("OrderNo", "주문번호"),
        ("ProductName", "상품명"),
        ("OptionName", "옵션명"),
        ("Quantity", "수량"),
        ("Recipient", "수취인"),
        ("Phone", "연락처"),
        ("Address", "주소"),
        ("DeliveryMessage", "배송메세지"),
        ("InvoiceLabel", "송장표시 품목명(CSKU 송장표시명+수량 자동조합, 설정 없으면 빈값)"),
        ("MappedSku", "매핑된 SKU"),
        ("Status", "처리 상태"),
        ("TrackingNo", "운송장번호"),
        ("ChannelCode", "채널코드"),
    ];

    public CourierConfigForm()
    {
        InitializeComponent();
        LoadCouriers();
    }

    private void InitializeComponent()
    {
        Text = "택배사 양식 관리";
        Size = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // 좌측: 택배사 목록
        var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _courierListBox = new ListBox { Dock = DockStyle.Fill, DisplayMember = "CourierName" };
        _courierListBox.SelectedIndexChanged += OnCourierSelected;

        var leftButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        var btnAddCourier = new Button { Text = "추가", Width = 70 };
        var btnDeleteCourier = new Button { Text = "삭제", Width = 70 };
        btnAddCourier.Click += OnAddCourierClick;
        btnDeleteCourier.Click += OnDeleteCourierClick;
        leftButtonPanel.Controls.Add(btnAddCourier);
        leftButtonPanel.Controls.Add(btnDeleteCourier);

        leftPanel.Controls.Add(_courierListBox, 0, 0);
        leftPanel.Controls.Add(leftButtonPanel, 0, 1);

        // 우측: 선택한 택배사 편집
        var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, Padding = new Padding(10) };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var namePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        namePanel.Controls.Add(new Label { Text = "택배사 이름:", AutoSize = true, Padding = new Padding(0, 6, 5, 0) });
        _txtCourierName = new TextBox { Width = 200 };
        namePanel.Controls.Add(_txtCourierName);

        var samplePanel = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnLoadSample = new Button { Text = "샘플 양식 불러오기", Width = 150 };
        btnLoadSample.Click += OnLoadSampleClick;
        samplePanel.Controls.Add(btnLoadSample);
        samplePanel.Controls.Add(new Label { Text = "엑셀 1행의 헤더를 읽어 아래 '엑셀 헤더' 후보로 채웁니다.", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });

        _mappingGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
        };

        var headerColumn = new DataGridViewComboBoxColumn
        {
            Name = "Header",
            HeaderText = "엑셀 헤더",
            DataPropertyName = "Header",
            Width = 250,
            FlatStyle = FlatStyle.Flat,
        };
        var propertyColumn = new DataGridViewComboBoxColumn
        {
            Name = "PropertyName",
            HeaderText = "매핑할 데이터",
            DataPropertyName = "PropertyName",
            Width = 250,
            FlatStyle = FlatStyle.Flat,
        };
        propertyColumn.Items.AddRange(PropertyOptions.Select(p => p.Property).Cast<object>().ToArray());

        _mappingGrid.Columns.Add(headerColumn);
        _mappingGrid.Columns.Add(propertyColumn);
        // 드롭다운 후보 외에 직접 입력도 허용한다.
        _mappingGrid.EditingControlShowing += (s, e) =>
        {
            if (_mappingGrid.CurrentCell?.OwningColumn is DataGridViewComboBoxColumn && e.Control is ComboBox comboBox)
            {
                comboBox.DropDownStyle = ComboBoxStyle.DropDown;
            }
        };

        _legendLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "매핑할 데이터 속성명: " + string.Join("  ·  ", PropertyOptions.Select(p => $"{p.Property}({p.Label})")),
            AutoSize = false,
        };

        // 운송장 결과 가져오기(입수) 양식 — 출력 양식과는 별개의 파일이라 따로 설정한다.
        // 발주/출고 이력 관리창에서 "운송장번호 불러오기" 시 이 설정으로 헤더 시작행과 수령인/
        // 운송장번호 열을 찾는다.
        var trackingImportGroup = new GroupBox { Text = "운송장 결과 가져오기 양식", Dock = DockStyle.Fill };
        var trackingImportPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5) };

        var btnLoadTrackingSample = new Button { Text = "샘플 양식 불러오기", Width = 130 };
        btnLoadTrackingSample.Click += OnLoadTrackingImportSampleClick;
        trackingImportPanel.Controls.Add(btnLoadTrackingSample);

        trackingImportPanel.Controls.Add(new Label { Text = "헤더 시작행:", AutoSize = true, Padding = new Padding(10, 6, 2, 0) });
        _numTrackingHeaderRow = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 1, Width = 50 };
        trackingImportPanel.Controls.Add(_numTrackingHeaderRow);

        trackingImportPanel.Controls.Add(new Label { Text = "수령인 헤더:", AutoSize = true, Padding = new Padding(10, 6, 2, 0) });
        _cmbTrackingRecipientHeader = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDown };
        trackingImportPanel.Controls.Add(_cmbTrackingRecipientHeader);

        trackingImportPanel.Controls.Add(new Label { Text = "운송장번호 헤더:", AutoSize = true, Padding = new Padding(10, 6, 2, 0) });
        _cmbTrackingNoHeader = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDown };
        trackingImportPanel.Controls.Add(_cmbTrackingNoHeader);

        trackingImportGroup.Controls.Add(trackingImportPanel);

        var saveButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnSave = new Button { Text = "저장", Width = 90 };
        btnSave.Click += OnSaveClick;
        saveButtonPanel.Controls.Add(btnSave);

        rightPanel.Controls.Add(namePanel, 0, 0);
        rightPanel.Controls.Add(samplePanel, 0, 1);
        rightPanel.Controls.Add(_mappingGrid, 0, 2);
        rightPanel.Controls.Add(_legendLabel, 0, 3);
        rightPanel.Controls.Add(trackingImportGroup, 0, 4);
        rightPanel.Controls.Add(saveButtonPanel, 0, 5);

        mainLayout.Controls.Add(leftPanel, 0, 0);
        mainLayout.Controls.Add(rightPanel, 1, 0);
        Controls.Add(mainLayout);
    }

    private void LoadCouriers()
    {
        _couriers = _courierRepository.GetAll();
        _courierListBox.DataSource = null;
        _courierListBox.DataSource = _couriers;
    }

    private void OnCourierSelected(object? sender, EventArgs e)
    {
        if (_courierListBox.SelectedItem is not CourierMaster courier)
        {
            return;
        }

        _selectedCourierName = courier.CourierName;
        _txtCourierName.Text = courier.CourierName;
        _txtCourierName.ReadOnly = true;

        var rows = new List<HeaderMappingRow>();
        try
        {
            var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(courier.HeaderMappingJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (mapping != null)
            {
                rows.AddRange(mapping.Select(kv => new HeaderMappingRow { Header = kv.Key, PropertyName = kv.Value }));
            }
        }
        catch (JsonException)
        {
            // 기존 데이터가 비어있거나 손상된 경우 빈 그리드로 시작
        }

        EnsureComboItemsInclude("Header", rows.Select(r => r.Header));
        EnsureComboItemsInclude("PropertyName", rows.Select(r => r.PropertyName));
        _mappingGrid.DataSource = new BindingList<HeaderMappingRow>(rows);

        _numTrackingHeaderRow.Value = Math.Clamp(courier.TrackingImportHeaderRow, (int)_numTrackingHeaderRow.Minimum, (int)_numTrackingHeaderRow.Maximum);
        _cmbTrackingRecipientHeader.Text = courier.TrackingImportRecipientHeader;
        _cmbTrackingNoHeader.Text = courier.TrackingImportTrackingNoHeader;
    }

    /// <summary>
    /// DataGridViewComboBoxColumn은 셀 값이 Items 목록에 없으면 표시 시 예외를 던진다.
    /// 새 창을 열 때마다 Items가 비어 시작하므로, 저장된 값을 불러올 때는 항상
    /// 그 값들을 Items에 먼저 채워둬야 한다(자유 입력으로 저장된 값 포함).
    /// </summary>
    private void EnsureComboItemsInclude(string columnName, IEnumerable<string> values)
    {
        if (_mappingGrid.Columns[columnName] is not DataGridViewComboBoxColumn column) return;

        var existing = new HashSet<string>(column.Items.Cast<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value) || !existing.Add(value)) continue;
            column.Items.Add(value);
        }
    }

    private void OnAddCourierClick(object? sender, EventArgs e)
    {
        _courierListBox.ClearSelected();
        _selectedCourierName = null;
        _txtCourierName.Text = string.Empty;
        _txtCourierName.ReadOnly = false;
        _mappingGrid.DataSource = new BindingList<HeaderMappingRow>();
        _numTrackingHeaderRow.Value = 1;
        _cmbTrackingRecipientHeader.Text = string.Empty;
        _cmbTrackingNoHeader.Text = string.Empty;
        _txtCourierName.Focus();
    }

    private void OnLoadTrackingImportSampleClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*", Title = "운송장 결과 샘플 파일을 선택하세요" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var package = ExcelFileOpener.OpenWithPasswordPrompt(ofd.FileName, this);
            if (package == null) return;

            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            var headerRow = (int)_numTrackingHeaderRow.Value;
            if (worksheet?.Dimension == null || headerRow > worksheet.Dimension.End.Row)
            {
                MessageBox.Show("엑셀 파일에서 헤더를 찾을 수 없습니다(헤더 시작행을 확인하세요).", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var headers = new List<string>();
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[headerRow, col].Value?.ToString();
                if (!string.IsNullOrWhiteSpace(header)) headers.Add(header);
            }

            _cmbTrackingRecipientHeader.Items.Clear();
            _cmbTrackingRecipientHeader.Items.AddRange(headers.Cast<object>().ToArray());
            _cmbTrackingNoHeader.Items.Clear();
            _cmbTrackingNoHeader.Items.AddRange(headers.Cast<object>().ToArray());

            MessageBox.Show($"{headers.Count}개의 헤더를 읽었습니다. '수령인 헤더'/'운송장번호 헤더'에서 선택하세요.", "샘플 불러오기 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnDeleteCourierClick(object? sender, EventArgs e)
    {
        if (_courierListBox.SelectedItem is not CourierMaster courier) return;

        var result = MessageBox.Show($"택배사 양식 '{courier.CourierName}'을(를) 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        _courierRepository.Delete(courier.CourierName);
        LoadCouriers();
        OnAddCourierClick(null, EventArgs.Empty);
    }

    private void OnLoadSampleClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog { Filter = "Excel Files (*.xlsx)|*.xlsx|All files (*.*)|*.*", Title = "샘플 양식 파일을 선택하세요" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var package = ExcelFileOpener.OpenWithPasswordPrompt(ofd.FileName, this);
            if (package == null) return;

            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet?.Dimension == null)
            {
                MessageBox.Show("엑셀 파일에서 헤더를 찾을 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var headers = new List<string>();
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[1, col].Value?.ToString();
                if (!string.IsNullOrWhiteSpace(header)) headers.Add(header);
            }

            if (_mappingGrid.Columns["Header"] is DataGridViewComboBoxColumn headerColumn)
            {
                headerColumn.Items.Clear();
                headerColumn.Items.AddRange(headers.Cast<object>().ToArray());
            }

            // 그리드에 아직 없는 헤더는 매핑할 데이터를 빈 채로 새 행에 추가해 바로 채울 수 있게 한다.
            var rows = (_mappingGrid.DataSource as BindingList<HeaderMappingRow>) ?? new BindingList<HeaderMappingRow>();
            var existingHeaders = new HashSet<string>(rows.Select(r => r.Header), StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers.Where(h => !existingHeaders.Contains(h)))
            {
                rows.Add(new HeaderMappingRow { Header = header, PropertyName = string.Empty });
            }
            _mappingGrid.DataSource = rows;

            MessageBox.Show($"{headers.Count}개의 헤더를 읽었습니다. '엑셀 헤더' 열에서 선택할 수 있습니다.", "샘플 불러오기 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        var courierName = _txtCourierName.Text.Trim();
        if (string.IsNullOrWhiteSpace(courierName))
        {
            MessageBox.Show("택배사 이름을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 매핑할 데이터를 지정하지 않은 헤더(예: 샘플의 c,e열)도 그대로 저장해야 한다 — 택배사
        // 프로그램에 그 파일을 그대로 올리려면 샘플에 있던 헤더가 출력 파일에도 전부 있어야 하기
        // 때문이다(매핑이 없는 헤더는 빈 칸으로 출력됨, CourierExporter 참고).
        var allRows = (_mappingGrid.DataSource as BindingList<HeaderMappingRow>)?
            .Where(r => !string.IsNullOrWhiteSpace(r.Header))
            .ToList() ?? [];

        if (allRows.Count == 0)
        {
            MessageBox.Show("최소 한 개 이상의 엑셀 헤더가 필요합니다(샘플 양식을 불러오거나 직접 입력하세요).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!allRows.Any(r => !string.IsNullOrWhiteSpace(r.PropertyName)))
        {
            MessageBox.Show("최소 한 개 이상의 '엑셀 헤더 → 매핑할 데이터'를 지정하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var mapping = allRows.ToDictionary(r => r.Header, r => r.PropertyName);
        var headerMappingJson = JsonSerializer.Serialize(mapping);

        _courierRepository.Upsert(new CourierMaster
        {
            CourierName = courierName,
            HeaderMappingJson = headerMappingJson,
            TrackingImportHeaderRow = (int)_numTrackingHeaderRow.Value,
            TrackingImportRecipientHeader = _cmbTrackingRecipientHeader.Text.Trim(),
            TrackingImportTrackingNoHeader = _cmbTrackingNoHeader.Text.Trim(),
        });

        MessageBox.Show("택배사 양식이 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadCouriers();
    }

    private class HeaderMappingRow
    {
        public string Header { get; set; } = string.Empty;
        public string PropertyName { get; set; } = string.Empty;
    }
}
