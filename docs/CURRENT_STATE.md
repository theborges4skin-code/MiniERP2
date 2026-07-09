# MiniERP2 현황 정리 (as of 2026-07-09)

> 이 문서의 목적: **현재 코드베이스를 실제로 읽고 정리한 "있는 그대로"의 구조/기능 문서**다. `docs/PLAN.md`(2026-06-26 작성, v0.2)는 개발 착수 전 기획서라 지금은 크게 낡았고, `docs/HANDOFF.md`는 시간순 작업일지라 구조 파악에는 부적합하다. 이 문서는 그 둘을 대체하는 것이 아니라, **다음에 작성할 "수정기획안" MD의 사실관계 기반(base)**으로 쓰기 위해 신규 작성했다. AI가 읽고 바로 다음 작업을 설계할 수 있도록 클래스명/테이블명/메서드명을 정확히 인용했다.
>
> 작성 방법: 코드베이스 3개 영역(Forms/UI, Database/Models, 비즈니스 로직·Utils)을 각각 전담 조사한 뒤 통합. 보조로 Notion의 QA 체크리스트 2건(2026-06-26, 2026-07-07)과 구조분석 문서(OFS 흐름도, 2026-06-29)를 참고했으며, 이 문서들과 실제 코드가 어긋나는 부분은 **코드를 신뢰**하고 각주로 표시했다.

---

## 0. 프로젝트 성격 요약

