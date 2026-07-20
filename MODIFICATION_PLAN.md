# MiniERP2 수정 기획서 (개발스펙, as of 2026-07-10, 2026-07-10 정정판)

> **목적**: `CURRENT_STATE.md`(있으면 그대로의 구조 문서)를 사실 기반(base)으로 삼아, 이번에 착수할 **리팩토링·버그수정·신규기능**을 AI가 읽고 바로 설계·구현할 수 있는 개발스펙으로 정리한다. 모든 항목은 실제 코드베이스를 직접 읽고 **파일명·라인번호·클래스/메서드명을 인용**한다.
>
> **정정 이력**: 2026-07-10 최초 작성분을 코드 직접 대조 검증(grep/Read 전수조사 + 라이브 DB 진단 쿼리)해 정정. 원본 초안의 "⚠️ 조사항목"으로 표시됐던 것 중 다수가 이번에 확정됨(아래 각 섹션의 "✅ 검증 완료" 표시 참고).
>
> **착수 순서**: 2부(버그) → 1부(리팩토링) → 3부(신규A) → 4부(신규B). 버그가 데이터 무결성에 직접 영향을 주므로 최우선. 각 부는 독립적이라 병렬 착수도 가능.

---

## 1부. 리팩토링 / 기술부채 정리

### Tier 1 — 안전 삭제 (회귀위험 최소)

착수 전 전역 grep으로 참조 0건을 재확인하고, 삭제 후 `dotnet build` 오류 0 + `dotnet test` 기존 통과 수 유지를 검증한다.

**1-1. 죽은 모델 3파일 삭제** — ✅ 검증 완료 (2026-07-10, grep 전수조사)
- 대상: `Models/ChannelSku.cs`, `Models/ChannelSkuPriceHistory.cs`, `Models/MappingHistory.cs`
- 근거: 세 클래스 모두 자기 선언부 외 실사용 참조 0건 확인(`ChannelSkuRepository`가 실제로 쓰는 건 이름만 비슷한 별개의 `ChannelSkuModel`).
- 주의: `MappingHistory` 클래스는 `MappingRuleType`을 참조하지만, 이 클래스를 참조하는 코드가 없으므로 삭제 안전.

**1-2. `MappingHistory` 테이블 CREATE 구문 제거** — ✅ 검증 완료
- 대상: `Database/DbSchema.cs:138`의 `CREATE TABLE IF NOT EXISTS MappingHistory (...)`
- 근거: 이 테이블에 대한 read/write SQL이 `DbSchema.cs`의 CREATE 한 곳 외 전무.
- **정책**: 코드(CREATE 구문)만 제거하고, **기존 DB 파일에 이미 만들어진 테이블은 DROP하지 않는다**. 빈 죽은 테이블은 무해하며, DROP은 오히려 배포 리스크.

**1-3. `MainHub` 죽은 코드 정리**
- 대상: `Forms/MainHub.cs` `CreateMenuButton`(135행) 내 `Enabled=false` 기본값 — ✅ 검증 완료: 호출부 14곳(64~116행) 전부 즉시 `Enabled=true`로 재설정해 기본값이 무의미함.
- 영향: 미미(코드 가독성).

### Tier 2 — 죽은 분기 제거 (⚠️ 이번 검증으로 안전성 확정)

