using MiniERP2.Forms;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Utils;

/// <summary>
/// 메인 허브 검색창(MainHub)이 쓰는 전체 기능 목록. MainHub.BuildMenuGroups()의 최상위 26개 화면뿐
/// 아니라, 그 화면들 안의 주요 버튼과 — 택배운임 통계(TrackingBackfillViewer)처럼 — 거기서 다시
/// 열리는 2단계 아래 하위 창의 버튼까지 포함한다. 화면 생성 코드에서 자동으로 긁어오는 게 아니라
/// 손으로 유지하는 정적 목록이므로, 화면에 버튼을 추가/변경/삭제할 때는 이 목록도 같이 고쳐야
/// 검색 결과가 실제 화면과 어긋나지 않는다.
/// </summary>
public static class FeatureIndex
{
    public static List<FeatureIndexEntry> All { get; } = Build();

    private static List<FeatureIndexEntry> Build()
    {
        var entries = new List<FeatureIndexEntry>();

        void Top(string group, string label, Action open) => entries.Add(new FeatureIndexEntry(group, label, null, open));
        void Sub(string group, string label, string path, Action open) => entries.Add(new FeatureIndexEntry(group, label, path, open));

        // ── 발주/배송 ──────────────────────────────────────────────────────
        Top("발주/배송", "OFS (발주처리)", () => FormManager.Show<OfsForm>());
        Sub("발주/배송", "OFS (발주처리)", "발주 파일 로드", () => FormManager.Show<OfsForm>());
        Sub("발주/배송", "OFS (발주처리)", "수동 주문 추가", () => FormManager.Show<OfsForm>());
        Sub("발주/배송", "OFS (발주처리)", "배송지 불러오기", () => FormManager.Show<OfsForm>());
        Sub("발주/배송", "OFS (발주처리)", "매핑 도우미", () => FormManager.Show<OfsForm>());
        Sub("발주/배송", "OFS (발주처리)", "미매핑 일괄 처리", () => FormManager.Show<OfsForm>());
        Sub("발주/배송", "OFS (발주처리)", "저장 (발주확정)", () => FormManager.Show<OfsForm>());
        Sub("발주/배송", "OFS (발주처리)", "선택 행 삭제", () => FormManager.Show<OfsForm>());
        Sub("발주/배송", "OFS (발주처리)", "선택건 택배양식 내보내기", () => FormManager.Show<OfsForm>());
        Sub("발주/배송", "OFS (발주처리)", "전체 택배양식 내보내기", () => FormManager.Show<OfsForm>());

        Top("발주/배송", "발주/출고 이력", () => FormManager.Show<OutboundHistoryForm>());
        Sub("발주/배송", "발주/출고 이력", "조회", () => FormManager.Show<OutboundHistoryForm>());
        Sub("발주/배송", "발주/출고 이력", "운송장번호 불러오기", () => FormManager.Show<OutboundHistoryForm>());
        Sub("발주/배송", "발주/출고 이력", "누적발주서 송장번호 입력", () => FormManager.Show<OutboundHistoryForm>());
        Sub("발주/배송", "발주/출고 이력", "운송장 파일 누락건 점검", () => FormManager.Show<OutboundHistoryForm>());
        Sub("발주/배송", "발주/출고 이력", "선택 건 택배사 양식 출력", () => FormManager.Show<OutboundHistoryForm>());
        Sub("발주/배송", "발주/출고 이력", "변경사항 저장", () => FormManager.Show<OutboundHistoryForm>());
        Sub("발주/배송", "발주/출고 이력", "송장번호 출력", () => FormManager.Show<OutboundHistoryForm>());
        // 택배운임 통계는 "운송장 파일 누락건 점검" 버튼으로 연 뷰어 창(TrackingBackfillViewer) 하단에 있다.
        // 파일을 먼저 읽어야 뷰어가 뜨는 구조라 뷰어를 바로 열 수는 없어, 발주/출고 이력 화면으로 안내한다.
        Sub("발주/배송", "발주/출고 이력", "운송장 파일 누락건 점검 > 택배운임 통계 (운임통계 내보내기)", () => FormManager.Show<OutboundHistoryForm>());
        Sub("발주/배송", "발주/출고 이력", "운송장 파일 누락건 점검 > OFS로 보내기", () => FormManager.Show<OutboundHistoryForm>());
        Sub("발주/배송", "발주/출고 이력", "운송장 파일 누락건 점검 > 채널 추가", () => FormManager.Show<OutboundHistoryForm>());

        // 위와 같은 기능을 발주/출고 이력 화면을 거치지 않고 바로 실행하는 메인 허브 단축 버튼.
        Top("발주/배송", "운송장 파일 누락건 점검", () => TrackingBackfillCheckFlow.Run(Application.OpenForms.OfType<MainHub>().First()));
        Sub("발주/배송", "운송장 파일 누락건 점검", "택배운임 통계 (운임통계 내보내기)", () => TrackingBackfillCheckFlow.Run(Application.OpenForms.OfType<MainHub>().First()));

        Top("발주/배송", "풀필먼트 발주 처리", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "CSKU 검색하여 추가", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "박스 산정", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "품목/설정 관리", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "행 추가(합포장)", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "지난 발주 불러오기", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "지난 CSKU 불러오기", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "입고양식 파일 변환", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "운송장 불러오기", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "엑셀파일 내보내기", () => FormManager.Show<FboOrderForm>());
        Sub("발주/배송", "풀필먼트 발주 처리", "저장 (발주확정)", () => FormManager.Show<FboOrderForm>());

        Top("발주/배송", "풀필먼트 발주 이력", () => FormManager.Show<FboHistoryForm>());
        Sub("발주/배송", "풀필먼트 발주 이력", "조회", () => FormManager.Show<FboHistoryForm>());
        Sub("발주/배송", "풀필먼트 발주 이력", "복사하여 신규 발주", () => FormManager.Show<FboHistoryForm>());
        Sub("발주/배송", "풀필먼트 발주 이력", "택배사 양식 출력하기", () => FormManager.Show<FboHistoryForm>());
        Sub("발주/배송", "풀필먼트 발주 이력", "입고양식 파일 변환", () => FormManager.Show<FboHistoryForm>());
        Sub("발주/배송", "풀필먼트 발주 이력", "운송장번호 불러오기", () => FormManager.Show<FboHistoryForm>());
        Sub("발주/배송", "풀필먼트 발주 이력", "선택 라인 수정", () => FormManager.Show<FboHistoryForm>());
        Sub("발주/배송", "풀필먼트 발주 이력", "선택 라인 복사(신규발주)", () => FormManager.Show<FboHistoryForm>());

        Top("발주/배송", "자동발주처리", () => FormManager.Show<AutoOrderInboxForm>());
        Sub("발주/배송", "자동발주처리", "지금 확인", () => FormManager.Show<AutoOrderInboxForm>());
        Sub("발주/배송", "자동발주처리", "다운로드&저장", () => FormManager.Show<AutoOrderInboxForm>());
        Sub("발주/배송", "자동발주처리", "발주 파일 로드로 열기", () => FormManager.Show<AutoOrderInboxForm>());
        Sub("발주/배송", "자동발주처리", "연동 설정", () => FormManager.Show<AutoOrderInboxForm>());

        Top("발주/배송", "FBA 발주 처리", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "CSKU 검색하여 추가", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "품목/설정 관리", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "장바구니 임시저장", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "장바구니 불러오기", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "박스 추가", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "선택 품목 박스에 담기", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "아마존 선적명세 내보내기", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "운송장 불러오기", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "하배출고이서 내보내기", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "작업지시서 발행", () => FormManager.Show<FbaOrderForm>());
        Sub("발주/배송", "FBA 발주 처리", "저장 (발주확정)", () => FormManager.Show<FbaOrderForm>());

        Top("발주/배송", "FBA 발주 이력", () => FormManager.Show<FbaHistoryForm>());
        Sub("발주/배송", "FBA 발주 이력", "조회", () => FormManager.Show<FbaHistoryForm>());
        Sub("발주/배송", "FBA 발주 이력", "발주 상세 열기", () => FormManager.Show<FbaHistoryForm>());
        Sub("발주/배송", "FBA 발주 이력", "복사하여 신규 발주", () => FormManager.Show<FbaHistoryForm>());
        Sub("발주/배송", "FBA 발주 이력", "하배출고이서 재출력", () => FormManager.Show<FbaHistoryForm>());
        Sub("발주/배송", "FBA 발주 이력", "선적명세 재출력", () => FormManager.Show<FbaHistoryForm>());
        Sub("발주/배송", "FBA 발주 이력", "Shipment ID 입력", () => FormManager.Show<FbaHistoryForm>());
        Sub("발주/배송", "FBA 발주 이력", "작업지시서 발행", () => FormManager.Show<FbaHistoryForm>());

        // ── 기준정보 ──────────────────────────────────────────────────────
        Top("기준정보", "마스터SKU 관리", () => FormManager.Show<MasterSkuForm>());
        Sub("기준정보", "마스터SKU 관리", "새 마스터SKU 추가", () => FormManager.Show<MasterSkuForm>());
        Sub("기준정보", "마스터SKU 관리", "엑셀 가져오기", () => FormManager.Show<MasterSkuForm>());
        Sub("기준정보", "마스터SKU 관리", "엑셀로 내보내기", () => FormManager.Show<MasterSkuForm>());
        Sub("기준정보", "마스터SKU 관리", "해당 CSKU 보기", () => FormManager.Show<MasterSkuForm>());
        Sub("기준정보", "마스터SKU 관리", "매입·납품 통합 조회", () => FormManager.Show<MasterSkuForm>());

        Top("기준정보", "거래처별 CSKU 관리", () => FormManager.Show<ChannelCskuForm>());
        Sub("기준정보", "거래처별 CSKU 관리", "CSKU 추가", () => FormManager.Show<ChannelCskuForm>());
        Sub("기준정보", "거래처별 CSKU 관리", "CSKU 삭제", () => FormManager.Show<ChannelCskuForm>());
        Sub("기준정보", "거래처별 CSKU 관리", "엑셀로 내보내기", () => FormManager.Show<ChannelCskuForm>());
        Sub("기준정보", "거래처별 CSKU 관리", "마스터SKU 미등록 CSKU 찾기", () => FormManager.Show<ChannelCskuForm>());

        Top("기준정보", "매핑 관리", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "매핑하기", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "임시 SKU 등록 후 매핑", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "조건부 매핑 규칙 추가", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "예외 처리(매핑 제외)", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "새 규칙 추가", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "규칙 정보 저장", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "직전매핑취소", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "조건 추가", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "상세조건 저장", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "CSKU 저장", () => FormManager.Show<MappingForm>());
        // "전체 조건부규칙 보기"로 여는 하위 창(ConditionRuleListForm).
        Sub("기준정보", "매핑 관리", "전체 조건부규칙 보기 > 새 규칙 추가", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "전체 조건부규칙 보기 > 중복 규칙 병합", () => FormManager.Show<MappingForm>());
        Sub("기준정보", "매핑 관리", "전체 조건부규칙 보기 > 이 규칙 편집", () => FormManager.Show<MappingForm>());
        // OFS/마감/이익분석/미매핑 처리 화면에서 공통으로 뜨는 매핑 작업창(MappingWorkbenchDialog).
        Sub("기준정보", "매핑 관리", "매핑 도우미(OFS) > 이 SKU로 매핑", () => FormManager.Show<OfsForm>());
        Sub("기준정보", "매핑 관리", "매핑 도우미(OFS) > 매핑 대상에서 제외", () => FormManager.Show<OfsForm>());
        Sub("기준정보", "매핑 관리", "매핑 도우미(OFS) > 새 마스터SKU 등록(정식)", () => FormManager.Show<OfsForm>());

        Top("기준정보", "채널 설정", () => FormManager.Show<ChannelConfigForm>());
        Sub("기준정보", "채널 설정", "추가", () => FormManager.Show<ChannelConfigForm>());
        Sub("기준정보", "채널 설정", "저장", () => FormManager.Show<ChannelConfigForm>());
        Sub("기준정보", "채널 설정", "엑셀 다운로드", () => FormManager.Show<ChannelConfigForm>());
        Sub("기준정보", "채널 설정", "현재 설정 내보내기", () => FormManager.Show<ChannelConfigForm>());
        Sub("기준정보", "채널 설정", "엑셀 일괄 등록", () => FormManager.Show<ChannelConfigForm>());
        Sub("기준정보", "채널 설정", "택배사 양식 관리", () => FormManager.Show<ChannelConfigForm>());
        Sub("기준정보", "채널 설정", "SalesManagerV2 채널 가져오기", () => FormManager.Show<ChannelConfigForm>());
        // 택배사 양식 관리 버튼으로 여는 하위 창(CourierConfigForm) — 택배사별 운송장/운임 파일 양식(헤더) 설정.
        Sub("기준정보", "채널 설정", "택배사 양식 관리 > 택배사 추가", () => FormManager.Show<CourierConfigForm>());
        Sub("기준정보", "채널 설정", "택배사 양식 관리 > 택배사 삭제", () => FormManager.Show<CourierConfigForm>());
        Sub("기준정보", "채널 설정", "택배사 양식 관리 > 샘플 양식 불러오기", () => FormManager.Show<CourierConfigForm>());
        Sub("기준정보", "채널 설정", "택배사 양식 관리 > 저장", () => FormManager.Show<CourierConfigForm>());

        Top("기준정보", "견적·단가 관리", () => FormManager.Show<PriceQuoteForm>());
        Sub("기준정보", "견적·단가 관리", "새 견적", () => FormManager.Show<PriceQuoteForm>());
        Sub("기준정보", "견적·단가 관리", "채널 추가...", () => FormManager.Show<PriceQuoteForm>());
        Sub("기준정보", "견적·단가 관리", "저장", () => FormManager.Show<PriceQuoteForm>());
        Sub("기준정보", "견적·단가 관리", "삭제", () => FormManager.Show<PriceQuoteForm>());
        Sub("기준정보", "견적·단가 관리", "CSKU 선택...", () => FormManager.Show<PriceQuoteForm>());

        Top("기준정보", "배송지 주소록 관리", () => FormManager.Show<AddressBookForm>());
        Sub("기준정보", "배송지 주소록 관리", "추가", () => FormManager.Show<AddressBookForm>());
        Sub("기준정보", "배송지 주소록 관리", "삭제", () => FormManager.Show<AddressBookForm>());
        Sub("기준정보", "배송지 주소록 관리", "저장", () => FormManager.Show<AddressBookForm>());

        Top("기준정보", "간이 마진 계산기", () => FormManager.Show<MarginCalculatorForm>());
        Sub("기준정보", "간이 마진 계산기", "항목 추가", () => FormManager.Show<MarginCalculatorForm>());
        Sub("기준정보", "간이 마진 계산기", "SKU 불러오기", () => FormManager.Show<MarginCalculatorForm>());
        Sub("기준정보", "간이 마진 계산기", "행 추가", () => FormManager.Show<MarginCalculatorForm>());
        Sub("기준정보", "간이 마진 계산기", "DB 적용", () => FormManager.Show<MarginCalculatorForm>());
        Sub("기준정보", "간이 마진 계산기", "신규 SKU 등록", () => FormManager.Show<MarginCalculatorForm>());
        Sub("기준정보", "간이 마진 계산기", "엑셀 내보내기", () => FormManager.Show<MarginCalculatorForm>());
        Sub("기준정보", "간이 마진 계산기", "임시저장", () => FormManager.Show<MarginCalculatorForm>());
        Sub("기준정보", "간이 마진 계산기", "임시저장 불러오기", () => FormManager.Show<MarginCalculatorForm>());

        Top("기준정보", "정산 마진 계산기", () => FormManager.Show<SimpleMarginCalculatorForm>());
        Sub("기준정보", "정산 마진 계산기", "CSKU 불러오기", () => FormManager.Show<SimpleMarginCalculatorForm>());
        Sub("기준정보", "정산 마진 계산기", "조회", () => FormManager.Show<SimpleMarginCalculatorForm>());
        Sub("기준정보", "정산 마진 계산기", "행 추가", () => FormManager.Show<SimpleMarginCalculatorForm>());
        Sub("기준정보", "정산 마진 계산기", "임시저장", () => FormManager.Show<SimpleMarginCalculatorForm>());
        Sub("기준정보", "정산 마진 계산기", "임시저장 불러오기", () => FormManager.Show<SimpleMarginCalculatorForm>());

        // ── 정산 ──────────────────────────────────────────────────────────
        Top("정산", "마감/이익분석", () => FormManager.Show<SettlementForm>());
        Sub("정산", "마감/이익분석", "정산파일 로드", () => FormManager.Show<SettlementForm>());
        Sub("정산", "마감/이익분석", "결과 저장", () => FormManager.Show<SettlementForm>());
        Sub("정산", "마감/이익분석", "보고서 저장", () => FormManager.Show<SettlementForm>());
        Sub("정산", "마감/이익분석", "엑셀로 내보내기", () => FormManager.Show<SettlementForm>());
        Sub("정산", "마감/이익분석", "출고내역 조회", () => FormManager.Show<SettlementForm>());
        Sub("정산", "마감/이익분석", "거래처 마감내역 불러오기", () => FormManager.Show<SettlementForm>());
        Sub("정산", "마감/이익분석", "출고내역 엑셀로 내보내기", () => FormManager.Show<SettlementForm>());

        Top("정산", "광고비 분석", () => FormManager.Show<AdMappingForm>());
        Sub("정산", "광고비 분석", "SalesManagerV2 데이터 가져오기", () => FormManager.Show<AdMappingForm>());
        Sub("정산", "광고비 분석", "불러온 항목 초기화", () => FormManager.Show<AdMappingForm>());
        Sub("정산", "광고비 분석", "분석결과 내보내기", () => FormManager.Show<AdMappingForm>());
        Sub("정산", "광고비 분석", "보고서에 저장", () => FormManager.Show<AdMappingForm>());
        Sub("정산", "광고비 분석", "규칙 추가", () => FormManager.Show<AdMappingForm>());
        Sub("정산", "광고비 분석", "규칙 정보 저장", () => FormManager.Show<AdMappingForm>());
        Sub("정산", "광고비 분석", "조건 추가", () => FormManager.Show<AdMappingForm>());

        Top("정산", "월별 마감 자동화", () => FormManager.Show<MonthlyClosingForm>());
        Sub("정산", "월별 마감 자동화", "찾아보기", () => FormManager.Show<MonthlyClosingForm>());
        Sub("정산", "월별 마감 자동화", "스캔", () => FormManager.Show<MonthlyClosingForm>());
        // 스캔 도중 미매핑 건이 있으면 뜨는 하위 창(UnmappedQueueForm).
        Sub("정산", "월별 마감 자동화", "스캔 > 미매핑 항목 매핑하기", () => FormManager.Show<MonthlyClosingForm>());

        Top("정산", "거래처 마감보드", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "수동 거래처 추가", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "금액입력/비고", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "수동 주문 추가", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "엑셀 일괄 추가", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "마감확정", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "확정취소", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "명세표 미리보기", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "매출장 미리보기", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "명세표 발행", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "매출장 발행", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "현황판 엑셀저장", () => FormManager.Show<PartnerClosingForm>());
        Sub("정산", "거래처 마감보드", "비매출 내역", () => FormManager.Show<PartnerClosingForm>());

        // ── 보고서 ────────────────────────────────────────────────────────
        Top("보고서", "종합보고서", () => FormManager.Show<ReportForm>());
        Sub("보고서", "종합보고서", "Excel에서 불러오기", () => FormManager.Show<ReportForm>());
        Sub("보고서", "종합보고서", "Excel 내보내기", () => FormManager.Show<ReportForm>());

        Top("보고서", "수출요약보고서", () => FormManager.Show<ExportSummaryForm>());
        Sub("보고서", "수출요약보고서", "A. 수출신고 파일 추가", () => FormManager.Show<ExportSummaryForm>());
        Sub("보고서", "수출요약보고서", "B. 판매(마켓선택)", () => FormManager.Show<ExportSummaryForm>());
        Sub("보고서", "수출요약보고서", "B. 파일명 자동탐지", () => FormManager.Show<ExportSummaryForm>());
        Sub("보고서", "수출요약보고서", "C. 송금 파일 추가", () => FormManager.Show<ExportSummaryForm>());
        Sub("보고서", "수출요약보고서", "수동 입력 편집기", () => FormManager.Show<ExportSummaryForm>());
        Sub("보고서", "수출요약보고서", "임시저장 불러오기", () => FormManager.Show<ExportSummaryForm>());
        Sub("보고서", "수출요약보고서", "집계", () => FormManager.Show<ExportSummaryForm>());
        Sub("보고서", "수출요약보고서", "엑셀로 내보내기", () => FormManager.Show<ExportSummaryForm>());

        Top("보고서", "CSKU별 통계", () => FormManager.Show<CskuStatForm>());
        Sub("보고서", "CSKU별 통계", "파일 추가", () => FormManager.Show<CskuStatForm>());
        Sub("보고서", "CSKU별 통계", "집계 실행", () => FormManager.Show<CskuStatForm>());
        Sub("보고서", "CSKU별 통계", "배치 저장", () => FormManager.Show<CskuStatForm>());
        Sub("보고서", "CSKU별 통계", "배치 불러오기", () => FormManager.Show<CskuStatForm>());
        Sub("보고서", "CSKU별 통계", "엑셀 내보내기", () => FormManager.Show<CskuStatForm>());

        // ── 문서관리 ──────────────────────────────────────────────────────
        Top("문서관리", "문서관리", () => FormManager.Show<DocLineHistoryForm>());
        Sub("문서관리", "문서관리", "조회", () => FormManager.Show<DocLineHistoryForm>());
        Sub("문서관리", "문서관리", "엑셀로 내보내기", () => FormManager.Show<DocLineHistoryForm>());
        Sub("문서관리", "문서관리", "전체 문서 작성 화면 열기", () => FormManager.Show<DocLineHistoryForm>());
        Sub("문서관리", "문서관리", "견적서 담기", () => FormManager.Show<DocLineHistoryForm>());
        Sub("문서관리", "문서관리", "견적서 작성", () => FormManager.Show<DocLineHistoryForm>());

        Top("문서관리", "전체 문서 작성(고급)", () => FormManager.Show<DocsForm>());

        Top("문서관리", "거래명세표 조회/내보내기", () => FormManager.Show<DocStatementBrowserForm>());
        Sub("문서관리", "거래명세표 조회/내보내기", "조회", () => FormManager.Show<DocStatementBrowserForm>());
        Sub("문서관리", "거래명세표 조회/내보내기", "엑셀로 내보내기", () => FormManager.Show<DocStatementBrowserForm>());

        // ── 데이터관리 ────────────────────────────────────────────────────
        Top("데이터관리", "데이터 관리", () => FormManager.Show<DataManagementForm>());
        Sub("데이터관리", "데이터 관리", "엑셀 내보내기", () => FormManager.Show<DataManagementForm>());
        Sub("데이터관리", "데이터 관리", "엑셀 불러오기", () => FormManager.Show<DataManagementForm>());
        Sub("데이터관리", "데이터 관리", "변경내역 저장", () => FormManager.Show<DataManagementForm>());
        Sub("데이터관리", "데이터 관리", "선택한 CSKU 삭제", () => FormManager.Show<DataManagementForm>());
        Sub("데이터관리", "데이터 관리", "선택한 규칙 삭제", () => FormManager.Show<DataManagementForm>());
        Sub("데이터관리", "데이터 관리", "지금 전체 백업", () => FormManager.Show<DataManagementForm>());
        Sub("데이터관리", "데이터 관리", "선택한 시점으로 복원", () => FormManager.Show<DataManagementForm>());
        Sub("데이터관리", "데이터 관리", "전체 가져오기", () => FormManager.Show<DataManagementForm>());
        Sub("데이터관리", "데이터 관리", "선택 가져오기", () => FormManager.Show<DataManagementForm>());

        // 레거시 데이터 가져오기는 MainHub 자신의 메뉴 핸들러(OnLegacyImportClick)를 직접 여는 방식이라
        // 이 정적 인덱스(MainHub 인스턴스를 모름)에서는 실행할 수 없다 — 메뉴 위치만 안내한다.
        Top("데이터관리", "레거시 데이터 가져오기", () => MessageBox.Show(
            "메인 허브 상단 메뉴의 '데이터관리 > 레거시 데이터 가져오기'에서 실행할 수 있습니다.",
            "안내", MessageBoxButtons.OK, MessageBoxIcon.Information));

        return entries;
    }
}
