# MiniERP2 인수인계 — 2026-06-27 Claude Code 세션 (자동화 연속 작업)

이 문서는 다음 작업자(미래의 Claude 세션 포함)가 이어받을 수 있도록 진행 상황을 정리한 것이다.
프로젝트 전체 배경/아키텍처는 [PLAN.md](PLAN.md) 참고. 2026-06-26 세션 작업 내역은 git log
(커밋 `1322618`~`f7db495`)와 이 문서의 이전 버전(git history) 참고.

## 이 세션의 배경

사용자가 "노션 기능 체크리스트 하단의 미구현/제약사항을 자동으로 구현, 필요한 권한은 자동 승인,
애매하면 권장 방향으로 먼저 개발 후 메모"를 요청해 자동화 모드로 연속 작업 중이다. 작업 단위가
끝날 때마다 체크하고, 컨텍스트 사용량이 높아지면 이 문서와 Notion 일지를 갱신하기로 합의했다.

## 이번 세션에서 한 일 (커밋 순서, 최신이 아래)

1. **다른 CLI가 남긴 중복/깨진 파일 정리** — `DataLoaders/ProfitCalculator.cs`(존재하지 않는
   enum 참조), `Models/MappingConflict.cs`(기존 record와 충돌), `Forms/FormManager.cs`,
   `DataLoaders/GrowthAuxJoinEngine.cs`(빈 파일) 삭제. 다른 작업 폴더
   `C:\Users\thebo\source\repos\MiniERP2`(커밋 0개)가 존재한다는 것도 확인됨 — 혼동 주의.
2. **GitHub 백업 푸시** — 그동안 미푸시 상태였던 로컬 13개 커밋을 origin/main에 푸시.
3. **ExcelLikeDataGridView 붙여넣기 구현** — Ctrl+V/우클릭 메뉴, 기존 행 범위 안에서만 채움.
4. **이중 출고 방지(Upsert)** — `OutboundDetailTable`에 `(OrderNo, MskuCode)` 유니크 인덱스 +
   `ON CONFLICT DO UPDATE`. 기존 중복 데이터가 있으면 인덱스 생성은 조용히 무시.