**2-1. `MappingRepository.SaveRules`의 Condition 분기 검토** — ✅ 조사 완료, **제거 안전**
- 대상: `Database/MappingRepository.cs:103` `SaveRules(...)`의 `if (ruleType == MappingRuleType.Condition)` 분기(111~121행, `DELETE FROM RuleConditionDetail ...` 포함)
- **검증 결과**: `SaveRules`의 전체 호출부를 전수조사함.
  - `Forms/MappingForm.cs:1984`(`SaveDirtyTabsAsync`), `:2077`(엑셀 임포트) — 둘 다 `grid.Tag is MappingRuleType ruleType` 조건으로만 호출되는데, `grid.Tag`가 `MappingRuleType`으로 세팅되는 곳은 `CreateRuleTabPage`(1789행) 단 한 곳뿐이며, 이 메서드는 Exception/Exact/Temp 탭(157~159행)에만 쓰이고 조건부 탭(`_conditionDetailTabPage`, 160행, `CreateConditionDetailTabPage()`로 별도 생성)에는 쓰이지 않음. → **UI에서 `SaveRules(Condition, ...)`이 호출되는 경로 없음**.
  - `Database/LegacyMigrationService.cs:304`의 `SaveRules(ruleType, ...)` 호출도 메서드명(`MigrateExactAndExceptionRules`)과 호출부(34~35행)가 Exact/Exception만 전달 — Condition 전달 경로 없음.
  - 단, `Tests/SkuMapperTests.cs:49`가 `repository.SaveRules(MappingRuleType.Condition, "CH1", ...)`를 **테스트 코드에서 직접** 호출함(UI를 거치지 않음). 이 테스트는 `RuleConditionDetail` 고아정리와 무관한 케이스라 분기 제거해도 깨지지 않을 것으로 보이나, **제거 후 반드시 `dotnet test` 재확인**.
- 결론: 프로덕션 호출 경로 0건 확정 → 이 분기 제거 가능. 단순화 작업으로 진행.

### Tier 3 — 중복 통합 (동작 불변, 헬퍼 추출)

**3-1. CSKU 정보 저장 헬퍼 2곳 통합** — ✅ 검증 완료
- 대상:
  - `Forms/OrderSkuMappingDialog.cs:258` `SaveChannelSkuInfoIfEntered(cskuCode, masterSku)`
  - `Forms/MappingForm.cs:799` `SaveChannelSkuInfoFromUnmappedPanel(cskuCode, masterSku, channelCode)`
- 근거: 두 메서드 모두 (1) VAT별도 라디오면 `Math.Round(price * 1.1m, 0)` 환산, (2) `ChannelSkuRepository.CreateIfNew(...)` 호출, (3) 실패 시 동일 "기존 CSKU 존재" 안내(문구까지 동일)로 거의 완전 동일. doc-comment도 문구까지 거의 일치.
- **범위 제외**: `Controls/QuickMappingPanel.cs`의 `OnSaveCskuClick`은 통합 대상 **아님** — CSKU *정보* 저장이 아니라 CSKU *매핑규칙* 저장(`AddConditionRuleWithDetails`)이라 성격이 다름.
- 변경: 공용 헬퍼로 추출. 시그니처 안:
  `SaveCskuFromInput(string channelCode, string cskuCode, string masterSku, string priceText, bool isVatExcluded, string invoiceName) → bool`
  (반환값 = CreateIfNew 성공 여부). 각 폼은 자기 컨트롤 값만 읽어 전달. VAT 환산 정책은 이 헬퍼로 단일화.
- 검증: 두 진입점(OFS 미매핑 도우미 / 매핑관리 미매핑 패널)에서 CSKU 신규생성·기존존재 동작이 이전과 동일.

---

## 2부. 버그 수정

### 근본 결함 (버그 1·2 공통 뿌리) — ✅ 근본원인 라이브 DB로 확정

`OutboundDetailTable`의 `UNIQUE INDEX (ShipmentGroupKey, MskuCode)`(`Database/DbSchema.cs:340`)를 저장 시 충돌 키로 쓰고, `Database/OutboundRepository.cs:25`의 `SaveOutbound`가 `ON CONFLICT(ShipmentGroupKey, MskuCode) DO UPDATE`로 **충돌 시 조용히 덮어쓴다**(사용자 경고 없음).