- 개인/소수 사용자 전제의 **로컬 단일 사용자 WinForms 데스크톱 앱**. 서버/웹 요소 없음(향후 확장안은 `PLAN.md` §10 "MiniERP3" 절에 별도 보류).
- 목적: 여러 온라인/오프라인 판매채널의 **발주 처리(OFS) → SKU 매핑 → 정산/이익분석 → 마감 → 문서관리**를 하나의 앱에서 처리. 레거시 자산(구버전 C# MiniERP V3, Python 기반 SalesManagerV2)의 실사용 로직을 이식해온 이력이 있다.
- **커밋 118개**(2026-06-26 최초 커밋 ~ 현재), 테스트 262개 중 260개 통과(무관한 CFS 테스트 2개 pre-existing 실패, 아래 §8 참고).

### 0.1 기술 스택
- .NET 10, C# 14, WinForms(`WinExe`), `net10.0-windows`, `Nullable`/`ImplicitUsings` 활성.
- DB: SQLite 단일 파일(`ERP_Database.sqlite`), 드라이버 `Microsoft.Data.Sqlite` 10.0.9(+`SQLitePCLRaw.bundle_e_sqlite3`).
- 엑셀: `EPPlus` 8.6.1 단일화(계획대로 ClosedXML 제거됨). 구형 `.xls`는 `ExcelDataReader` 3.7.0으로 변환 후 EPPlus로 취급. CSV는 자체 파서(`CsvWorkbookReader`, UTF-8/CP949 자동판별).
- 다크모드: `Application.SetColorMode(SystemColorMode.System)`(계획대로 적용).
- NuGet 그 외: `System.Text.Encoding.CodePages`(CP949/EUC-KR 지원용).

### 0.2 폴더/네임스페이스 구조 (실측, `PLAN.md` §4.0과 대체로 일치하되 확장됨)

| 폴더 | 네임스페이스 | 역할 |
|---|---|---|
| `/Forms` | `MiniERP2.Forms` (일부 `MiniERP2.Exporters`, `MiniERP2.UI`) | 전체 화면(약 45개 .cs 파일) |
| `/Models` | `MiniERP2.Models` | DTO/설정/Enum 모델(약 40개 파일) |
| `/Database` | `MiniERP2.Database` | SQLite 접근 계층(Repository) + 레거시 마이그레이션 서비스 + 백업 서비스 |
| `/DataLoaders` | `MiniERP2.DataLoaders` | 엑셀/CSV 로드 + 표준화 + 표준필드 매핑 |
| `/Mapping` | `MiniERP2.Mapping` | SKU/광고 매핑 엔진, 조건 평가기, 충돌 감지, 손익 계산 |
| `/Migration` | `MiniERP2.Migration` | 레거시 거래명세표(엑셀) → DB 마이그레이션 파서/커밋 서비스(이번 세션 신규) |
| `/Utils` | `MiniERP2.Utils` | 엑셀 출력, 파일 열기, 택배사 필드 처리 등 공통 유틸(18개 파일) |
| `/Config` | `MiniERP2.Config` | JSON 설정 파일 입출력(채널설정/경로/그리드레이아웃/윈도우위치 등) |
| `/Controls` | `MiniERP2.Controls` | 공통 커스텀 컨트롤(`ExcelLikeDataGridView` 등 3종) |
| `/DataManagement` | `MiniERP2.DataManagement` | 데이터관리창의 DataTable 스테이징 어댑터 |
| `/Services` | (미상, 단일 파일) | `ClosingOrchestrator`(월별 마감 자동화 파이프라인) |
| `/Tests` | `MiniERP2.Tests` | MSTest 단위 테스트(csproj에서 메인 빌드 Compile 대상에서 제외) |

---

## 1. DB 스키마 (SQLite, `Database/DbSchema.cs`)

`DbSchema.EnsureCreated(connection)`가 앱 시작 시(정확히는 `SqliteConnectionFactory.OpenConnection()` 최초 호출 시, DB 경로별 1회만) 전체 `CREATE TABLE IF NOT EXISTS`를 실행하고, 그 뒤 `EnsureColumn(table, col, type)` 호출들로 과거 버전 DB 파일에 없는 컬럼을 보강한다. 구조적 마이그레이션(스키마 자체가 바뀐 경우) 2건도 여기서 처리: `ChannelSkuTable`의 PK를 `(ChannelCode, Msku)`→`(ChannelCode, CskuCode)`로 전환(레거시 테이블은 `ChannelSkuTable_Legacy`로 백업), `OutboundDetailTable`의 유니크 인덱스를 `(OrderNo, MskuCode)`→`(ShipmentGroupKey, MskuCode)`로 전환(분리배송 지원). 서비스 상태값 정규화(`발송대기`/`발송완료` → `발주확정`/`출고확정`)도 기동 시마다 실행(멱등).

### 1.1 마스터데이터
| 테이블 | 핵심 컬럼 | 비고 |
|---|---|---|
| `ItemTable` | `Sku`(PK) TEXT, `ItemName` TEXT NOT NULL, `CostPrice` REAL NOT NULL, `Reserve1~3` TEXT, `ProductGroup` TEXT | 마스터SKU. Reserve1-3/ProductGroup은 후속 추가(`EnsureColumn`) |
| `ItemCostHistory` | `Id` PK, `Sku`, `OldCost`, `NewCost`, `ChangedAt` | 원가 변경 이력 |
| `ChannelSkuTable` | PK `(ChannelCode, CskuCode)`, `Msku`, `SupplyPrice` REAL, `InvoiceDisplayName` TEXT | CSKU(채널별 SKU). PK가 과거 `(ChannelCode, Msku)`에서 마이그레이션됨(한 마스터SKU가 채널 옵션별로 여러 CSKU로 분화 가능) |
| `ChannelSkuPriceHistory` | `Id` PK, `ChannelCode`, `Msku`(실제로는 CskuCode 저장, 컬럼명은 하위호환 유지), `OldPrice`, `NewPrice`, `ChangedAt` | CSKU 납품가 변경 이력 |
| `SalesChannelTable` | `ChannelCode`(PK), `ChannelName`, `GroupName`, `IsFavorite`, `DisplayOrder`, `LastUsedDate`(후속 추가) | 채널 마스터(UI 트리뷰용) |
| `CourierMasterTable` | `CourierName`(PK), `HeaderMappingJson`, `TrackingImportHeaderRow`/`TrackingImportRecipientHeader`/`TrackingImportTrackingNoHeader`/`QuantityNotationFormat`(전부 후속 추가) | 택배사 양식 마스터 |

### 1.2 매핑 규칙(발주 SKU 매핑)
| 테이블 | 핵심 컬럼 | 비고 |
|---|---|---|
| `RuleExact` | `Id` PK, `ChannelCode`, `Key`, `TargetSku` | 1:1 매핑 |
| `RuleTemp` | 〃 | 임시 매핑 |
| `RuleException` | 〃 | 예외처리(TargetSku=`[EXCLUDED]`) |
| `RuleCondition` | `Id` PK, `ChannelCode`, `Key`, `TargetSku`, `TargetMsku`(후속 추가, CSKU 없이 MSKU만 매핑하는 정산 전용 규칙용) | 조건부 매핑 헤더 |
| `RuleConditionDetail` | `Id` PK, `RuleId`, `HeaderField`, `Operator`, `TargetValue`, `Logic` | 조건부 규칙의 AND/OR 세부조건(다대일) |
| `MappingHistory` | `Id` PK, `ChannelCode`, `Key`, `OldSku`, `NewSku`, `MatchType`, `ChangedAt` | **스키마+모델만 존재, 읽거나 쓰는 Repository 코드가 전혀 없음(사실상 미사용 테이블)** |

### 1.3 광고비 매핑 규칙 (SKU 매핑과 병렬 구조, 대상이 "상품그룹")
| 테이블 | 핵심 컬럼 |
|---|---|
| `AdRuleTemp` | `Id`, `ChannelCode`, `Key`, `TargetGroup` |
| `AdRuleCondition` | `Id`, `ChannelCode`, `Key`, `TargetGroup` |
| `AdRuleConditionDetail` | `Id`, `RuleId`, `HeaderField`, `Operator`, `TargetValue`, `Logic` |
| `AdRuleException` | `Id`, `ChannelCode`, `HeaderField`, `Operator`, `TargetValue` |

*(광고 매핑엔 1:1 단계가 없음 — 예외 > 임시 > 조건부만 존재)*

### 1.4 정산/발주·출고/마감
| 테이블 | 핵심 컬럼 | 비고 |
|---|---|---|
| `SettlementData` | `Id` PK, `ChannelCode`, `ProductName`, `OptionName`, `Msku`, `Qty` INT, `Settlement`/`Shipping`/`Fee`/`Profit` REAL, `Status` | 이익분석 결과. **C# 모델엔 Revenue/TrackingNo/OrderNo/TaxNo/TaxDate/EventType/ProductGroup/RawValues 필드가 더 있지만 DB엔 저장 안 됨(메모리 전용)** |
| `OutboundDetailTable` | `Id` PK, `ChannelCode`, `OrderNo`, `ShipmentGroupKey`(후속 추가, PK 마이그레이션됨), `TrackingNo`, `MskuCode`, `Qty`, `SupplyPrice`, `CreatedAt`, `Status`(기본값 정규화됨), `ConfirmedAt`/`Recipient`/`Address`/`ProductName`(전부 후속 추가) | 발주확정/출고확정 이력. UNIQUE INDEX `(ShipmentGroupKey, MskuCode)` |
| `ClosingRun` | `Id`, `FolderPath`, `Period`("YYYY-MM"), `Status`(draft\|confirmed), `CreatedAt`, `UpdatedAt` | 월별 마감 자동화 실행(run) |
| `ClosingStagedFile` | `Id`, `RunId`, `ChannelCode`, `ChannelName`, `SourceType`(settlement\|ad), `OriginalPath`, `FileCreatedAt`, `Status`(pending\|processed\|error\|skipped), `RowCount`, `UnmappedCount`, `ErrorMessage` | 마감 실행에 포함된 파일 |
| `ClosingUnmapped` | `Id`, `RunId`, `ChannelCode`, `SourceKey`("상품명\|옵션명"), `OccurrenceCount`, `SampleAmount`, `MappedSku` | 마감 실행 단위의 미매핑 큐 |
| `ProfitFactTable` | `Id`, `Period`, `ChannelCode`, `ChannelName`, `ProductGroup`, `Qty`, `Revenue`, `GrossProfit`, `SavedAt` | 종합보고서용 이익 집계 팩트 |
| `AdFactTable` | `Id`, `Period`, `ChannelCode`, `ChannelName`, `ProductGroup`, `AdCost`, `SavedAt` | 종합보고서용 광고비 집계 팩트 |

### 1.5 기타
| 테이블 | 핵심 컬럼 | 비고 |
|---|---|---|
| `ExportLogTable` | `Id`, `ExportedAt`, `TableName`, `FilePath`, `RowCount`, `Headers` | 데이터관리창 엑셀 내보내기 로그 |
| `ExportSummaryDraftEntry` | `Id`, `MarketCode`, `YearMonth`, `Indicator`, `Currency`, `Amount`, `SavedAt` | 수출요약보고서 수동입력 임시저장 |

### 1.6 문서관리(Docs) 관련 — 오늘 세션 대부분 신규/확장
| 테이블 | 핵심 컬럼 | 비고 |
|---|---|---|
| `DocFavoritePhraseTable` | `Id`, `Title`, `Body`, `Category`(기본 '일반'), `IsFavorite` | 가격조정 공문 등의 상투 문구 |
| `DocPartyTable` | `Id`, `ProfileName`, `RegNo`, `CompanyName`, `CeoName`, `Address`, `BizType`, `BizItem`, `Tel`, `Email`, `IsDefaultSupplier`, `ChannelCode`(후속 추가, 채널 연결), `IsActive`(후속 추가, **ChannelCode 존재 여부로 자동 계산되는 값 — 수동 토글 아님**), `CreatedAt`(후속 추가) | 거래처(공급자/공급받는자) 프로필. 신규 거래처 테이블을 안 만들고 이 테이블을 레거시 이관·채널연결·수기입력 전부에서 공유 |
| `DocStatementTable` | `Id`, `PartyId`(FK→DocPartyTable), `IssueDate`, `IssueYearMonth`, `TotalSupply`/`TotalTax`/`TotalAmount`/`TotalQty`/`CarryoverBalance` REAL, `ReconcileNote`, `TemplateSignature`, `StatusFlags`, `SourceFileName`, `SourceSheetName`, `CreatedAt`. UNIQUE(SourceFileName, SourceSheetName) | 레거시 엑셀 거래명세표 마이그레이션 결과(발행건 헤더). **합계는 계산이 아니라 저장된 원본값 그대로 보존**(재계산하면 과거 원본과 달라질 위험 — 모델 주석에 명시) |
| `DocStatementLineTable` | `Id`, `StatementId`(FK), `RowNo`, `LineDate`, `ItemName`, `Spec`, `Qty`, `UnitPrice`, `UnitPriceVatIncluded`, `SupplyAmount`, `Tax`, `Total`, `Note` | 위 발행건의 라인 |
| `DocHistoryTable` | `Id`, `DocType`, `IssueDate`, `BuyerName`, `TotalAmount`, `FilePath`, `CreatedAt` | DocsForm에서 실제 생성한 문서(6종)의 발행 이력 로그 |

**중요 설계 원칙(§2.1 활성/비활성 모델)**: `DocPartyTable.IsActive`는 `DocPartyRepository.Save()`가 `ChannelCode` 존재 여부로 자동 계산한다(레거시 마이그레이션으로 들어온 거래처는 비활성, 채널설정에서 채널과 연결되면 자동 활성). `DocStatementTable`/`DocStatementLineTable`에는 활성 플래그를 복제하지 않고, 소비하는 기능이 `DocParty.IsActive`로 조인 필터링하는 설계다.

---

## 2. Repository 계층 (`Database/*.cs`)

모든 Repository는 호출마다 `SqliteConnectionFactory.OpenConnection()`으로 새 연결을 열고(DB 경로별 스키마 보장은 프로세스당 1회 캐시), 다중 쓰기는 `SqliteTransaction`으로 묶는다. 공유 DbContext/커넥션 풀 패턴은 없음.

| Repository | 관리 테이블 | 비고 |
|---|---|---|
| `ItemRepository` | ItemTable, ItemCostHistory | |
| `ChannelSkuRepository` | ChannelSkuTable, ChannelSkuPriceHistory | |
| `SalesChannelRepository` | SalesChannelTable | |
| `CourierRepository` | CourierMasterTable | |
| `SettlementRepository` | SettlementData | |
| `OutboundRepository` | OutboundDetailTable | `GetTopCskusByChannel` 등 OFS 수동주문 빠른입력용 조회 포함 |
| `MappingRepository` | RuleExact/Temp/Exception/Condition/ConditionDetail | `SaveRules(Condition,...)`는 **채널 전체 조건부 규칙을 삭제 후 재삽입**하는 방식(하단 §9 위험요소 참고) |
| `AdMappingRepository` | AdRuleTemp/Condition/ConditionDetail/Exception | |
| `ExportLogRepository` | ExportLogTable | |
| `ProfitFactRepository` | ProfitFactTable, AdFactTable | |
| `ClosingRunRepository` | ClosingRun, ClosingStagedFile, ClosingUnmapped | |
| `ExportSummaryDraftRepository` | ExportSummaryDraftEntry | |
| `DocFavoritePhraseRepository` | DocFavoritePhraseTable | |
| `DocPartyRepository` | DocPartyTable | `FindByRegNo`(활성/비활성 전체 대상), `GetChannelLinkedAll` 등 |
| `DocStatementRepository` | DocStatementTable, DocStatementLineTable | `Upsert`는 `(SourceFileName,SourceSheetName)` 재실행 시 삭제 후 재삽입(replace 정책) |
| `DocHistoryRepository` | DocHistoryTable | |

**DB 파일이 아닌 별도 유틸리티(Database/ 폴더에 있지만 Repository는 아님)**:
- `SqliteConnectionFactory` — 연결 오픈 + 스키마 보장 캐시.
- `DbBackupService` — 전체 DB 파일 백업/복원(최근 3개 보관).
- `LegacyMigrationService` — 구버전 C# MiniERP V3 SQLite → 현재 스키마 이관(채널/아이템/CSKU/매핑규칙 4종).
- `SalesChannelLegacyMigrationService` — Python SalesManagerV2의 `channels_config.json` → SalesChannelTable + ChannelConfig JSON 이관.
- `AdLegacyMigrationService` — Python SalesManagerV2의 광고매핑 JSON(`ad_condition_rules.json` 등) 이관.

---

## 3. Models 계층 (`Models/*.cs`, 약 40개)

전부 나열하지 않고 카테고리별 대표만 정리(전체 목록은 탐색 로그 참고 가능):

- **DB 테이블 대응 모델**: `ItemModel`, `ItemCostHistory`, `ChannelSkuModel`, `ChannelSkuPriceHistoryModel`, `SalesChannel`, `CourierMaster`, `SettlementData`, `OutboundDetail`, `MappingRule`, `MappingConditionDetail`, `AdMappingRule`, `AdConditionDetail`, `AdExceptionRule`, `ExportLogEntry`, `ProfitFactRow`/`AdFactRow`, `ClosingRun`/`ClosingStagedFile`/`ClosingUnmappedItem`, `DocParty`, `DocStatement`/`DocStatementLine`, `DocFavoritePhrase`, `DocHistoryRecord`.
- **JSON 영속 설정 모델(DB 테이블 아님)**: `ChannelConfig`(채널별 필드매핑/광고레이아웃/보조소스/CFS설정 등 최대 규모 설정 객체), `GrowthAuxSource`, `FieldMapping`, `CourierHeaderOverride`, `AdFileLayout`, `GrowthCfsFeeConfig`, `GridColumnLayout`, `WindowBounds`, `ExportSummaryConfig`(+ 하위 `ExportSummaryMarket`/`DeclarationTrackConfig`/`SalesMarketMapping`/`RemittanceTrackConfig`/`RemittanceRule`).
- **Enum**: `MappingRuleType`, `PostExportAction`, `ConditionOperator`, `ConditionLogic`, `AdConditionOperator`, `ChannelType`(+ `IsMarketplace`/`ToKoreanLabel` 확장 — General/CoupangGeneral/CoupangRocket/ElevenStreet/CoupangGrowth/AmazonUs/AmazonJp/Partner/Other), `StdField`(발주/정산 표준필드), `AdStdField`, `DocType`(6종).
- **메모리 전용 작업 모델**: `OfsOrderItem`(OFS 그리드 행), `AdSpendItem`, `ProfitGroupSummary`, `TradeStatementDoc`/`QuoteDoc`/`PriceAdjDoc`/`SalesLedgerDoc`(+ 각 LineItem) — DocsForm이 "지금 작성 중"인 문서용, `DocStatement`류(이관된 과거 이력)와는 별개.
- **죽은 코드로 보이는 모델**(사용처 없음, 후속 리팩터링 후보): `Models/ChannelSku.cs`(구 CSKU 모델, `ChannelSkuModel`로 대체됨), `Models/ChannelSkuPriceHistory.cs`(구 이력 모델, `ChannelSkuPriceHistoryModel`로 대체됨), `Models/MappingHistory.cs`(테이블은 있으나 Repository 없음).
- **주의**: `Models/FormManager.cs`는 폴더는 Models지만 실제 네임스페이스는 `MiniERP2.UI`이고 데이터 모델이 아니라 창 단일화(`Show<T>()`)+위치기억 담당 정적 클래스.

---

## 4. 화면(Forms) 목록

### 4.0 MainHub 진입점 전체 (`Forms/MainHub.cs`)

좌측 사이드바 버튼 14개, 전부 `FormManager.Show<T>()`(같은 타입 창 재사용) 패턴. `CreateMenuButton`이 내부적으로 `Enabled=false`를 세팅하는 죽은 코드가 남아있지만 직후 전부 `true`로 재설정되어 **14개 전부 활성 상태**.

1. OFS(발주처리) → `OfsForm`
2. 마스터SKU 관리 → `MasterSkuForm`
3. 매핑 관리 → `MappingForm`
4. 채널 설정 → `ChannelConfigForm`
5. 마감/이익분석 → `SettlementForm`
6. 광고비 분석 → `AdMappingForm`
7. 종합보고서 → `ReportForm`
8. 월별 마감 자동화 → `MonthlyClosingForm`
9. 발주/출고 이력 → `OutboundHistoryForm`
10. 수출요약보고서 → `ExportSummaryForm`
11. 기타/문서관리 → `DocsForm`
12. 거래명세표 조회/내보내기 → `DocStatementBrowserForm`
13. 레거시 데이터 가져오기 → (FormManager 아님) `OnLegacyImportClick`이 직접 `OpenFileDialog`+`LegacyMigrationService.Migrate()` 호출
14. 데이터 관리 → `DataManagementForm`

### 4.1 OFS 발주처리
- **`OfsForm.cs`** — 발주 파일 로드→채널선택→SKU매핑→그리드편집(합포장/분리배송, undo 5단계)→저장(발주확정)→택배사 양식 출력까지의 핵심 화면. 실제 택배사 헤더 그대로 보여주는 편집 가능한 미리보기 패널 보유(다른 화면에 없는 기능).
- `ManualOrderDialog` / `ManualOrderQuantityDialog` — 수동 주문 추가(추가모드/교체모드), 최근 CSKU 원클릭.
- `CumulativeOrderSelectionDialog` — 누적발주서 채널의 최근 N일 필터 선택.
- `SelectChannelDialog` — 발주파일 채널 선택(OFS/SettlementForm 공용), "신규 채널 바로 추가" 진입점 포함.
- `SelectCourierDialog` — 택배사 선택(OFS/OutboundHistoryForm 공용).
- `OrderSkuMappingDialog` — 미매핑 1건 마스터DB 검색 매핑 도우미(OFS/SettlementForm 공용).

### 4.2 마스터SKU/CSKU
- `MasterSkuForm` — 마스터SKU CRUD, 엑셀 가져오기/내보내기, 행 더블클릭→CSkuForm.
- `CSkuForm` — 특정 마스터SKU의 채널별 CSKU(납품가/송장표시명) 관리.
- `CostHistoryForm` / `ChannelSkuPriceHistoryForm` — 각각 원가/납품가 변경 이력 읽기전용 뷰.
- `MasterSkuImportMappingDialog` — 마스터SKU 엑셀 가져오기 시 시트/헤더행/열 지정.

### 4.3 매핑관리
- **`MappingForm.cs`**(2173줄, 최대 파일) — 채널별 1:1/예외/임시/조건부 탭 + "전체 규칙 관리"(4종 통합+CSKU 조인) + "조건부 매핑(상세)"(다중 AND/OR 편집) + "미매핑 처리" 탭.
- `ConditionRuleListForm` — 전체 조건부 규칙 목록, 중복 규칙 강조+병합.

### 4.4 채널설정
- **`ChannelConfigForm.cs`**(1507줄) — 채널(그룹 트리뷰)별 발주서/정산서/광고 필드매핑, 거래처(공급받는자) 정보, CFS 수수료 설정 등 관리하는 최대급 설정 화면.
- `AddChannelDialog` / `MoveChannelToGroupDialog` / `FieldMappingHelpDialog` — 보조 다이얼로그.
- `CourierConfigForm` — 택배사양식 관리(채널설정과 독립적).

### 4.5 마감/이익분석
- **`SettlementForm.cs`**(1648줄) — "이익분석(자동)"/"마감 대조(수기)" 2탭. 정산파일 로드시 `SelectChannelDialog`→`LoadingProgressDialog`(비모달 진행창).
- `PeriodInputDialog` — 보고서 저장 기간(YYYY-MM) 입력.
- `TextPromptDialog` — 범용 텍스트 입력(현재 SettlementForm 1곳만 사용).
- `LoadingProgressDialog` — 비모달 진행 안내.

### 4.6 데이터관리
- **`DataManagementForm.cs`**(965줄) — 마스터SKU/CSKU/매핑규칙 스테이징 관리, 저장 직전 자동 DB 백업(최근 3개), "레거시 가져오기" 탭(SQLite/채널설정/광고매핑/**거래명세표 마이그레이션** 4개 진입점).
- `ExportColumnSelectionDialog` — 엑셀 내보내기 열/필터 선택.

### 4.7 문서관리(DocsForm) 및 관련
- **`DocsForm.cs`**(1389줄) — 문서 유형 **6종**: 거래명세표(VAT별도/포함), 견적서(기본/수량포함), 가격조정 공문, **매출장(내부용, SalesLedger)**. 거래처 불러오기(`PartyManagerForm`/`ChannelPartySelectDialog`/`PartySelectDialog`), CSKU 라인 채우기(`CskuPickerDialog`), 즐겨찾기 문구(`FavoritePhraseDialog`), 발행 이력(`DocHistoryForm`).
- `PartyManagerForm`(+내부 `PartyEditDialog`) — 거래처 프로필 목록 CRUD.
- `CskuPickerDialog` — 채널/검색어로 CSKU를 찾아 문서 라인에 채움.
- `DocHistoryForm` — 발행 이력 조회(기간/문서종류)+파일 열기/삭제/재내보내기.

### 4.8 거래명세표 마이그레이션(이번 세션 신규, 3단계 전부 완료)
- `TradeStatementMigrationDialog` — 레거시 엑셀(수백 시트 규모) 스캔→검토(체크박스)→커밋 3단계 1회성 이관 창. `DataManagementForm`의 "레거시 가져오기" 탭에서 진입. 이 앱에서 유일하게 "확인창 없이 즉시 커밋"하지 않는 레거시 가져오기 기능.
- `DocStatementBrowserForm` — 이관된 과거 거래명세표를 거래처/기간으로 조회하고 사내 재현 양식으로 재내보내기하는 상시 화면(마이그레이션 다이얼로그는 1회용, 이 폼은 상시용).

### 4.9 광고매핑
- **`AdMappingForm.cs`**(1015줄) — 상품그룹 단위 매핑(1:1 단계 없음, 예외>임시>조건부). 필드매핑 탭에서 채널별 광고 리포트 헤더 설정.
- `AdFactPeriodInputDialog` / `AdTargetGroupPromptDialog` — 보조 입력창.

### 4.10 발주/출고이력
- **`OutboundHistoryForm.cs`**(714줄) — 발주/출고 이력 조회, 운송장 결과 파일 수령인 매칭, 셀 편집 후 "변경사항 저장" 필요, 미출력 건 재출력.
- `TrackingMatchPickerDialog` — 동일 수령인 다중 매칭 시 선택.
- `Forms/CourierExporter.cs` — **Form 아님**, 실제로는 `namespace MiniERP2.Exporters`의 택배사 양식 출력 헬퍼(OFS/OutboundHistoryForm 공용).

### 4.11 종합보고서 / 수출요약보고서
- `ReportForm`(748줄) — ProfitFactTable/AdFactTable 기반 기간×채널×상품그룹 피벗 + 지표 7종 + 엑셀 출력(2개월 이상이면 "월별시계열" 시트 추가).
- `ExportSummaryForm`(639줄) — 수출신고/판매/송금 3트랙 **독립 집계**(서로 대사하지 않는 설계). `SalesFileLoaderDialog`(마켓별 판매파일 로드), `ExportSummaryManualEntryDialog`(수동입력 편집기).

### 4.12 월별 마감 자동화
- `MonthlyClosingForm`(436줄) — 폴더+기간→스캔(채널 자동탐지)→수동지정→`ClosingOrchestrator` 처리→미매핑 큐.
- `UnmappedQueueForm` — 마감 후 미매핑 항목 SKU 연결+채널 재계산.

### 4.13 공용 소형 다이얼로그
- `PasswordPromptDialog`(OFS/SettlementForm/AdMappingForm 공용), `PostExportDialog`(엑셀 내보내기 후 파일/폴더 열기 공통 다이얼로그, `ExportHelper` 경유).

---

## 5. 비즈니스 로직 계층

### 5.1 DataLoaders (엑셀/CSV → 표준 모델)
- `OrderLoader` — 채널의 `OrderFieldMappings`로 발주파일 로드+`SkuMapper.ApplyMapping` 즉시 적용. 헤더행 불일치 경고 플래그(`LastLoadHeaderRowLooksEmpty`) 보유.
- `SettlementLoader` — 정산파일 로드(xlsx/csv). 헤더행 자동탐지(`HeaderRowDetectionColumn`, 아마존 등), "보조소스"(GrowthAuxSource) JOIN(`GrowthAuxJoinEngine` 경유), 쿠팡그로스 CFS 모드일 땐 보조소스 JOIN 생략(중복 방지), 200연속빈행 가드. 로드 후 채널별 후처리(쿠팡로켓 소계행 제거, 11번가 배송비 보정, 쿠팡일반 배송비 집계, 아마존 Transfer행 제거)는 `ProfitCalculator`에 위임. `ApplyMappingAndProfit`(단일 행 재매핑, 캐시로 벌크 재매핑 성능 확보).
- `AdSpendLoader` — 광고비 파일 로드. `DetectLayout`으로 채널의 여러 `AdFileLayout` 후보 중 자동 판별. CSV UTF-8/CP949 폴백.
- `CfsFeeLoader` — 쿠팡그로스 CFS(입출고비/배송비) 파일 로드, 옵션ID 기준 다중파일 합산, ×1.1(VAT 포함 환산).
- `ExportSummaryLoader` — 수출요약보고서 3트랙(신고/판매/송금) 로더, 트랙 간 의도적 비대사 설계.

### 5.2 Mapping (SKU/광고 매핑 엔진)
- **`SkuMapper`** — 우선순위 **예외(Contains) > 1:1(Exact) > 임시(Temp) > 조건부(Condition)**, 코드로 재확인 완료. 예외 대상값 `[EXCLUDED]`는 매핑 제외 처리. 매핑 성공 시 CSKU의 `InvoiceDisplayName` 우선 적용.
- `ConditionEvaluator` — AND/OR 다중조건 평가(연산자: Contains/NotContains/Equals).
- `AdConditionEvaluator`/`AdMappingEngine` — 광고비 전용(연산자가 더 많음: 수치 비교 포함). **우선순위: 예외 > 임시 > 조건부, 1:1 단계 없음.**
- `GrowthAuxJoinEngine` — 보조시트 JOIN 순수 로직.
- `MappingConflictDetector` — 동일 우선순위 규칙 간 충돌(다른 SKU를 가리킴) 감지.
- **`ProfitCalculator`** — 채널유형별 이익 공식(§6에 상세) + 채널별 행 필터/집계 정적 메서드 모음.

### 5.3 Migration (이번 세션 신규 — 거래명세표 레거시 이관)
- `TradeStatementSheetParser` — 라벨 앵커 기반 파서(좌표 하드코딩 없음). "품목" 라벨로 헤더행 탐지(1~20행), 라벨 별칭으로 컬럼 매핑, "공급받는자" 블록 추출, VAT 파생, 총계행 대조, 노이즈/사본/폐기 시트 플래그. DB에 아무것도 쓰지 않는 순수 파서.
- `LegacyStatementCommitService` — 파싱 결과를 실제 DB에 커밋. 거래처 식별: 등록번호 있으면 기존 `DocPartyTable`(활성 포함) 전체에서 매칭·재사용, 없으면(익명) 이름 기반 병합 없이 개별 비활성 레코드로 적재(단, 같은 시트 재커밋 시엔 이전에 만든 거래처를 재사용해 고아 레코드가 안 쌓이도록 처리).
- `LegacyStatementModels` — 파싱 결과 DTO(`ParsedStatementSheet`/`ParsedPartyInfo`/`ParsedStatementLine`).

### 5.4 Utils (18개 파일)
발주코드/임시SKU 생성기(`ChannelCodeGenerator`/`TempSkuGenerator`/`CskuCodeGenerator`), 엑셀 I/O 공통(`ExcelFileOpener`/`XlsWorkbookReader`/`CsvWorkbookReader`/`ExcelLicense`/`ExportHelper`), 문서 출력(`DocumentExporter` — 거래명세표/견적서/가격조정공문/매출장/레거시재현 5종 출력 로직), 택배사 필드 해석(`CourierFieldResolver`/`CourierHeaderMapping`), 그리드 정렬(`GridSorter`), 진단로그(`DiagnosticsLogger`), 배송그룹핑(`ShipmentGrouping`/`ShipmentCountEstimator`), 메타시트(`MetaSheetHelper`, 마감자동화 파이프라인의 파일-채널 자동인식용), 정산행 상태분류(`SettlementRowStatus`), 조건규칙 중복판정(`ConditionRuleSignature`).

### 5.5 Config (7개 파일)
전부 JSON 파일 입출력 서비스: `ChannelConfigService`(channels_config.json), `ExportSummaryConfigService`(export_summary_config.json), `GridSettingsService`(grid_layouts.json), `SplitterSettingsService`(splitter_layouts.json), `WindowBoundsService`(window_bounds.json), `SettingsService`(settings.json, 마지막 폴더 기억), `PathProvider`(전체 경로 중앙관리, `AppDataFolder` 기준 — 테스트에서 격리용으로 오버라이드하는 지점).

### 5.6 Services / Controls / DataManagement
- `Services/ClosingOrchestrator` — 월별 마감 자동화 파이프라인(폴더 스캔→채널 자동탐지→`SettlementLoader` 처리→미매핑 큐 집계→`ProfitFactRow` 저장→재계산).
- `Controls/ExcelLikeDataGridView` — 열 레이아웃 기억, 헤더클릭 정렬(BindingList 한계 우회), Ctrl+V 붙여넣기, 우클릭 메뉴에 폼의 모든 버튼 자동 노출("이 창의 기능").
- `Controls/PersistentSplitContainer` — 분할선 위치 기억.
- `Controls/QuickMappingPanel` — OFS/정산 화면에 인라인 삽입되는 조건부 매핑 패널(별도 창 전환 없이 미매핑 1건 처리).
- `DataManagement/*` — `IManagedDataTable` 인터페이스로 마스터SKU/CSKU/단순매핑(1:1·예외·임시)/조건부매핑을 DataTable로 스테이징, `ManagedTableChangeApplier`(Added/Modified/Deleted 적용, 자연키 변경은 delete+insert로 분해), `ManagedTableExcelIO`(공통 엑셀 가져오기/내보내기).

---

## 6. 핵심 비즈니스 로직 — 채널유형별 이익 계산 공식 (`Mapping/ProfitCalculator.cs`)

| 채널유형(`ChannelType`) | 공식 |
|---|---|
| 기본(General 등) | `정산액 − 제조원가×수량` |
| 쿠팡그로스(CoupangGrowth), CFS 모드 아님 | `정산액 − (배송비×1.1) − (수수료×1.1) − 제조원가×수량` |
| 쿠팡그로스, CFS 모드(CfsFeeLoader 사용) | 이미 VAT 포함된 CFS 파일값을 그대로 사용(추가 ×1.1 안 함) |
| 쿠팡로켓(CoupangRocket) | 배송비 ×1.1 반영 |
| 아마존(US/JP) | `(정산액 − 제조원가÷1.1×수량) × 환율` |

채널별 특수 행 처리(전부 `ProfitCalculator`의 정적 메서드, `SettlementLoader`가 로드 후 호출):
- 쿠팡로켓: "소계" 행 제거(`ApplyCoupangRocketFilter`).
- 11번가: 매출액에 배송비가 중복 포함되어 있어 `매출액 = 매출액 − 배송비`로 보정 + 수량0 배송비 행 제거(`ApplyElevenStreetFilter`).
- 쿠팡일반: 수량0 배송비 행을 주문번호 단위로 집계/분배, 주문번호 매핑이 안 되면 첫 행에 전체 배송비를 몰아서 표기(`ApplyCoupangGeneralShippingAggregation`).
- 아마존: `Transfer`(입금) 이벤트 행 제거(`ApplyAmazonTransferFilter`).

**미검증 항목**: 이 공식들은 단위테스트로만 검증되어 있고, `SalesManagerV2`(Python) 실데이터 대비 회귀검증은 미수행(§8 참고, 2026-06-26 체크리스트 이후 갱신 안 됨 — 재확인 필요).

---

## 7. 구현/미구현 목록

### 7.1 확실히 구현 완료(코드로 직접 확인됨)
- MainHub 14개 화면 전체 진입 가능, 창 중복 방지(`FormManager.Show<T>`), 창 크기/위치 기억, 다크모드 자동 대응.
- OFS: 발주 로드→자동매핑→그리드 편집(합포장/분리배송/undo)→택배사 미리보기(실제 헤더+수동입력 필드)→발주확정→출고확정→택배사 양식 출력. 누적발주서 채널 지원. 수동주문(추가/교체 모드, 채널 고정값 자동기입).
- 마스터SKU/CSKU 전체 CRUD + 원가/납품가 변경이력.
- 매핑관리: 4종 규칙(예외/1:1/임시/조건부) + 조건부(상세) 다중AND/OR + 전체규칙관리(중복병합) + 충돌감지.
- 채널설정: 발주서/정산서/광고 필드매핑, 보조소스(GrowthAuxSource) JOIN, CFS 수수료 설정, 거래처 정보 탭, 그룹/즐겨찾기 트리뷰.
- 마감/이익분석: 자동 이익분석(§6 공식) + 수기 마감대조 + 정산 결과 저장(ProfitFactTable).
- 광고비 분석: 5탭(데이터/임시매핑/조건부매핑/예외처리/필드매핑), 다중파일+CSV, 분석결과 내보내기.
- 발주/출고 이력: 조회, 운송장 매칭, 미출력건 재출력.
- 데이터관리창: 4종 관리 테이블 스테이징+백업/롤백(최근3)+레거시가져오기 4종(SQLite/채널설정/광고매핑/거래명세표).
- 종합보고서/수출요약보고서: 피벗+엑셀출력, 3트랙 독립집계.
- 월별 마감 자동화: 스캔→채널자동탐지→일괄처리→미매핑큐.
- **문서관리(DocsForm)**: 거래명세표(VAT별도/포함)·견적서(기본/수량포함)·가격조정공문·**매출장(내부용)** 6종 문서 작성+XLSX 출력, 거래처 프로필 관리(PartyManagerForm)+채널연결, CSKU 라인 자동채움, 즐겨찾기 문구, 발행이력 조회.
- **거래명세표 레거시 마이그레이션**(3단계 전부 완료, 실제 476개 시트로 검증됨): 앵커 파서→스캔/검토/커밋 UI→조회/내보내기 화면.

### 7.2 알려진 미구현 / 제약사항 (Notion QA 체크리스트 + 코드 확인 기반, ⚠️는 재검증 필요)
- **외부 ERP 양식 거래명세표 출력** — 거래명세표 마이그레이션 스펙 §5-B, 대상 ERP 포맷 자체가 미확정이라 착수 안 함.
- **택배송장 4줄 초과 시 수동 줄합침 전용 UI** — 현재 경고만 표시(1단계), 전용 편집 UI는 미구현(2단계 보류 중, 실사용 피드백 대기).
- **이익분석 공식의 SalesManagerV2 실데이터 대비 회귀검증** — 단위테스트만 있고 실데이터 대조 미수행(⚠️ 최신 상태 재확인 필요).
- **쿠팡그로스 CFS 실데이터 검증** — 단위테스트 외 수동 검증 안 됨(⚠️).
- **`MappingRepository.SaveRules(Condition,...)`가 채널 전체 조건부규칙을 삭제 후 재삽입** — "조건부 매핑(상세)" 탭은 별도 저장경로(`UpdateConditionRuleSummary`/`ReplaceConditionDetails`)로 분리되어 이 위험을 피해가지만, 혹시 남아있을 수 있는 단순 "조건부 매핑" 그리드 저장 경로가 있다면 여전히 위험(⚠️ `MappingForm.cs`에 단순 조건부매핑 그리드 탭이 실제로 남아있는지 코드 재확인 필요 — 2026-06-29 노션 분석 시점엔 "제거 권장"이었고 이후 커밋(`0f659a3 매핑폼 3가지 수정 - 조건탭 제거`)이 있어 이미 제거됐을 가능성 높음).
- **`MappingHistory` 테이블** — 스키마+모델만 있고 Repository/사용처 없음. 매핑 변경이력 추적 기능 자체가 미구현 상태(테이블만 선점된 죽은 스키마).
- **`Models/ChannelSku.cs`/`Models/ChannelSkuPriceHistory.cs`** — 구버전 모델, 사용처 없는 죽은 코드(정리 후보).
- **`ExportSummaryConfig`의 TH/TW/VN/BR 마켓 판매파일 헤더** — 원본 기획 문서가 손상되어 읽을 수 없었던 부분이라 리터럴 `"TODO: ..."` placeholder로 남아있음(`Config/ExportSummaryConfigService.cs`).
- **발주서 분리배송 시 발주/출고이력에 분리건 중 1개만 저장되는 버그** — 2026-07-01 Notion 체크리스트에 미해결로 기록(⚠️ 이후 수정 여부 git log로 재확인 필요).
- **투유채널 발주 12건만 출고이력에 표시되는 버그** — 2026-07-09 Notion 체크리스트에 원인 미파악 상태로 최근 기록됨(⚠️ 가장 최근 미해결 항목, 우선 확인 권장).
- **마감대조(수기) 화면 좌측 그리드 합계행 미표시** — 2026-06-29 요청사항, 구현 여부 미확인(⚠️).
- **DB 백업/롤백(최근 3개)의 실제 복원 동작, 레거시 가져오기 3종의 실제 레거시 파일 재현 테스트** — Notion 체크리스트상 "재테스트 필요"로 남아있던 항목(⚠️).

> **주의**: Notion "기능 테스트 체크리스트(2026-06-26)"의 "문서관리(PDF 출력) 전체 미구현 — MainHub의 '기타/문서관리' 버튼은 여전히 빈 핸들러" 항목은 **명백히 최신화되지 않은 오래된 기록**이다. 실제 코드에는 `DocsForm`이 6종 문서 유형으로 완전히 구현되어 있고, PDF가 아니라 XLSX로 출력한다(계획서의 "PDF 출력"이라는 전제 자체가 실제 구현과 다름 — 이 문서를 근거로 향후 기획할 때는 "미구현"이 아니라 "이미 XLSX 기반으로 구현되어 있음, PDF 출력은 애초에 채택 안 됨"으로 정정할 것).

### 7.3 구조적 위험/기술부채 (2026-06-29 OFS 흐름도 분석 기준, 최신 코드 반영 여부 ⚠️로 표시)
- CSKU 정보 저장 로직이 `OrderSkuMappingDialog`/`MappingForm` 2곳에 유사 코드로 중복 구현 — 커밋 `9e95d0a CSKU 저장 로직 통합...`으로 이미 정리된 것으로 보임(⚠️ 완전 통합 여부 재확인).
- 합포장/분리배송 메뉴가 상단 그리드와 하단 미리보기 양쪽에 있어 사용자가 어디를 눌러야 할지 헷갈릴 수 있음(제안: 미리보기 쪽으로 통일) — 미해결로 추정.
- 채널 유형(마켓플레이스 vs 거래처)에 따라 매핑관리창 탭 구성을 다르게 하는 방향은 "향후 검토" 수준으로만 남아있고 미구현.

---

## 8. 테스트/빌드 현황
- `dotnet build`: 오류 0 (경고 다수, 주로 MSTest 분석기 권장사항 + nullable 경고).
- `dotnet test`: 262개 중 260개 통과. 실패 2건은 `Tests/CfsFeeLoaderTests.cs`의 `AccumulateSheet_MultipliesRawByVat`/`LoadAndMerge_SumsAcrossMultipleFiles` — 이번 세션(거래명세표 마이그레이션) 변경과 무관함을 격리 검증 완료(원인 미조사 상태로 남아있음, 별도 확인 필요).
- 테스트는 전부 `Tests/*.cs`(MSTest), 메인 앱 csproj의 Compile 대상에서 명시적으로 제외.

---

## 9. 참고 자료
- Notion: "작업일지"(시간순 개발로그), "기능 테스트 체크리스트 (2026-06-26)"/"(2026-07-07)"(화면별 구현/미구현 QA 체크리스트), "OFS 발주처리 + SKU매핑 흐름도 분석 (2026-06-29)"(구조적 위험요소 분석).
- 레포 내: `docs/PLAN.md`(v0.2, 최초 기획서 — 상당 부분 낡음), `docs/HANDOFF.md`(2026-06-27~28 세션 시간순 로그).
- `C:\Users\thebo\Desktop\거래명세표_DB이식_개발스펙.md` — 거래명세표 마이그레이션 전용 스펙 문서(레포 밖, git 미포함).
