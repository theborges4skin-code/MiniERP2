# MiniERP2 인수인계 — 2026-06-27 Claude Code 세션 (자동화 연속 작업)

이 문서는 다음 작업자(미래의 Claude 세션 포함, 특히 이 대화의 auto-compact 이후 이어받는 경우)가
바로 이어받을 수 있도록 진행 상황을 정리한 것이다. 프로젝트 전체 배경/아키텍처는
[PLAN.md](PLAN.md) 참고. 2026-06-26 세션 작업 내역은 git log(커밋 `1322618`~`f7db495`) 참고.

**지금 빌드/테스트 상태**: `dotnet build` 오류 0, `dotnet test` **77/77 통과**.
마지막 커밋: `c6de8f6`. 전부 `origin/main`에 푸시됨.

## auto-compact 이후 추가로 한 일

10. **조건부 매핑 다중조건 전용 편집 UI** 구현 완료 — 매핑관리창에 "조건부 매핑(상세)" 탭 신설.
    `Forms/MappingForm.cs`의 `CreateConditionDetailTabPage` 이하. 좌측에서 채널의 조건부 규칙
    목록(요약 Key/TargetSku)을 고르면 우측에 그 규칙의 상세조건(HeaderField/Operator/
    TargetValue/Logic) 그리드가 뜨고, 각 영역(규칙 정보 저장/상세조건 저장)이 즉시 DB에
    반영된다. 기존 단순 "조건부 매핑" 탭의 일괄 `SaveRules` 저장과는 완전히 분리되어 있어,
    이제 단순 탭에서 저장해도 이 탭에서 관리하는 다중조건 데이터가 삭제되지 않는다
    — **이전에 기록했던 데이터 손실 위험은 해소됨**. `MappingRepository`에
    `UpdateConditionRuleSummary`/`DeleteConditionRule`/`ReplaceConditionDetails` 추가.
11. 사용자가 실제 프로그램을 켜놓고 테스트하면서 발견한 3건 추가 처리:
    - `StdField.DeliveryMessage`(배송메세지) 필드 신설 — 발주서 매핑 탭, 택배사 양식의
      "매핑할 데이터" 목록에 추가했고 `OrderLoader`가 실제로 읽어 `OfsOrderItem.
      DeliveryMessage`에 채운다. `CourierExporter`는 속성명 리플렉션 방식이라 추가 수정 없이
      택배사 출력양식에서 바로 쓸 수 있다.
    - 채널설정 발주서/정산서 매핑 탭의 샘플 엑셀 헤더 미리보기: 더블클릭으로 "열"에 적용할 때
      미리보기에서 선택한 헤더 행 번호도 함께 "헤더 행" 칸에 반영하도록 수정(기존엔 헤더 행을
      2/3/4로 바꿔 헤더를 찾아도 매핑 설정 쪽 헤더 행이 1로 남아 수동 수정이 필요했음).
    - OFS 미매핑 리스트/매핑관리창 충돌 강조/마감 원가없음 강조 셀: 배경색만 지정하고
      글자색을 지정하지 않아 시스템 다크모드에서 흰 글자 + 연분홍/허니듀 배경이 겹쳐 안 보이는
      문제 수정 — 강조 배경에는 항상 `Color.Black` 글자색을 함께 지정하도록 변경
      (`Forms/OfsForm.cs`, `Forms/MappingForm.cs`, `Forms/SettlementForm.cs`).

## 이 세션의 배경

사용자가 "Notion 기능 체크리스트 하단의 미구현/제약사항을 자동으로 구현, 필요한 권한은 자동 승인,
애매하면 권장 방향으로 먼저 개발 후 메모"를 요청해 자동화 모드로 연속 작업 중이다. 작업 단위가
끝날 때마다 체크하고, 컨텍스트 사용량이 높아지면 이 문서와 Notion 일지를 갱신하기로 합의했다.
대화가 auto-compact될 예정이라, 이 문서 + git 커밋 + 메모리 파일(`.claude/.../memory/`)이 그
이후의 유일한 컨텍스트가 된다 — 여기 적힌 정보가 가장 신뢰할 수 있는 최신 상태다.

## 이번 세션에서 한 일 (커밋 순서, 최신이 아래)