문제는 `ShipmentGroupKey`를 만드는 `Utils/ShipmentGrouping.cs:18 GetEffectiveGroupId`:
- 명시 그룹(`item.ShipmentGroupId`)이 없으면 `$"__row_{RuntimeHelpers.GetHashCode(item)}"` 반환 — **런타임 객체 해시**
- 주석 자체가 "내보내기 1회성 그룹 키일 뿐이라 영속성은 필요 없다"고 명시하는데, 이 값이 `Forms/OfsForm.cs:568`에서 **DB UNIQUE 키로 그대로 영속 저장**됨 — 설계 충돌
- 결과: 같은 발주서를 재로드해 재저장하면 객체가 새로 생성되어 해시가 바뀜 → `ON CONFLICT` 미탐지 → 중복 레코드. 반대로 세션 내 특정 조합에서 우연히 해시 충돌 → 덮어쓰기 손실.

### 버그 1 — 분리배송 시 이력에 1건만 저장 (원인 확정)

- ✅ 검증 완료: `Forms/OfsForm.cs:1030 OnSplitIntoNewShipmentClick`이 선택한 **여러 줄 전체에 동일한 `newGroupId` 하나**를 부여(`:1042~1047`, `foreach`로 모든 선택 줄에 같은 `$"{baseId}-분리{Guid.NewGuid()...[..6]}"` 세팅). "분리"라는 이름과 달리 실제로는 선택 줄을 한 그룹으로 묶음.
- 라이브 DB(`bin/Debug/net10.0-windows/ERP_Database.sqlite`)에서 실물 패턴 확인: CH005(진도그린) "아이스버블 면도기클리너 500ml"에 `__row_37787734`, `__row_37787734-분리6aa2ca`, `__row_37787734-분리474621-분리b4b57e` 등 "-분리" 접미사가 체인처럼 붙은 서로 다른 키 10개가 실제 저장돼 있음 — 버그 설명과 일치하는 네이밍 패턴 확인. (단, 10건 타임스탬프가 모두 같은 밀리초대라 실사용 반복클릭이 아니라 테스트 데이터일 가능성 높음 — 참고용 정황 증거로만 취급)
- 수정 방향: 분리배송은 선택 줄 **각각에 개별 고유 groupId**를 부여(줄 단위로 서로 다른 키). 전체 동일 부여 금지.

### 버그 2 — 특정 채널 발주 12건만 추고 이력 표시 (근본원인 재확정, ⚠️ 마이그레이션 가설은 기각)

- 사용자 확인: 특정 거래처 채널의 대량 발주시 실제 발주식 수량/취급 SKU 숫자보다 **같은 CSKU가 여러 주문에 반복 등장** / OFS(하단출력 포함) 주력 사용
- **✅ 검증 결과 1 (OrderNo 공백률)**: 거래처(오프_사입) 그룹 채널 CH001(투유)/CH002(푸디)/CH005(진도그린)/CH006(맘씨생활건강) 중 CH001만 OrderNo 100% 존재(51건), 나머지 3개 채널은 **OrderNo 100% 공백**(CH002 3/3, CH005 10/10, CH006 1/1). 가설의 절반은 맞음.
- **✅ 검증 결과 2 (하지만 마이그레이션이 원인은 아님)**: `Database/DbSchema.cs:332`의 `UPDATE OutboundDetailTable SET ShipmentGroupKey = OrderNo WHERE ShipmentGroupKey = ''` 백필은 **컬럼이 새로 생긴 시점의 과거 데이터에만 적용**된다. 현재 빌드는 저장 시마다 `OfsForm.cs:568`에서 `ShipmentGrouping.GetEffectiveGroupId(order)`를 직접 호출해 해시 키를 채우므로 `ShipmentGroupKey`가 애초에 빈 문자열이 되는 경우가 없어 이 백필 대상이 아니다. 실제로 라이브 DB의 CH002/005/006 행 전부 `ShipmentGroupKey`가 `OrderNo`가 아니라 `__row_...` 해시 패턴이었음(마이그레이션 흔적 없음).
- **결론**: 버그 2의 실제 원인은 마이그레이션이 아니라 **"근본 결함" 섹션에서 설명한 해시 기반 키의 구조적 결함 그 자체**다. OrderNo가 공백인 채널은 특히 이 결함에 취약할 뿐(재로드 시 해시가 바뀌어 새 레코드가 생기거나, 우연한 해시값으로 다른 주문과 충돌).
- 마이그레이션(`DbSchema.cs:332`) 자체는 과거 레거시 데이터 정합용으로 그대로 두어도 무방(주범이 아님이 확인됐으므로 원본 발주서 재업로드 여부 같은 후속 조사는 우선순위 낮춤).