5. **창 크기/위치 기억** — `FormManager`가 `window_bounds.json`에 저장/복원. MainHub 포함.
6. **레거시(구버전 C# MiniERP V3) DB 마이그레이션 도구 + MainHub 동기화 요약** — 아래 "레거시
   마이그레이션" 절 참고. 가장 중요한 발견/작업.
7. **CSV 파일 실제 처리** — `CsvWorkbookReader`(UTF-8/CP949 자동 감지, RFC4180 파싱)로
   OrderLoader/SettlementLoader가 .csv도 읽을 수 있게 됨.
8. **암호 걸린 엑셀 자동 해제** — `ExcelFileOpener` + `PasswordPromptDialog`. 비밀번호 없이
   열다 실패하면 다이얼로그로 물어보고 재시도. 앱 내 엑셀 여는 지점 전부(7곳) 연결.

빌드/테스트 상태: `dotnet build` 오류 0, `dotnet test` **68/68 통과** (이 세션 시작 시 51개였음).

## 레거시 데이터 마이그레이션 (이번 세션 핵심 발견)

사용자가 "기존 파이썬(SalesManagerV2)으로 만든 매핑조건 json을 이식해달라"고 요청했으나, 이
머신에서 SalesManagerV2 자체나 그 json 파일은 끝내 찾지 못했다. 대신
**`C:\Users\thebo\source\repos\MiniERP\MiniERP\bin\Debug\net10.0-windows\ERP_Database.sqlite`**
(구버전 C# MiniERP V3의 실제 운영 DB)에서 매핑 데이터를 발견했다. 이 DB의 `SalesChannelTable`
한 행에 `Memo: "[파이썬 마이그레이션]"`이 적혀있어, **구버전 C# 앱이 이미 Python SalesManagerV2의
exact_rules.json 등을 이관해 보관해온 것**으로 확인됨(`FormLegacyMigrator.cs` 참고). 즉 사용자가
찾던 "파이썬 매핑조건"의 최종 산출물이 이 DB다.

발견된 실데이터 규모: 채널 16개, 마스터SKU 550건, 채널별 납품가 405건, 1:1 매핑규칙 270건,
예외규칙 13건, **조건부 매핑규칙 485건(+상세조건 750건, AND/OR 다중조건)**, 임시SKU 9건.

`Database/LegacyMigrationService.cs`를 만들어 MainHub의 [레거시 데이터 가져오기] 버튼에서
실행할 수 있게 했다. **이번에 이관한 것**: 채널 설정(필드매핑/보조소스JOIN/고정값 변환 포함),
마스터SKU, 채널별 납품가, 1:1/예외 매핑규칙(270+13건).

**이번에 의도적으로 미루고 가져오지 않은 것**(범위가 가장 커서 별도 착수 필요):
- **조건부 매핑규칙 485건+상세 750건** — 레거시는 한 규칙에 여러 조건(헤더명/연산자(contains,
  not_contains, ==)/값/AND·OR)을 가질 수 있는데, MiniERP2의 현재 `MappingRuleType.Condition`은
  단일 키 Contains 매칭만 지원한다(`MappingRule.Key` 하나뿐). **임의 단순화하지 말 것**(CLAUDE.md
  지침)이라는 원칙에 따라, 이건 새 테이블(`RuleConditionDetailTable`류)과 `SkuMapper`의 조건
  평가 로직 확장이 필요한 별도 아키텍처 작업이다. 다음 작업자가 이어서 할 것.
- `CourierMasterTable.TemplateJson`(택배사 양식 1건) — role 라벨("[표준] 수령인명" 등)을
  OfsOrderItem 속성명으로 번역해야 하는데 데이터가 1건뿐이라 택배사양식관리창에서 수동 입력이
  더 빠름.
- `RuleConditionTable`/`RuleConditionDetailTable` 외 운영 이력성 테이블(OutboundTable,
  UploadBatchTable, ItemHistoryTable, OnlineSettlementTable, TrackingProfileTable)은 과거
  운영기록이라 이관 대상에서 제외.

`Mapping/SkuMapper.cs`에 `[EXCLUDED]` 마커 처리를 추가했다(레거시 예외규칙의 TargetSku가
`"[EXCLUDED]"`이면 실제 SKU가 아니라 "배송비/수수료 등 상품 아닌 행은 매핑 제외"라는 뜻이었음).

⚠️ **주의**: 분석 과정에서 실수로 레거시 DB의 실제 데이터(상품명/가격 등)가 담긴 디버그용 json
2개를 커밋·푸시했다가 다음 커밋에서 삭제했다(`9c9741e`). **git history에는 여전히 남아있다**
(`6793fb4` 커밋). 진짜 민감정보(비밀번호 등)는 아니지만, 완전히 지우려면 history rewrite가
필요하므로 사용자에게 확인 후 진행할 것 — 임의로 force-push/rewrite 하지 말 것.

## 알아둬야 할 설계 결정 / 임의 선택 사항 (이번 세션 추가분)

- **레거시 ChannelType 판별은 ChannelTypeLabel 키워드 추정**이다(`LegacyMigrationService.
  GuessChannelType`) — "그로스"/"로켓"/"쿠팡"/"11번가"/"아마존"/"거래처" 등 키워드 매칭. 부정확할
  수 있으니 가져오기 후 채널설정에서 확인 권장.
- **레거시 STD_PRODUCT_ID, STD_AMT, STD_TAX_*, STD_DATE, STD_EXTRA1/2는 MiniERP2에 대응 필드가
  없어 이관 시 버려진다.** STD_ORDER_ID는 StdField.ProductNo로 매핑(OrderLoader가 ProductNo를
  주문번호로 쓰는 기존 관례를 따름).
- **레거시는 발주서/정산서 매핑을 구분하지 않았다.** 이관 시 같은 MappingJson을
  OrderFieldMappings와 SettlementFieldMappings 양쪽에 동시 적용한다(겹치는 필드만).
- **이중출고 방지 유니크 인덱스 생성 실패는 조용히 무시한다** — 과거 버그로 이미 중복 데이터가
  쌓인 DB에서는 인덱스가 생성되지 않아 Upsert도 동작하지 않을 수 있음. 실제 운영 DB에 적용 시
  중복 제거 스크립트를 먼저 돌리는 게 안전.
- CSV 인코딩은 UTF-8(BOM 포함) 우선 시도 후 실패하면 CP949로 폴백한다. 둘 다 아닌 인코딩(예:
  UTF-16)은 처리하지 않는다.

## 알려진 미구현/제약사항 (남은 것)

- **조건부 매핑 다중 AND/OR 아키텍처 업그레이드 + 레거시 485건 이관** — 위 "레거시 마이그레이션"
  절 참고. 가장 큰 남은 작업.
- 문서관리(PDF 출력, Phase 8) 전체 미구현
- 운송장 매핑/동명이인 조율/합포장·분리배송 컨텍스트 메뉴(PLAN.md 5.5절 신규설계 항목) 미착수
- 이익분석 결과의 SalesManagerV2 실데이터 대비 회귀검증 — 비교할 "정답" 산출물(엑셀 등)이 없어
  미수행. PLAN.md 명세 자체를 정답으로 한 단위테스트만 존재.
- SKU 매핑 도우미로 선택한 매핑은 1회성(영구 규칙 미저장) — 이전 세션 노트, 여전히 유효.

## 다음 작업 후보 (우선순위 제안)

1. **조건부 매핑 다중 조건 아키텍처 업그레이드** — `RuleConditionDetailTable` 신설,
   `SkuMapper`가 OfsOrderItem 필드 기준으로 AND/OR 평가하도록 확장, 그 다음 레거시 485건 이관.
2. Phase 8 문서관리(거래명세표/견적서/마감내역서 PDF 출력)
3. OFS 운송장매핑/동명이인 조율/합포장·분리배송 컨텍스트 메뉴
4. git history에 남은 레거시 데이터 디버그 파일 완전 제거(사용자 확인 후 history rewrite)

## 참고

- Notion "MiniERP2" 페이지: https://app.notion.com/p/38bde24a14bc80488a35d5e74307fd08
- Notion "작업일지": https://app.notion.com/p/38bde24a14bc80488ff9d2f25e485ffa
- Notion "기능 테스트 체크리스트": https://app.notion.com/p/38bde24a14bc81348051fd2d32843288
- 레거시 운영 DB(읽기 전용으로 참고): `C:\Users\thebo\source\repos\MiniERP\MiniERP\bin\Debug\net10.0-windows\ERP_Database.sqlite`
- 정체불명의 빈 병렬 작업폴더: `C:\Users\thebo\source\repos\MiniERP2`(git 커밋 0개, 용도 불명 —
  사용자에게 삭제해도 되는지 확인 후 처리 권장)
