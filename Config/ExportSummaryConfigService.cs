using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Config;

/// <summary>
/// export_summary_config.json을 읽고 쓴다. 파일이 없으면 기획서 내용을 바탕으로 한 기본값을 만들어
/// 저장해두므로, 처음 실행해도 바로 쓸 수 있다. 이후에는 이 파일을 코드 재빌드 없이 직접 고쳐서
/// 헤더 문구 수정/신규 마켓 추가에 대응한다.
/// </summary>
public class ExportSummaryConfigService
{
    private readonly string _filePath;

    public ExportSummaryConfigService(string? filePath = null)
    {
        _filePath = filePath ?? PathProvider.ExportSummaryConfigFilePath;
    }

    public ExportSummaryConfig Load()
    {
        if (!File.Exists(_filePath))
        {
            var defaultConfig = CreateDefault();
            Save(defaultConfig);
            return defaultConfig;
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<ExportSummaryConfig>(json) ?? CreateDefault();
    }

    public void Save(ExportSummaryConfig config)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// 기획서(export_summary_report_기획.md) 4-2/4-4/5/6절 내용을 옮긴 기본값. 쇼피 SG/MY/PH는
    /// 원문 헤더가 영문이라 그대로 옮겼지만, TH/TW/VN/BR은 기획서 원문 자체가 인코딩 손상으로
    /// 읽을 수 없었던 상태라("Notion Korean text corruption"과 동일한 유형의 손상) 실제 헤더 문구를
    /// 확인할 수 없어 자리표시자(TODO...)로 남겨두었다. 각 마켓의 실제 판매 파일을 열어 헤더 행의
    /// 정확한 문구로 이 값을 바꿔야 해당 마켓 파싱이 동작한다.
    /// </summary>
    private static ExportSummaryConfig CreateDefault()
    {
        var config = new ExportSummaryConfig
        {
            Markets = new List<ExportSummaryMarket>
            {
                new() { MarketCode = "SG", MarketName = "쇼피-싱가포르", Currency = "SGD" },
                new() { MarketCode = "MY", MarketName = "쇼피-말레이시아", Currency = "MYR" },
                new() { MarketCode = "PH", MarketName = "쇼피-필리핀", Currency = "PHP" },
                new() { MarketCode = "TH", MarketName = "쇼피-태국", Currency = "THB" },
                new() { MarketCode = "TW", MarketName = "쇼피-대만", Currency = "TWD" },
                new() { MarketCode = "VN", MarketName = "쇼피-베트남", Currency = "VND" },
                new() { MarketCode = "BR", MarketName = "쇼피-브라질", Currency = "BRL" },
                new() { MarketCode = "MX", MarketName = "쇼피-멕시코", Currency = "MXN" },
                new() { MarketCode = "US", MarketName = "아마존-미국", Currency = "USD" },
                new() { MarketCode = "JP", MarketName = "아마존-일본", Currency = "JPY" },
                new() { MarketCode = "WISH", MarketName = "위시몰", Currency = "USD" },
                new() { MarketCode = "ZZ", MarketName = "직거래-미국", Currency = "USD" },
                new() { MarketCode = "OWN_US", MarketName = "자사몰-미국", Currency = "USD" },
                // USD 신고건 중 마켓 미분리분(아마존US/위시몰/직거래 공통) — 신고 합계만 표시.
                new() { MarketCode = "USD_UNMATCHED", MarketName = "USD(미분리)", Currency = "USD" },
            },
            DeclarationTrack = new DeclarationTrackConfig
            {
                HeaderRow = 1,
                DateColumn = "신고일자",
                AmountColumn = "결제금액",
                CurrencyColumn = "통화코드",
                CurrencyToMarket = new Dictionary<string, string>
                {
                    ["SGD"] = "SG",
                    ["MYR"] = "MY",
                    ["PHP"] = "PH",
                    ["THB"] = "TH",
                    ["TWD"] = "TW",
                    ["VND"] = "VN",
                    ["BRL"] = "BR",
                    ["MXN"] = "MX",
                    ["JPY"] = "JP",
                    // USD 신고 = 아마존 미국 FBA 입고분(인천공항세관). 쇼피 신고는 모두 현지통화, 쇼피 USD는 페이오니어 정산뿐
                    // (2026-10-12 확인: FBA-20260914-01 인보이스 3,855.72 ↔ 9/21 USD 신고 3,855.9).
                    ["USD"] = "US",
                },
            },
            SalesMarkets = new List<SalesMarketMapping>
            {
                // 쇼피 기준(2026-10-12 확정): 구매확정(Order.completed) 내역, 날짜=주문완료시간,
                // 판매액=소비자가 모든 할인(판매자/쇼피 쿠폰·코인 등)을 받고 실제 낸 상품금액(배송비 제외).
                // 이 금액은 주문 단위라 주문의 모든 줄에 반복되므로 주문번호당 한 번만 센다.
                // 쇼피가 직접 주는 값이 있으면 그 값을 쓴다(환불·코인 반영이 정확). 브라질만 직접 값이 없어 계산.
                // Recon = 수출신고 대사용(출고일=신고일, 신고액=(할인가×수량−판매자쿠폰)×DeclaredRate). 출고시각은 현지시간이라
                // 한국시간으로 맞추는 시차를 더한다. DeclaredRate는 3분기 실데이터 추정치(대사 때 매번 다시 추정).
                new() { MarketCode = "SG", HeaderRow = 1, DateColumn = "Order Complete Time", AmountColumn = "Grand Total",
                    OrderIdColumn = "Order ID", SubtractColumns = new() { "Buyer Paid Shipping Fee" },
                    Recon = new() { ShipDateColumn = "Ship Time", ShipDateOffsetHours = 1, UnitPriceColumn = "Deal Price", QuantityColumn = "Quantity",
                        SellerVoucherColumn = "Discount Voucher Amount Sponsored by Seller", SkuColumn = "SKU Reference No.", DeclaredRate = 0.759m },
                    FileNamePatterns = new() { "SG_", "_SG_", "_SG.", "completed.sg", "singapore", "^Order.completed.2" } },
                new() { MarketCode = "MY", HeaderRow = 1, DateColumn = "Order Complete Time", AmountColumn = "Grand Total",
                    OrderIdColumn = "Order ID", SubtractColumns = new() { "Buyer Paid Shipping Fee" },
                    Recon = new() { ShipDateColumn = "Ship Time", ShipDateOffsetHours = 1, UnitPriceColumn = "Deal Price", QuantityColumn = "Quantity",
                        SellerVoucherColumn = "Discount Voucher Amount Sponsored by Seller", SkuColumn = "SKU Reference No.", DeclaredRate = 0.691m },
                    FileNamePatterns = new() { "MY_", "_MY_", "_MY.", "completed.my", "malaysia", "^myOrder." } },
                new() { MarketCode = "PH", HeaderRow = 1, DateColumn = "Order Complete Time", AmountColumn = "Products' Price Paid by Buyer (PHP)",
                    OrderIdColumn = "Order ID",
                    Recon = new() { ShipDateColumn = "Ship Time", ShipDateOffsetHours = 1, UnitPriceColumn = "Deal Price", QuantityColumn = "Quantity",
                        SellerVoucherColumn = "Seller Voucher(PHP)", SkuColumn = "SKU Reference No.", DeclaredRate = 0.778m },
                    FileNamePatterns = new() { "PH_", "_PH_", "_PH.", "completed.ph", "philippines", "^phOrder." } },
                new() { MarketCode = "TH", HeaderRow = 1, DateColumn = "เวลาที่ทำการสั่งซื้อสำเร็จ", AmountColumn = "ราคาสินค้าที่ชำระโดยผู้ซื้อ (THB)",
                    OrderIdColumn = "หมายเลขคำสั่งซื้อ",
                    Recon = new() { ShipDateColumn = "เวลาส่งสินค้า", ShipDateOffsetHours = 2, UnitPriceColumn = "ราคาขาย", QuantityColumn = "จำนวน",
                        SellerVoucherColumn = "โค้ดส่วนลดชำระโดยผู้ขาย", SkuColumn = "เลขอ้างอิง SKU (SKU Reference No.)", DeclaredRate = 0.496m },
                    FileNamePatterns = new() { "TH_", "_TH_", "_TH.", "thailand", "completed.th", "^thOrder." } },
                new() { MarketCode = "TW", HeaderRow = 1, DateColumn = "訂單完成時間", AmountColumn = "買家總支付金額",
                    OrderIdColumn = "訂單編號", SubtractColumns = new() { "買家支付運費" },
                    Recon = new() { ShipDateColumn = "實際出貨時間", ShipDateOffsetHours = 1, UnitPriceColumn = "商品活動價格", QuantityColumn = "數量",
                        SellerVoucherColumn = "賣家負擔優惠券", SkuColumn = "商品選項貨號", DeclaredRate = 0.835m },
                    FileNamePatterns = new() { "TW_", "_TW_", "_TW.", "taiwan", "completed.tw", "^twOrder." } },
                new() { MarketCode = "VN", HeaderRow = 1, DateColumn = "Thời gian hoàn thành đơn hàng", AmountColumn = "Tổng giá trị đơn hàng (VND)",
                    OrderIdColumn = "Mã đơn hàng",
                    Recon = new() { ShipDateColumn = "Thời gian giao hàng", ShipDateOffsetHours = 2, UnitPriceColumn = "Giá ưu đãi", QuantityColumn = "Số lượng",
                        SellerVoucherColumn = "Mã giảm giá của Shop", SkuColumn = "SKU phân loại hàng", DeclaredRate = 0.604m },
                    FileNamePatterns = new() { "VN_", "_VN_", "_VN.", "vietnam", "completed.vn", "^vnOrder." } },
                // 브라질 Valor Total은 상품소계+구매자 배송비(쿠폰 차감 전). 코인 컬럼은 개수라 0.01을 곱해 금액으로.
                new() { MarketCode = "BR", HeaderRow = 1, DateColumn = "Hora completa do pedido", AmountColumn = "Valor Total",
                    OrderIdColumn = "ID do pedido",
                    SubtractColumns = new() { "Taxa de envio pagas pelo comprador", "Cupom do vendedor", "Cupom Shopee", "Compensar Moedas Shopee*0.01" },
                    Recon = new() { ShipDateColumn = "Tempo de Envio", ShipDateOffsetHours = 12, UnitPriceColumn = "Preço acordado", QuantityColumn = "Quantidade",
                        SellerVoucherColumn = "Cupom do vendedor", SkuColumn = "Número de referência SKU", DeclaredRate = 0.860m },
                    FileNamePatterns = new() { "BR_", "_BR_", "_BR.", "brazil", "completed.br", "^brOrder." } },
                // "^xxOrder." / "^Order.completed.2"(싱가포르) = 2026년 2분기까지 받은 파일 이름 형식
                // 위시몰은 Download.CSV 한 파일에 Gross/Net이 함께 있고, 기획서 5장 기준 판매액=Gross.
                new() { MarketCode = "WISH", HeaderRow = 1, DateColumn = "Date", AmountColumn = "Gross",
                    FileNamePatterns = new() { "wish", "Wish" } },
            },
            RemittanceTrack = new RemittanceTrackConfig
            {
                HeaderRow = 1,
                DateColumn = "Date",
                AmountColumn = "Amount",
                DescriptionColumn = "Description",
                ExcludeContains = new List<string> { "Withdrawal to Woori" },
                Rules = new List<RemittanceRule>
                {
                    new() { Contains = new List<string> { "Shopee", "Singapore" }, MarketCode = "SG" },
                    new() { Contains = new List<string> { "Shopee", "Malaysia" }, MarketCode = "MY" },
                    new() { Contains = new List<string> { "Shopee", "Philippines" }, MarketCode = "PH" },
                    new() { Contains = new List<string> { "Shopee", "Thailand" }, MarketCode = "TH" },
                    new() { Contains = new List<string> { "Shopee", "Taiwan" }, MarketCode = "TW" },
                    new() { Contains = new List<string> { "Shopee", "Vietnam" }, MarketCode = "VN" },
                    new() { Contains = new List<string> { "Shopee", "VN" }, MarketCode = "VN" }, // 페이오니어 표기 "Payment from Shopee VN"
                    new() { Contains = new List<string> { "Shopee", "Brazil" }, MarketCode = "BR" },
                    new() { Contains = new List<string> { "Shopee", "Mexico" }, MarketCode = "MX" },
                    new() { Contains = new List<string> { "Amazon", "559199" }, MarketCode = "US" },
                    new() { Contains = new List<string> { "Amazon", "770668" }, MarketCode = "JP" },
                    new() { Contains = new List<string> { "WISE" }, MarketCode = "ZZ" },
                },
            },
        };

        return config;
    }
}