### 통합 수정 방향

1. **`ShipmentGroupKey` 재설계(최우선)** — 해시 기반 폐기, **저장 시점 결정적(deterministic)·안정 고유키**로 변경. OrderNo가 없는 채널도 안정적이어야 하므로, 후보: `{ChannelCode}|{OrderNo 또는 원본행 안정식별자}#{합포장그룹}`. OrderNo가 없는 행에 대해 "안정 식별자"를 어디서 가져올지가 설계 핵심 — OfsOrderItem에 로드 시점에 부여되는 고정 ID가 있는지 확인 필요(다음 섹션에서 조사).
2. **분리배송 핸들러 수정** — `OnSplitIntoNewShipmentClick`: 선택 줄마다 개별 고유 groupId 부여.
3. **`SaveOutbound` 안전장치** — `ON CONFLICT` 덮어쓰기 건수를 집계해 반환/로그. 예상외 덮어쓰기 시 사용자 경고(조용한 유실 방지).
4. **마이그레이션은 현행 유지** — 과거 데이터 정합 목적이므로 그대로 둠. 다만 주석에 "이 백필은 현재 빌드에서는 신규 저장 경로에 영향 없음(참고)"을 추가.

- 검증 기준: 같은 CSKU 다수 + 분리배송 + 발주서 재로드 시나리오를 재현했을 때 이력 건수가 실제 발주 건수와 일치. 기존 회귀 테스트 통과 유지 + `ShipmentGroupingTests`/`CourierExporterTests`/`OutboundRepositoryTests` 확인.

---

## 3부. 신규기능 A — 거래처 출고이력 ERP화 + 문서관리 연동

### 배경 · 방향 (초안 요지, 코드 인용은 원본 유지)

- 채널 기준: `Models/ChannelType.cs:22 IsMarketplace()`가 Partner/Other만 false(거래처) — ✅ 검증 완료.
- CSKU가 이미 `MskuCode` 컬럼에 저장되는 중(명명 혼란): `Mapping/SkuMapper.cs:40` 주석대로 `order.MappedSku`(=매핑 규칙의 TargetSku)는 실제로 **CSKU 코드**이며, `Forms/OfsForm.cs:568`에서 `MskuCode = order.MappedSku`로 저장됨 — ✅ 검증 완료. 컬럼명 변경은 리스크가 크므로 **뷰 레이어에서만 "품목(CSKU)"로 표기**해 흡수.
- `OutboundDetailTable` 현재 컬럼(✅ 확인, `DbSchema.cs:83~98`)에는 `Note`, `UpdatedAt` 없음 — A-1에서 추가 필요.

### A-1. 스키마 확장
`OutboundDetailTable`에 `EnsureColumn`으로 컬럼 추가:
- `Note TEXT NOT NULL DEFAULT ''` — 비고
- `UpdatedAt TEXT` — 데이터 최종수정일. 모든 UPDATE 경로(`ApplyTrackingNo`/`UpdateDetail`/`MarkAsShipped`)에서 갱신

### A-2. 채널속성 이력관리 플래그
- `Models/ChannelConfig.cs`에 `bool ManageOutboundHistory { get; set; }` + `[Description("발주/출고 이력을 ERP 이력뷰에서 관리합니다. 주로 거래처(Partner/Other) 채널을 체크하세요.")]`

