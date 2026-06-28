# MiniERP2 개발기획서

신규 프로젝트 기술 기획안 (v0.2) · VS Code 기반 개발

> v0.2 변경사항: (1) 화면별 명칭을 MainHub/OFS 등으로 확정, (2) 공통 UI/UX 요구사항(2장) 신설, (3) SalesManagerV2(Python) 실제 코드 분석을 통해 채널별 손익공식·다중시트 JOIN 구조를 4.6/4.4절에 구체화, (4) 1.3절 가정 사항을 진행 경과에 따라 갱신.
>
> 원본: `MiniERP2_개발기획서_v0.2.docx` (Desktop) — 2026-06-26 Claude Code로 변환.

## 0. 용어 및 화면 명칭 정의

본 문서 및 이후 대화에서 사용하는 화면 지칭 용어를 아래와 같이 고정한다.

| 자연어 지칭(대화용) | 화면 명칭(코드/문서용) | 기존 코드 대응 |
|---|---|---|
| 메인화면 / 메인창 / 메인허브 / 시작화면 | **MainHub** | 기존 Form1 |
| 오더처리 / 발주처리 | **OFS** (Order Fulfill System) | 기존 FormMappingManagerV2와 역할 유사 |
| 마스터SKU 관리창 | 마스터SKU 관리창 (+파생 CSKU 관리창) | 기존 FormMskuManager |
| 매핑관리창 | 매핑관리창 | 기존 FormConditionManager + FormUnmappedChecker 통합 개념 |
| 채널 설정 창 | 채널 설정 창 | 기존 FormOnlineChannelConfig |
| 마감/이익분석 창 | 마감/이익분석 창 | 기존 FormSettlementDashboard + FormOnlineSettlementManager 통합 개념 |
| 기타/문서관리 | 기타/문서관리 | 기존 미구현 항목 |

OFS를 '발주서→매핑→엑셀양식 출력→엑셀업로드 및 발송처리'까지 한 창에서 처리하는 구성은, 기존 FormMappingManagerV2가 이미 유사한 단일창 구조였으므로 자연스러운 확장이다.

## 1. 개요

### 1.1 목적