1. 다른 CLI가 남긴 중복/깨진 파일 정리(`DataLoaders/ProfitCalculator.cs` 등 4개 삭제)
2. GitHub 백업 푸시(미푸시 로컬 13개 커밋 → origin/main)
3. ExcelLikeDataGridView 붙여넣기 구현(Ctrl+V/우클릭, 기존 행 범위 안에서만)
4. 이중 출고 방지(Upsert) — `(OrderNo, MskuCode)` 유니크 인덱스 + `ON CONFLICT DO UPDATE`
5. 창 크기/위치 기억 — `FormManager` + `window_bounds.json`
6. **레거시(구버전 C# MiniERP V3) DB 마이그레이션 도구 + MainHub 동기화 요약** — 아래 절 참고
7. CSV 파일 실제 처리(`CsvWorkbookReader`, UTF-8/CP949 자동 감지)
8. 암호 걸린 엑셀 자동 해제(`ExcelFileOpener` + `PasswordPromptDialog`, 앱 내 7곳 전부 연결)
9. **조건부 매핑 다중 AND/OR 아키텍처 + 레거시 조건부 규칙 485건 이관** — 아래 절 참고

## 레거시 데이터 마이그레이션 (이번 세션 핵심 발견 — 가장 중요)

사용자가 "기존 파이썬(SalesManagerV2)으로 만든 매핑조건 json을 이식해달라"고 요청했으나, 이
머신에서 SalesManagerV2 자체나 그 json 파일은 끝내 찾지 못했다. 대신
**`C:\Users\thebo\source\repos\MiniERP\MiniERP\bin\Debug\net10.0-windows\ERP_Database.sqlite`**
(구버전 C# MiniERP V3의 실제 운영 DB)에서 매핑 데이터를 발견했다. 이 DB의 `SalesChannelTable`
한 행에 `Memo: "[파이썬 마이그레이션]"`이 적혀있어, **구버전 C# 앱이 이미 Python SalesManagerV2의
exact_rules.json 등을 이관해 보관해온 것**으로 확인됨(`FormLegacyMigrator.cs` 참고). 즉 사용자가
찾던 "파이썬 매핑조건"의 최종 산출물이 이 DB다. (이 사실은 메모리에도 저장되어 있다: memory의
`legacy_migration_source` 항목.)

발견된 실데이터 규모: 채널 16개, 마스터SKU 550건, 채널별 납품가 405건, 1:1 매핑규칙 270건,
예외규칙 13건, 조건부 매핑규칙 485건(+상세조건 750건, AND/OR 다중조건), 임시SKU 9건.

`Database/LegacyMigrationService.cs` + MainHub의 [레거시 데이터 가져오기] 버튼으로 실행한다.
**이관 완료된 것** (전부):
- 채널 설정(필드매핑/보조소스JOIN/고정값 변환 포함)
- 마스터SKU, 채널별 납품가
- 1:1/예외 매핑규칙(270+13건)
- **조건부 매핑규칙(485건, 다중 AND/OR 조건)** — `RuleConditionDetail` 신규 테이블 +
  `ConditionEvaluator`(평가 로직) + `SkuMapper` 확장으로 임의 단순화 없이 이관 완료.
  `SkuMapper`가 실제로 이 다중조건을 평가해서 매핑하는 것까지 통합 테스트로 검증됨
  (`Migrate_ConditionRule_WorksThroughSkuMapper`).

**아직 이관하지 않은 것**(범위가 작아서 보류, 우선순위 낮음):
- `CourierMasterTable.TemplateJson`(택배사 양식 1건) — role 라벨 번역이 필요한데 데이터가
  1건뿐이라 택배사양식관리창에서 수동 입력이 더 빠름.
- 운영 이력성 테이블(OutboundTable, UploadBatchTable, ItemHistoryTable, OnlineSettlementTable,
  TrackingProfileTable) — 과거 운영기록이라 이관 대상에서 제외.

**조건부 매핑 편집 UI는 이제 있다(해결됨)**: 매핑관리창에 "조건부 매핑(상세)" 탭이 신설되어
다중조건 규칙을 보고/추가/수정/삭제할 수 있다. 기존 단순 "조건부 매핑" 탭에서 [저장]을 눌러도
이 탭이 관리하는 상세조건은 더 이상 삭제되지 않는다(분리된 즉시-저장 방식으로 구현). 자세한
내용은 위 "auto-compact 이후 추가로 한 일" 10번 참고.

`Mapping/SkuMapper.cs`에 `[EXCLUDED]` 마커 처리도 추가했다(레거시 예외규칙의 TargetSku가
`"[EXCLUDED]"`이면 실제 SKU가 아니라 "배송비/수수료 등 상품 아닌 행은 매핑 제외"라는 뜻이었음).

⚠️ **주의**: 분석 과정에서 실수로 레거시 DB의 실제 데이터(상품명/가격 등)가 담긴 디버그용 json
2개를 커밋·푸시했다가 다음 커밋에서 삭제했다(`9c9741e`). **git history에는 여전히 남아있다**
(`6793fb4` 커밋). 진짜 민감정보(비밀번호 등)는 아니지만, 완전히 지우려면 history rewrite가
필요하므로 사용자에게 확인 후 진행할 것 — 임의로 force-push/rewrite 하지 말 것.

## 알아둬야 할 설계 결정 / 임의 선택 사항 (이번 세션분)

- **레거시 ChannelType 판별은 ChannelTypeLabel 키워드 추정**이다(`LegacyMigrationService.
  GuessChannelType`). 부정확할 수 있으니 가져오기 후 채널설정에서 확인 권장.
- **레거시 STD_PRODUCT_ID, STD_AMT, STD_TAX_*, STD_DATE, STD_EXTRA1/2는 MiniERP2에 대응 필드가
  없어 이관 시 버려진다.** STD_ORDER_ID는 StdField.ProductNo로 매핑.
- **레거시는 발주서/정산서 매핑을 구분하지 않았다.** 이관 시 같은 MappingJson을
  OrderFieldMappings와 SettlementFieldMappings 양쪽에 동시 적용한다(겹치는 필드만).
- **레거시 조건 상세의 HeaderName(헤더 텍스트)→StdField 변환은 키워드 추정**이다
  (`LegacyMigrationService.TryTranslateLegacyHeaderName`: "옵션"→OptionName, "상품명"/"품목명"
  →ProductName, "수량"→Quantity, "주문"→ProductNo, "수령/받는"→Recipient, "연락처/전화"→Phone,
  "주소"→Address). 매칭 안 되는 헤더의 조건은 건너뛰고, 모든 조건이 번역 불가능하면 규칙 전체를
  건너뛴다.
- **조건부 매핑 이관은 1회성 ADD라 재실행하면 중복 추가된다**(1:1/예외 규칙처럼 병합하지 않음).
  같은 레거시 DB를 두 번 가져오면 조건부 규칙이 두 배가 되니 주의.
- **이중출고 방지 유니크 인덱스 생성 실패는 조용히 무시한다** — 과거 버그로 이미 중복 데이터가
  쌓인 DB라면 인덱스가 생성되지 않아 Upsert도 동작하지 않을 수 있음.
- CSV 인코딩은 UTF-8(BOM 포함) 우선 시도 후 실패하면 CP949로 폴백한다. 그 외 인코딩은 미지원.

## 알려진 미구현/제약사항 (남은 것)

- 문서관리(PDF 출력, Phase 8) 전체 미구현
- 운송장 매핑/동명이인 조율/합포장·분리배송 컨텍스트 메뉴(PLAN.md 5.5절 신규설계 항목) 미착수
- 이익분석 결과의 SalesManagerV2 실데이터 대비 회귀검증 — 비교할 "정답" 산출물이 없어 미수행.
  PLAN.md 명세 자체를 정답으로 한 단위테스트만 존재.
- SKU 매핑 도우미로 선택한 매핑은 1회성(영구 규칙 미저장) — 이전 세션 노트, 여전히 유효.

## 다음 작업 후보 (우선순위 제안)

1. Phase 8 문서관리(거래명세표/견적서/마감내역서 PDF 출력)
2. OFS 운송장매핑/동명이인 조율/합포장·분리배송 컨텍스트 메뉴
3. git history에 남은 레거시 데이터 디버그 파일 완전 제거(사용자 확인 후 history rewrite)

## 참고

- Notion "MiniERP2" 페이지: https://app.notion.com/p/38bde24a14bc80488a35d5e74307fd08
- Notion "작업일지": https://app.notion.com/p/38bde24a14bc80488ff9d2f25e485ffa
- Notion "기능 테스트 체크리스트": https://app.notion.com/p/38bde24a14bc81348051fd2d32843288
- 레거시 운영 DB(읽기 전용으로 참고): `C:\Users\thebo\source\repos\MiniERP\MiniERP\bin\Debug\net10.0-windows\ERP_Database.sqlite`
- 정체불명의 빈 병렬 작업폴더: `C:\Users\thebo\source\repos\MiniERP2`(git 커밋 0개, 용도 불명 —
  사용자에게 삭제해도 되는지 확인 후 처리 권장)
- 이 프로젝트의 영구 메모리: `C:\Users\thebo\.claude\projects\c--Users-thebo-Documents-MiniERP2\memory\`
  (`legacy_migration_source.md`, `legacy_condition_rules_todo.md` — auto-compact 이후에도 유지됨)