### A-3. ERP 출고이력 뷰 (기존 `OutboundHistoryForm` 확장)
- 컬럼 추가: 발주일(`CreatedAt`) · 채널 · 품목(CSKU=`MskuCode`) · 납품단가(`SupplyPrice`) · 수량(`Qty`) · 금액(`Qty*SupplyPrice`) · 송장번호(`TrackingNo`) · 출고상태(`Status`) · 비고(`Note`) · 최종수정일(`UpdatedAt`)
- 필터: 기간 + 채널(이력관리 플래그 켜진 채널만 콤보에 노출)

### A-4. CSKU별 묶어 집계
- 체크박스: 켜면 동일 CSKU를 통합(수량합·금액합), 해제 시 기본(줄 단위)

### A-5. 가격변동 이력 조회 (기존 `ChannelSkuPriceHistoryForm` 재사용)

### A-6. 엑셀 내보내기
- ERP 거래이력용 일반 엑셀 export 추가(A-3 컬럼 그대로)

### A-7. 문서관리 연동 (거래명세표 + 견적서)
- `Forms/DocsForm.cs`에 "출고이력 불러오기" 버튼 추가 → 채널·기간 선택 → 문서 라인으로 주입(`DocModels.cs` LineItem: `ItemName`←CSKU 표시명, `Qty`, `UnitPrice`←`SupplyPrice`)

### 구현 단계 요약
A-2(플래그) → A-1(스키마) → A-3·A-4·A-6(뷰/엑셀) → A-5(가격이력) → A-7(문서연동)

---

## 4부. 신규기능 B — 마감/이익분석 · 광고비 결과 DB 관리 강화

### 배경 (일부 이미 구현됨)
- 이익분석 DB저장: `Forms/SettlementForm.cs:105 OnSaveProfitFactClick` → `ProfitFactTable`
- 광고비 DB저장: `Forms/AdMappingForm.cs:204 OnSaveAdFactClick` → `AdFactTable`
- 종합보고서 DB조회: `Forms/ReportForm.cs`가 `ProfitFactTable`/`AdFactTable`에서 직접 읽음
- 외부 엑셀 불러오기: `Forms/ReportForm.cs:176 OnImportFromExcelClick` 이미 존재하고 `HasData` 체크 + 덮어쓰기 확인까지 갖춰져 있음(안전) — ✅ 검증 완료

### B-2. 이익분석 상세 DB 관리 + 종합보고서 드릴다운

- ✅ 검증 완료: `SettlementData`(`DbSchema.cs:69~81`)에 `Period`/`ProductGroup` 컬럼 없음. `SettlementRepository.cs:11 Insert`는 단순 append, dedup 없음.
- 반면 집계는 `ProfitFactRepository.cs:11 SaveProfitFacts`가 `(Period,ChannelCode)` **교체식**(DELETE 후 INSERT) + `HasData(period,channel)`(92행) — ✅ 검증 완료.

**변경**:
1. `SettlementData`에 `Period TEXT`, `ProductGroup TEXT` 추가(`EnsureColumn`)
2. `SettlementRepository` 저장을 `(Period, ChannelCode)` 교체식으로 전환
3. `GetSettlementDetails(string period, string channelCode, string productGroup) → List<SettlementData>` 추가
4. 종합보고서 드릴다운: `ReportForm`의 집계 피벗행 더블클릭 → 상세 그리드(읽기전용, "엑셀 내보내기" 버튼 포함)

### B-1. 자동 저장 (기간 20개 롤링)