기존 GitHub 저장소 'MiniERP'(V3.2, C#/.NET 10 WinForms 기반)와 별도로 운영되어온 Python 기반 'SalesManagerV2'(채널별 정산/매핑 로직의 실사용 레퍼런스)는 참고 자료로만 활용하고, 'MiniERP2'를 완전히 새로운 프로젝트로 시작한다.

### 1.2 분석 대상 정리

- GitHub 'MiniERP'(C#/.NET 10 WinForms, 단일 커밋): UI/폼 구조, DB 스키마, 매핑 우선순위 로직의 기준 코드
- 'SalesManagerV2'(Python/PyQt6, 실사용 중): 채널별 손익계산 공식, 다중 파일·다중 시트 헤더 로드(growth_aux_sources) 로직의 기준 코드 — 채널별 실제 운영 설정(channels_config.json 등) 포함
- 기획 문서 2건(초안 V2.0, 새대화창 V3.2): 기능 범위 및 진행상태 기록

1.2절 상세 분석 결과(기능별 구현 확인/미확인 목록)는 v0.1에서 정리된 내용을 그대로 유지한다(본 문서 부록 성격 — 별도 보관).

### 1.3 전제 및 확인 경과

| 항목 | v0.1 당시 상태 | v0.2 갱신 상태 |
|---|---|---|
| 기술 스택 | C#/.NET WinForms+SQLite 유지 가정 | VS Code 기반 개발로 확정. WinForms는 코드 기반 UI 패턴(.Designer.cs 미사용) 유지, 특정 폼만 필요시 Visual Studio 디자이너 패턴 전환 가능 |
| 기능 범위 | WMS 포함 전체 범위 가정 | OFS(발주~출고), 마스터SKU/CSKU, 매핑관리, 채널설정, 마감/이익분석, 문서관리까지 전체 화면 목록이 구체화되어 전체 범위로 진행 확인됨 |

## 2. 공통 UI/UX 요구사항

모든 화면에 공통으로 적용되는 동작 규칙이다. 기존 코드 기준 구현 여부를 함께 표기한다.

- **[부분 보완] 2.1 시스템 라이트/다크 모드 자동 대응**
  고정 색상(특히 흰색/밝은색 텍스트) 하드코딩 금지. .NET 10 WinForms부터 `Application.SetColorMode(SystemColorMode.System)`가 정식 기능으로 제공되어 OS 다크모드를 자동 반영한다(MiniERP2는 net10.0-windows 대상이라 바로 적용 가능). 단, 기존 코드(UiTheme.cs 등)에 퍼져있는 `Color.FromArgb(...)` / `ForeColor=Color.White` 하드코딩은 전부 제거 대상이다. `FlatStyle.Flat` 버튼의 커스텀 색이나 DataGridView 헤더는 자동 적용되지 않아 별도 처리가 필요하다(공식 문서에 명시된 한계).
- **[신규 구현] 2.2 엑셀 내보내기 후 처리**
  내보내기 완료 시 '파일 열기 / 폴더 열기 / 닫기'를 묻는 공통 다이얼로그를 신설한다. 모든 엑셀 출력 기능(정산 추출, 출고 이력 추출, 거래명세표 등)에서 동일한 헬퍼를 재사용한다.
- **[부분 보완] 2.3 DataGridView 엑셀형 동작**
  열 순서/열 폭/행 높이 기억 + 우클릭 복사·붙여넣기·수정·다중선택을 모든 표에서 기본 제공한다. 기존 FormManager.cs는 열 폭만 저장하며 열 순서·행 높이는 미저장 상태다. 우클릭 메뉴는 현재 FormMskuManager.cs 한 곳에만 존재해 공통화되어 있지 않다. → 공통 커스텀 컨트롤(예: `ExcelLikeDataGridView`)을 만들어 전체 화면에 동일 적용하는 것을 권장한다.
- **[부분 보완] 2.4 기능별 마지막 폴더 위치 기억**
  DB 파일 열기, 발주파일 열기 등 기능별로 최근 폴더 경로를 독립적으로 저장한다. 기존 Config/SettingsService.cs 구조에 키별 저장을 추가하면 된다(Python 구버전의 `get_last_folder(key)`/`set_last_folder(key, path)` 패턴과 동일한 설계로, 검증된 방식).
- **[기존 자산 계승] 2.5 엑셀/CSV 처리 및 암호 파일**
  기본 xlsx, csv 지원. 암호 지정 파일은 암호 입력창을 띄운다. 기존 SmartLoader(csv/xlsx 처리)와 FormExcelPassword(암호 해제)로 이미 구현된 패턴을 계승한다.
- **[기존 자산 계승] 2.6 창 크기 기억**
  각 창은 마지막 크기/위치를 기억한다. 기존 FormManager.cs가 Width/Height/Top/Left/WindowState를 저장·복원하는 기능을 이미 보유하고 있어 그대로 계승한다.
- **[기존 자산 계승] 2.7 창 중복 실행 방지**
  동일 화면을 중복으로 띄우지 않고, 이미 열려있으면 해당 창을 최상단으로 가져온다. 기존 `FormManager.Show<T>()`가 정확히 이 동작(`Application.OpenForms` 검색 → 있으면 BringToFront, 없으면 신규 생성)을 구현하고 있어 그대로 계승한다.

## 3. 기술 스택 및 개발 환경

### 3.1 유지하는 기술

- 런타임/UI: .NET 10, WinForms (WinExe), C# 14
- DB: SQLite 단일 파일(`ERP_Database.sqlite`)
- 엑셀 처리: EPPlus(OfficeOpenXml) — 다중 시트 로드에 필요
- UI 구성 방식: 코드 기반 동적 생성 유지, 특정 폼만 필요시 `.Designer.cs` 패턴 전환 가능
- 다크모드: `Application.SetColorMode(SystemColorMode.System)` 적용(2.1절)

### 3.2 정리가 필요한 부분

| 항목 | 기존 상태 | MiniERP2 권고 |
|---|---|---|
| 엑셀 라이브러리 | ClosedXML + EPPlus + ExcelDataReader 3종 혼재 | EPPlus 단일화, ExcelDataReader는 .xls 읽기 전용 검토, ClosedXML 제거 |
| SQLite 드라이버 | System.Data.SQLite + SQLitePCLRaw 혼재 | Microsoft.Data.Sqlite(+Dapper) 단일화 검토 |
| 색상 처리 | Color.FromArgb 하드코딩 다수 | SystemColors 기반 + 다크모드 API로 전환(2.1절) |
| 네임스페이스 | 루트/하위 혼재 | 계층별 네임스페이스 고정(4.0절) |
| DB 경로 처리 | StartupPath 조합과 하드코딩 혼재 | 공통 PathProvider 클래스로 단일화 |

### 3.3 개발 도구

- 에디터: Visual Studio Code + C# Dev Kit(또는 OmniSharp) + Claude Code 확장
- 빌드/실행: dotnet CLI (`dotnet build` / `dotnet run`)
- WinForms 디자이너가 필요한 특정 폼만 작업 시점에 Visual Studio 병행 사용(동일 .csproj 공유, 이식 절차 불필요)
- 형상관리: Git, MiniERP2는 새 리포지토리로 시작

## 4. 시스템 아키텍처

### 4.0 폴더/네임스페이스 구조

| 폴더 | 네임스페이스 | 역할 |
|---|---|---|
| /Forms | MiniERP2.Forms | MainHub, OFS, 마스터SKU/CSKU 관리창, 매핑관리창, 채널설정창, 마감/이익분석창, 문서관리창 |
| /Models | MiniERP2.Models | DTO/설정 모델(ChannelConfig, StdField Enum 등) |
| /DataLoaders | MiniERP2.DataLoaders | 엑셀/CSV 로드, 다중시트 JOIN(growth_aux_sources), 시트 검증 |
| /Mapping | MiniERP2.Mapping | SKU 매핑 엔진, 조건 평가기, 충돌 감지(신규) |
| /Database | MiniERP2.Database | SQLite 접근 계층(Repository) |
| /Config | MiniERP2.Config | 설정 파일(JSON) 입출력, 기능별 최근 폴더 경로 |
| /Controls | MiniERP2.Controls | 공통 커스텀 컨트롤(ExcelLikeDataGridView 등, 2.3절) |
| /Utils | MiniERP2.Utils | 헤더 정규화 등 공통 유틸 |
| /Tests | MiniERP2.Tests | 모듈 단위 통합 테스트 |

규칙: 루트 네임스페이스(MiniERP2)에는 Program.cs 외 클래스를 두지 않는다. 사용되지 않게 된 클래스는 즉시 삭제한다.

### 4.1 데이터 처리 흐름 (4단계 정규화)

1. **1단계 Raw Load**: 엑셀/CSV 원본을 DataTable로 적재(파일별로 헤더 행이 다를 수 있음)
2. **2단계 Standardization**: 채널별 설정(필드별 col/sheet_name/header_row)에 따라 원본 헤더를 표준 필드(StdField)로 변환 — 같은 파일 내에서도 필드마다 다른 시트/다른 헤더행을 지정 가능(SalesManagerV2 SmartLoader 검증된 패턴)
3. **2.5단계 보조시트 JOIN**: growth_aux_sources(보조 시트명/헤더행/키컬럼/값컬럼)에 정의된 보조 시트를 키 컬럼 기준으로 메인 데이터에 JOIN — 4.4절 상세
4. **3단계 Transformation**: 조건부 규칙 적용, SKU 매핑(예외→1:1→임시→조건부) 적용, 매핑 충돌 감지
5. **4단계 Calculation**: 채널 타입별 손익 공식 적용(4.6절 상세)

### 4.2 데이터베이스 스키마 (초안 v0.2)

| 테이블 | 핵심 컬럼 | 비고 |
|---|---|---|
| ItemTable | SKU, ItemName, CostPrice | 마스터SKU(품목/원가 마스터) |
| ItemCostHistory | SKU, OldCost, NewCost, ChangedAt | 마스터SKU 원가 변경이력 — 신규 |
| ChannelSkuTable(CSKU) | ChannelCode, MSKU, SupplyPrice | 채널별 CSKU 납품가 |
| ChannelSkuPriceHistory | ChannelCode, MSKU, OldPrice, NewPrice, ChangedAt | CSKU 납품가 변경이력 — 신규 |
| CourierMasterTable | CourierName, 헤더매핑 JSON | 택배사별 출고 양식 |
| SalesChannelTable | ChannelCode, ChannelName | 거래처/채널 마스터 |
| SettlementData | StdProductId/Amt/Settlement/Shipping/Fee 등 | 온라인 정산 표준화 결과 |
| OutboundDetailTable | OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice | OFS 출고 상세 |
| RuleExact / RuleCondition / RuleTemp / RuleException | ChannelCode, Key, TargetSku 등 | SKU 매핑 룰셋 4종 |
| MappingHistory | ChannelCode, Key, OldSku, NewSku, MatchType, ChangedAt | 매핑 변경이력/중복매핑 추적 — 신규 |

ItemCostHistory / ChannelSkuPriceHistory / MappingHistory는 2장 요구사항(변경이력 추적)에 따라 v0.2에서 신규 추가된 테이블이다. 정확한 컬럼/제약조건은 4장 모듈별 설계 단계에서 확정한다.

## 5. 기능 모듈 상세 명세

### 5.1 MainHub

- 사이드바 메뉴에서 각 기능별 화면을 새 창으로 호출(`FormManager.Show<T>` 패턴 계승, 2.7절)
- 마스터 데이터 동기화 상태 요약 표시

### 5.2 마스터SKU 관리창 + CSKU 관리창

- 마스터SKU: 로컬 보관 엑셀 마스터DB를 프로그램에 로드, 마스터상품명/마스터제조원가 등록·수정, 원가 변경이력(ItemCostHistory) 기록
- CSKU 관리창(파생): 마스터SKU와 채널을 연동하여 채널별 납품가(ChannelSkuTable) 등록, 단가 변경이력(ChannelSkuPriceHistory) 및 출고이력 조회

### 5.3 매핑관리창

- 채널별 상품명/옵션명/상품번호 등에 따른 매핑 내역 관리
- 우선순위: 예외처리 > 1:1 매핑(상품+옵션+수량 등 완전일치) > 임시SKU 매핑 > 조건부 매핑
- 매핑 변경이력(MappingHistory) 및 중복 매핑 관리
- 매핑 충돌(2개 이상 규칙이 동시에 적용되는 경우) 자동 감지 및 화면 강조 — 신규 구현(기존 C#/Python 두 코드베이스 모두에 미존재 확인됨)

### 5.4 채널 설정 창

온/오프라인 채널별로 발주파일·정산파일 등 파일별 헤더값을 어떻게 읽어올지 지정한다. SalesManagerV2(Python) 실사용 코드/설정을 레퍼런스로 명세를 구체화한다.

- 필드별 개별 시트/헤더행 지정: 같은 채널 설정 안에서도 표준 필드(StdField)마다 sheet_name/header_row를 따로 지정 가능(전체(자동) 옵션 포함)
- 보조소스 JOIN(growth_aux_sources): 메인 시트 외 보조 시트(예: '입출고비', '배송비')를 키 컬럼(옵션ID 등) 기준으로 매칭하여 비용 데이터를 JOIN — 실제 운영 설정(쿠팡그로스)에서 입출고비/배송비 시트 분리 JOIN 확인됨. 항목: enabled, target_std_field, sheet_name, header_row, key_header, value_header, out_col
- 특수 보조정보 매칭: 채널 유형별로 별도 파일(예: 세금계산서 요약본)의 키값을 본 데이터에 매칭하는 보조 로직(예: 계산서번호↔발행일자) — 채널 유형별 신규 규칙으로 확장 가능한 구조로 설계
- 조건부 헤더 매핑: 특정 조건(수량=0 등)에 따라 다른 표준 필드로 값을 재분류(배송비 이중집계 방지 등)

위 보조소스 JOIN 로직은 기존 C# 코드에는 데이터 모델(GrowthAuxSource)만 있고 실제 처리 로직이 없었던 부분이다. SalesManagerV2(Python)의 동작 로직을 신뢰할 수 있는 레퍼런스로 채택하여 C#으로 정식 포팅한다.

### 5.5 OFS (오더처리/발주처리)

발주서 로드 → 매핑 → 엑셀양식 출력 → 엑셀 업로드 및 발송처리까지를 한 창에서 수행한다.

- 스마트 엑셀 로더: 암호 지정 엑셀 자동 해제, 발주서 누적 로드(2.5절 공통규칙 적용)
- 발주서 역순(최신순) 선택 로더: 여러 누적 파일 중 최신 데이터를 선택적으로 로드
- 수동 주문 인젝터: CS 수동 주문 병합
- 이중 출고 방지(Upsert): 중복 저장 차단 및 최신값 덮어쓰기
- 택배사 양식 자동 인식 후 엑셀 출력, 발송처리 기록
- 운송장 매핑, 동명이인 조율, 합포장/분리배송 컨텍스트 메뉴 — 4.x 단계에서 신규 설계

### 5.6 마감/이익분석 창

마감 대조와 이익분석을 한 화면에서 다룬다.

**마감 대조(수기)**: 월말 엑셀 출고내역 일괄 출력, 또는 수시 업로드·발송처리된 내역과 거래처 제공 마감내역을 사용자가 수기로 대조

**이익분석(자동) — 채널별 손익 공식**: 온라인 정산파일을 불러와 채널별로 정해진 로직에 따라 이익을 계산한다. 폭넓은 조합의 상품명+옵션명+가격 등으로 SKU를 매핑하는 것이 선행 단계의 핵심이다(5.3절). SalesManagerV2(Python) analysis_engine.py의 실제 운영 로직을 검증된 레퍼런스로 사용한다.

| 채널 유형 | 이익액 계산 공식 | 비고 |
|---|---|---|
| 일반 / 쿠팡일반 / 쿠팡로켓 / 11번가 등 | 정산액(원화) − 제조원가 × 수량 | 기본 공식 |
| 쿠팡그로스 | 정산액 − (제조원가×수량) − (그로스배송비×1.1) − (입출고비×1.1) | 배송비/입출고비가 부가세 별도금액으로 들어와 1.1 보정 필요 |
| 아마존(미국/일본) | (정산액 − (제조원가÷1.1×수량)) × 환율 | 마스터 원가가 VAT포함 기준이라 ÷1.1로 공급가 환산 후 계산 |

추가 확인된 특수 규칙: 쿠팡일반 채널은 배송비를 전체(매핑성공/미매핑/예외처리 전체) 합산한 뒤 결과의 첫 행에만 몰아서 표기하는 처리가 있다. 마이그레이션 시 누락하기 쉬운 규칙이므로 별도 테스트 케이스로 관리한다.

기존 C# 코드(FormOnlineSettlementManager.cs)는 채널 구분 없이 '정산액 − 원가×수량'만 적용하고 있어 위 VAT 보정 분기가 빠져 있었다 — MiniERP2에서 정식 반영 대상이다.

### 5.7 기타/문서관리

거래명세표, 견적서, 마감내역서를 정해진 필터링 규칙에 따라 PDF로 출력 — 기존 미구현 항목, 신규 구현

## 6. 개발 단계 및 마일스톤

모듈 단위로 'UI 없이 로직 먼저 검증 → 폼 부착' 순서를 따른다. 각 Phase는 이전 Phase가 끝나야 시작한다.

| Phase | 목표 | 주요 산출물 | 완료 기준 |
|---|---|---|---|
| 0. 프로젝트 셋업 | VS Code 개발 환경 구성 | 새 git 저장소, .csproj, 폴더 구조, NuGet 정리, 다크모드 API 적용 | dotnet build 성공, MainHub 빈 화면 1개 실행 |
| 1. 데이터 계층 | Models/DB/Config 골격 | ChannelConfig 등 모델, SQLite 스키마(신규 이력 테이블 포함) | 단위 테스트로 저장/조회 검증(UI 없음) |
| 2. 공통 컴포넌트 | 2장 공통 UI/UX 구현 | ExcelLikeDataGridView, 내보내기 후처리 다이얼로그, 폴더기억 서비스 | 임의 화면 1개에 적용 후 동작 확인 |
| 3. 마스터데이터 | 마스터SKU/CSKU 관리창 | 5.2절 화면 일체 | 등록 데이터가 DB에 정확히 반영, 변경이력 기록 확인 |
| 4. 매핑 엔진 | 매핑관리창 + 충돌 감지 | 5.3절 엔진 및 화면 | 우선순위 4단계 + 충돌 표시 정상 동작 |
| 5. 채널설정 + JOIN | 채널 설정 창 | 5.4절 화면, 보조소스 JOIN 엔진 | SalesManagerV2 실제 설정값 기준 결과 일치 검증 |
| 6. OFS | 발주~출고 | 5.5절 전체 | 발주 엑셀 입력 → 출고 확정까지 1건 흐름 통과 |
| 7. 마감/이익분석 | 손익 공식 + 마감 대조 | 5.6절 전체 | 채널별 공식 결과가 SalesManagerV2 결과와 일치 |
| 8. 문서관리 + 통합 | PDF 출력, 통합 테스트 | 5.7절, Tests 일체 | 실데이터 기준 합계 일치 검증 |

## 7. 테스트 전략

- 각 로직 클래스는 UI와 분리하여 독립 실행 가능한 형태로 작성
- 채널별 손익 공식(5.6절)은 SalesManagerV2의 실제 결과값과 대조하는 회귀 테스트를 별도로 구성
- 신규 기능(매핑 충돌 감지, 보조소스 JOIN)은 구현 직후 더미 데이터 기준 검증 코드를 함께 작성
- 통합 테스트는 Phase 8에서 실데이터(또는 익명화된 샘플)로 원본 합계 대비 결과 합계 일치 여부를 검증

## 8. 코드 품질 원칙 (기술부채 방지)

- 동일 기능을 수행하는 클래스가 2개 이상 생기지 않도록, 클래스 교체 시 이전 클래스는 즉시 삭제
- 외부 라이브러리는 기능당 1개만 채택(3.2절)
- 색상은 SystemColors/다크모드 API 기준으로만 처리, 하드코딩 금지(2.1절)
- 네임스페이스는 4.0절 표를 벗어나지 않음
- 코드 변경 시 합의된 a/b/c 절차 준수

## 9. 확인이 필요한 사항

- 5.5절(OFS) 중 운송장 매핑/동명이인 조율 등 신규 설계 항목의 상세 우선순위
- 3.2절 라이브러리/드라이버 단일화 방향에 대한 동의 여부
- 데이터 이전 도구(기존 MiniERP V3.2 SQLite → MiniERP2)의 실제 필요 여부

## 10. 향후 확장 검토 — MiniERP3용 (2026-06-28 논의, 현재 버전은 개발 계획 없음)

MiniERP2(현재 버전, WinForms + 로컬 SQLite 단일 사용자 전제)의 다음 세대인 "MiniERP3"에서
검토할 확장 방향. **이 절은 기록 목적으로만 남기며, MiniERP2에서는 착수하지 않는다.**

### 10.1 인트라넷/서버 기반 확장
- 두 가지 경로:
  - **DB만 서버로 이전**(SQLite → PostgreSQL/MSSQL 등): 변경 범위가 작고(Repository 계층의
    connection string만 교체) 여러 PC가 같은 데이터를 공유할 수 있다. 단, 지금은 낙관적 락 등
    동시편집 충돌 처리가 거의 없어 여러 사용자가 같은 화면을 동시에 쓰면 덮어쓰기 위험이 있다.
  - **웹앱 전면 재작성**(ASP.NET Core/Blazor 등): 외부 접속까지 가능한 진짜 온라인 베이스가
    되지만, 지금 있는 20개 이상의 WinForms 화면을 거의 다시 만드는 수준의 작업이다.

### 10.2 이메일 기반 자동 발주처리
- 지정된 이메일로 들어오는 발주서(엑셀 첨부 또는 메일 본문 텍스트)를 자동으로 인식해 발주처리.
- 구조적으로 "항상 켜져 있는 백그라운드 서비스"가 필요하다(사용자가 앱을 켜놨을 때만 동작하면
  안 됨) — 10.1과 무관하게 별도의 Windows 서비스/스케줄 작업이 필요.
- 엑셀 첨부파일은 기존 OFS 채널별 필드매핑 파이프라인을 재사용할 수 있어 비교적 수월하다.
- **메일 본문 텍스트 발주서**는 거래처마다 형식이 달라 채널 필드매핑과 비슷한 "거래처별 텍스트
  패턴 설정"이 필요하고, 정규식/패턴이 안 맞으면 오발주 위험이 있어 가장 불확실성이 큰 부분.

### 10.3 AI 기반 이메일 처리 파이프라인 (10.2의 본문 텍스트 문제를 푸는 대안)
- 흐름: 이메일(본문/첨부) → AI(LLM, 구조화 출력)가 수령인/주소/상품명/옵션/수량 등을 JSON으로
  추출 → MiniERP2(또는 후속 버전)의 기존 발주서 가져오기 파이프라인에 그 결과를 입력 → 이후
  채널 매핑/SKU 매핑은 기존 로직 그대로 재사용.
- 거래처마다 다른 형식을 일일이 설정할 필요가 없고, 엑셀이 다소 지저분해도(헤더 변형 등) AI가
  해석할 수 있어 10.2보다 일반화하기 쉽다.
- **필수 안전장치**: AI 추출 결과를 곧바로 확정 발주처리하면 안 된다 — 지금 있는 "미매핑건 확인"
  화면과 같은 패턴으로, AI가 채운 내용을 사람이 검토·확정하는 단계가 반드시 있어야 한다(오인식된
  주소/수량이 그대로 발주확정되는 사고 방지).
- **리스크**: 거래처 개인정보(주소/연락처 등)가 외부 AI API로 전송됨 — 10.1의 "인트라넷 전용"
  방향과 상충될 수 있고, 거래처와의 개인정보 처리 계약상 검토가 필요. 이메일 1건당 API 호출
  비용도 발생.

### 10.4 문서 역방향 발송(거래명세표/견적서/출고결과를 이메일로 푸시)
- 이미 있는 엑셀 출력 인프라(EPPlus)에 SMTP 발송만 추가하면 되는 수준 — 위 항목들보다 단순하고
  리스크가 적다. 5.7절(문서관리, PDF 출력)과 함께 검토 가능.

### 10.5 권장 착수 순서(MiniERP3 시점에)
10.4(발송) → 10.2(엑셀첨부 자동처리) → 10.3(AI 기반 본문 텍스트 처리) → 10.1(서버화). 서버화를
먼저 하면 가장 가치가 큰 이메일 자동화가 늦어지고, 10.2/10.4는 현재 데이터 구조를 그대로 둔 채
"서버 측에 작은 서비스 하나 추가"로 시작할 수 있기 때문.