- **✅ 사용자 결정 (2026-07-10)**: 롤링 삭제 기준은 **통합 Period 기준**(ProfitFact/AdFact/Settlement 세 테이블을 합쳐 "최근 20개 월"을 하나의 기준으로 판단, 사용자 관점 "월" 단위와 일치).
- 이익분석·광고비 분석 결과 산출 시 `ProfitFactTable`/`AdFactTable`(및 B-2의 `SettlementData`)에 자동 저장.
- 저장 후 `DISTINCT Period`(통합 집합)가 20을 초과하면 가장 오래된 Period의 데이터를 세 테이블 모두에서 자동 삭제.
- 근거 부품: `ProfitFactRepository.cs:81 GetDistinctProfitPeriods`, `:168 GetDistinctAdPeriods`(DESC 정렬) 이미 존재 — ✅ 검증 완료.

### B-3. 저장경로 이원화 조사 (보류, ⚠️ 스캐폴드 후 재검토)

- ✅ 검증 결과 갱신: `ProfitFactTable`에 쓰는 경로가 원안의 2곳이 아니라 **3곳**임 — `SettlementForm.cs:728`(`OnSaveProfitFactClick`), `Services/ClosingOrchestrator.cs:165,253`(`SaveProfitFactsAsync`), 그리고 `Forms/ReportForm.cs:239`(엑셀 임포트). 이 중 **ReportForm 경로는 `HasData` 체크 + 사용자 확인 후 저장이라 안전** — ✅ 검증 완료. 나머지 두 경로(SettlementForm ↔ ClosingOrchestrator)의 충돌 가능성은 실사용 빈도 확인 후 판단.
- 조치: 이번 라운드에서 구현하지 않고 실사용 테스트로 적합성 확인. 문제 재현 시 (a) 병합 저장(append+집계) 또는 (b) 경로 일원화 검토.

---

## 부록. ⚠️ 남은 조사항목 · 미해결 체크리스트

| # | 항목 | 상태 | 관련 |
|---|---|---|---|
| 1 | `SaveRules(Condition)` 실사용 호출 경로 0건 여부 | ✅ 해결 (2026-07-10) — 프로덕션 경로 없음, 테스트 코드만 직접 호출 | 1부 Tier 2 |
| 2 | 거래처 발주서 OrderNo 존재/고유성 | ✅ 해결 (2026-07-10, 라이브 DB) — 3/4 채널 100% 공백 | 2부 버그2 |
| 3 | 버그2 근본원인이 마이그레이션인지 해시키 구조 자체인지 | ✅ 해결 — 해시키 구조 자체가 원인, 마이그레이션 가설 기각 | 2부 |
| 4 | 롤링 삭제 기준: 테이블별 vs 통합 Period | ✅ 결정됨 — 통합 Period | 4부 B-1 |
| 5 | ProfitFactTable 저장경로 3곳 중 미가드 2곳의 실사용 충돌 빈도 | ⚠️ 미해결(실사용 테스트 필요) | 4부 B-3 |
| 6 | 분리배송/합포장 메뉴 이중배치(상단그리드+미리보기) UX 통일 | ⚠️ 미해결(사용자 판단 필요) | (CURRENT_STATE §7.3) |
| 7 | CFS 테스트 2건 pre-existing 실패 원인 | ⚠️ 미해결 | (CURRENT_STATE §8) |
| 8 | `ExportSummaryConfig` TH/TW/VN/BR 헤더 TODO | ⚠️ 외부의존(말레이시아 등 판매파일 헤더 확보 전) | (CURRENT_STATE §7.2) |

---

## 착수 공통 검증 기준
- 각 변경 후 `dotnet build` 오류 0
- `dotnet test` 기존 통과 수 유지 또는 신규 테스트 추가분만큼 증가
- 스키마 변경은 `EnsureColumn`/`CREATE ... IF NOT EXISTS`로 **기존 DB 파일 마이그레이션** 보장
- 사용자 작업 원칙 준수: 명시적 지시 없는 코드 자율수정 금지, 실제 업로드/코드 분석 후 실행 필요, 사고 직후 확인 배치 진행
