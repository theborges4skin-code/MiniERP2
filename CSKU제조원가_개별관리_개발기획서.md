# CSKU 제조원가 개별관리(오버라이드) 개발기획서

작성일: 2026-07-31 (검토 반영: 2026-07-31)
대상: 채널별 CSKU 관리창(`Forms/ChannelCskuForm.cs`), 원가 해석 경로 전반
상태: 기획(검토 완료) → 코드 작성 단계

---

## 1. 배경

현재 채널별 CSKU 관리창의 "제조원가" 열을 수정하면, 그 값이 마스터DB(`ItemTable.CostPrice`)에 직접 반영된다. 특정 채널에서만 원가를 다르게 잡고 싶어도 방법이 없다.

**요구**: 마스터 연동 없이 해당 CSKU의 제조원가만 별도로 관리할 수 있어야 한다.

---

## 2. 현행 구조 (코드 확인 사실)

### 2.1 원가는 이미 3층 구조 + 폴백 체인으로 되어 있음

| 층 | 저장소 | 성격 |
|---|---|---|
| 1 | `OutboundDetailTable.PurchasePrice` (nullable) | 발주/출고 라인 단위 **원가 스냅샷** |
| 2 | `PurchaseSkuTable.PurchasePrice` | **매입처(채널)별** 매입가. `PurchaseSkuPriceHistory`로 변경이력 관리 |
| 3 | `ItemTable.CostPrice` | 대표 원가 **폴백**. `ItemCostHistory`로 변경이력 관리 |

폴백 구현 위치(현재 2곳에 분산):

- `Forms/OutboundHistoryForm.ResolveCostSnapshot()` — 매입처 지정 시 `PurchaseSku.PurchasePrice`, 아니면 `ItemTable.CostPrice`
- `Database/PartnerClosingRepository.cs:186` — `od.PurchasePrice ?? ItemTable.CostPrice`

→ **비어 있는 층은 "판매채널(CSKU) 단위 원가" 하나뿐**이다. 이번 개발은 그 층을 채우는 것이다.

### 2.2 CSKU 화면의 제조원가 열은 ChannelSkuModel 소속이 아님

`ChannelCskuForm`:

- 제조원가 열은 `DataPropertyName = string.Empty`인 **비바인딩 열**이고, `_costPriceByMsku` 딕셔너리로 화면값을 채운다(`OnCskuGridCellFormatting`)
- 편집하면 `_dirtyCostMskus`에 Msku를 담아뒀다가, 저장 시 `ItemRepository.Upsert(existing)`로 **마스터를 직접 갱신**한다(`OnSaveClick`)
- 마스터SKU가 `ItemTable`에 없으면(임시SKU 등) 저장을 건너뛰고 사이드바로 안내한다 → 이 동작은 유지 대상

### 2.3 현행 동작의 부작용 3가지

1. **조용한 교차 오염**: 같은 Msku를 참조하는 다른 채널의 CSKU 원가가 함께 바뀐다. 화면에는 아무 경고가 없다.
2. **과거 손익 소급 변동**: `PurchasePrice`가 NULL인 미스냅샷 출고 라인은 마감 계산 시 현재 마스터 원가를 참조하므로, 오늘 원가를 고치면 과거 미확정 구간 손익이 함께 바뀐다. (단, `PartnerClosingLineTable`에는 `CostPrice`가 라인별로 복사·저장되므로 **이미 확정된 마감은 영향 없음**)
3. **인지 불가**: 사용자는 CSKU 값을 고쳤다고 인식하지만 실제로는 마스터를 고칠 것이다.

### 2.4 `ChannelSkuTable` 스키마 및 이력 자산

```
ChannelSkuTable(ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName, Note, UpdatedAt, Unit, Packing)
  PRIMARY KEY (ChannelCode, CskuCode)
```

- 컬럼 추가는 `DbSchema`의 `EnsureColumn(connection, "ChannelSkuTable", ...)` 패턴이 이미 5건 사용 중(InvoiceDisplayName/Note/UpdatedAt/Unit/Packing) → 동일 방식으로 안전하게 추가 가능
- `ChannelSkuRepository.Upsert()`는 트랜잭션 안에서 `RecordFieldChange()`로 변경 필드를 `ChannelSkuFieldHistory`에 1행씩 기록한다(현재 대상: 매칭된 마스터SKU / 송장표시명 / 비고) → **신규 이력 테이블 불필요**

---

## 3. 설계 결정

| 결정 | 내용 | 사유 |
|---|---|---|
| **채택** | `ChannelSkuTable`에 nullable 컬럼 `CostPriceOverride` 1개 추가 | 비어 있는 한 층만 정확히 채움. 기존 폴백 체인을 자연스럽게 편입 |
| **불채택** | 별도 `IsCostLinked` 플래그 컬럼 | nullable 값 자체가 플래그다. 플래그와 값이 어긋나는 모순 상태(플래그=연동인데 값 NULL이 아님)가 생김. 체크박스는 **UI 표현에만** 사용 |
| **불채택** | 해당 CSKU용 마스터SKU를 새로 만들어 분리 | "물리적 상품 1개 = Msku 1개" 원칙이 깨지고 매핑규칙·출고이력·마감·재고가 모두 갈라짐. 원가 하나 때문에 치르기엔 비용이 큼 |
| **범위 밖** | 매입처가 달라서 원가가 다른 경우 | 이미 `PurchaseSkuTable` + 발주/출고 라인의 매입처 지정으로 해결됨. 이번 건과 혼용하지 않음 |

---

## 4. 상세 설계

### 4.1 스키마 변경

`DbSchema`의 마이그레이션 구간에 1줄 추가:

```
EnsureColumn(connection, "ChannelSkuTable", "CostPriceOverride", "REAL");
```

- **NULL 허용, 기본값 없음** → 기존 전 데이터는 NULL이 되어 현행 동작(마스터 연동)이 그대로 유지된다
- CREATE TABLE 원문은 건드리지 않고 EnsureColumn만 추가(기존 5개 컬럼과 동일 방식)

의미 정의:

| 값 | 의미 |
|---|---|
| `NULL` | **마스터 연동**. 원가는 `ItemTable.CostPrice`를 따른다(현행 동작) |
| 숫자 | **개별관리**. 이 CSKU의 제조원가는 이 값이며 마스터 변경에 영향받지 않는다 |
| `0` | 개별관리 상태에서 원가 0원. NULL과 명확히 구분한다 |

### 4.2 모델 변경

`Models/ChannelSkuModel.cs`에 `decimal? CostPriceOverride` 추가. 주석에 "NULL=마스터 연동, 값 있음=개별관리"를 명시한다.

### 4.3 저장 및 이력

`ChannelSkuRepository.Upsert()`:

- INSERT/ON CONFLICT 절에 `CostPriceOverride` 추가
- 기존 `RecordFieldChange()` 호출부에 1줄 추가하여 `ChannelSkuFieldHistory`에 필드명 **`제조원가(개별관리)`**로 기록. NULL↔값 전환도 기록 대상(연동 해제/복귀 자체가 이력에 남아야 함)

**값 표기 규약**

`RecordFieldChange`는 `oldValue ?? ""`로 비교한 뒤 TEXT로 저장하므로, NULL을 그대로 넘기면 `""`로 정규화되어 "연동 상태"와 "빈 값"이 구분되지 않는다. 리포지토리에서 문자열로 변환한 뒤 넘긴다.

| 상태 | 기록 문자열 |
|---|---|
| NULL (마스터 연동) | `(마스터 연동)` |
| 0 (개별관리, 원가 0원) | `0` |
| 12345.5 | `12345.5` |

- 숫자는 `InvariantCulture` + `0.####` 형식으로 변환한다. 천단위 구분자(`N0`)를 쓰면 이력을 역파싱할 때 로케일 의존이 생긴다
- `(마스터 연동)`은 괄호로 시작해 어떤 숫자 표기와도 충돌하지 않는다
- 필드명은 `제조원가(개별관리)`로 고정해, 메타값 이력(`ChannelSkuPriceHistory`)과 조회 화면에서 섞이지 않게 한다

`ChannelSkuPriceHistory`는 **메타값(SupplyPrice) 전용**이므로 여기서 원가를 얹지 않는다.

### 4.4 원가 해석 규칙

발주확정/출고 시 원가 스냅샷 결정 우선순위:

```
1. 라인의 매입처(PurchaseChannelCode)가 지정되어 있고 그 매입SKU가 존재 → PurchaseSku.PurchasePrice
2. CSKU의 CostPriceOverride가 있음(NOT NULL)            → CostPriceOverride
3. 그 외                                                → ItemTable.CostPrice
```

1번이 2번보다 우선인 이유: 매입처 지정은 사용자가 그 라인에 대해 내린 **명시적·건별 의사판단**이므로, 채널 단위 기본값보다 구체적이다.

**공통 헬퍼로 통합할 것.** 현재 이 로직이 `OutboundHistoryForm.ResolveCostSnapshot()`과 `PartnerClosingRepository:186`에 따로 존재한다. 신규 `Utils/CostResolver`(가칭) 한 곳으로 모으고 두 호출부가 이를 쓰도록 한다 → 층이 3개에서 4개로 늘어나는 지금, 분산 로직을 방치하면 채널별 손익 불일치가 재발하기 쉽다.

**구현 시 유의(2026-07-31 2차 검토 반영)**: `CostResolver`는 매입가/오버라이드/마스터원가 3개 **값**을 우선순위대로 병합하는 순수 함수(`Resolve(decimal? purchasePrice, decimal? costPriceOverride, decimal masterCostPrice)`)로 두고, 리포지토리 조회 자체는 각 호출부가 계속 담당한다. 두 호출부의 "매입가를 얻는 방법"이 서로 다르기 때문이다(`OutboundHistoryForm`은 미확정 상태에서 라이브로 `PurchaseSkuRepository`를 조회하고, `PartnerClosingRepository.BuildLine`은 이미 스냅샷된 `od.PurchasePrice`를 그대로 씀). `CostResolver`가 이 차이까지 흡수해 매번 라이브 조회를 하도록 만들면 기존 동작을 바꿔버리므로, 우선순위 "규칙"만 공유하고 값을 가져오는 방식은 호출부 그대로 둔다.

> 참고: `PartnerClosingRepository:186`은 `od.PurchasePrice ?? CostPrice` 형태로, **스냅샷이 있으면 그것이 최우선**이다. 이 규칙은 변하지 않는다. 위 1~3은 "스냅샷이 없을 때 무엇으로 채우는가"를 다룬다.

### 4.5 UI 설계 (`ChannelCskuForm`)

**열 구성 변경**

| 열 | 변경 |
|---|---|
| `제조원가` | 유지(비바인딩). 표시값은 4.4절 2~3번 규칙으로 해석한 값 |
| `개별관리` (신규) | 체크박스 열. `CostPriceOverride != null`이면 체크. **이 명칭 확정: "개별관리"** |

**상태별 동작**

| 상태 | 표시 | 편집 시 동작 |
|---|---|---|
| 연동(체크 해제) | 값은 **회색**으로 표시하고 시각적 연동 표시 | 값을 고치면 **경고 다이얼로그** 표시(아래) |
| 개별관리(체크) | 일반 색상 | `CostPriceOverride`만 수정. 마스터는 건드리지 않음 |

**체크 전환 동작**

- 해제 → 체크(개별관리 시작): 현재 마스터 원가값을 그대로 `CostPriceOverride`에 복사해 넣고 편집 가능 상태로 전환(값이 갑자기 0이 되지 않게)
- 체크 → 해제(연동 복귀): "이 CSKU의 개별 원가를 삭제하고 마스터DB 원가(N원)를 따르게 합니다" 확인 후 NULL 처리

**연동 상태에서 원가를 수정하려 할 때 경고 (부작용 2.3-①·② 대응)**

`ChannelSkuRepository.GetAllByMsku(msku)`로 영향 범위를 세어 다음을 안내한다:

> 이 값은 마스터DB 공유 원가입니다. 저장하면 이 Msku를 사용하는 **N개 채널 / M개 CSKU**의 원가가 함께 바뀌고, 아직 원가 스냅샷이 없는 미확정 출고 라인의 손익도 재계산됩니다.
> [마스터 원가 변경] [이 CSKU만 개별관리로 전환] [취소]

가운데 버튼이 이번 기능의 직접적인 진입점이 된다(체크박스를 먼저 켜야만 쓸 수 있는 구조를 피함).

**미등록 마스터SKU 처리**

현행처럼 `ItemTable`에 없는 임시SKU는 마스터 원가 저장을 건너뛰고 안내한다. 단 **개별관리(오버라이드)는 `ChannelSkuTable`에만 물리므로 임시SKU여도 저장 가능**하다 → 현행 제약이 오히려 부수 효과다.

### 4.6 여파 영향 정리

| 대상 | 영향 |
|---|---|
| 마감 확정 라인(`PartnerClosingLineTable.CostPrice`) | **영향 없음** — 라인별로 원가가 복사·저장되어 있음 |
| 스냅샷이 있는 출고 라인(`OutboundDetail.PurchasePrice` NOT NULL) | **영향 없음** |
| 스냅샷이 없는 미확정 라인 | 오버라이드 지정 시 그 값으로 재계산됨 → **의도된 동작** |
| 기존 데이터 마이그레이션 | **불필요.** 전부 NULL로 시작하므로 현행 동작과 동일 |

일괄 백필은 하지 않는다(과거 값을 임의로 확정 시키는 위험이 이득보다 큼).

---

## 5. 영향 범위

| 파일 | 변경 |
|---|---|
| `Database/DbSchema.cs` | `EnsureColumn` 1줄 |
| `Models/ChannelSkuModel.cs` | `decimal? CostPriceOverride` 추가 |
| `Database/ChannelSkuRepository.cs` | Upsert 컬럼 추가, SELECT 매핑 추가(`GetAllByChannel`/`GetByChannelAndCskuCode`/`GetAllByMsku`/`GetAll`), `RecordFieldChange` 1건 추가 |
| 신규 `Utils/CostResolver.cs` | 4.4절 우선순위 값-병합 단일 함수(리포지토리 조회는 각 호출부 유지) |
| `Forms/OutboundHistoryForm.cs` | `ResolveCostSnapshot()` → `CostResolver` 사용, `ChannelSkuModel`을 한 번만 조회해 Msku/오버라이드 함께 획득(중복조회 제거) |
| `Database/PartnerClosingRepository.cs` | 186행 폴백 → `CostResolver` 사용 |
| `Forms/ChannelCskuForm.cs` | 개별관리 열 추가, 표시/편집/경고 동작 |
| `Forms/CskuPickerDialog.cs` | 109행에서 `item?.CostPrice`를 직접 읽는 부분 → `csku.CostPriceOverride ?? item?.CostPrice` 경로로 교체(매입처 맥락이 없는 화면이라 2단계만 적용) **(이번 버전 포함)** |
| `Forms/PurchaseSalesOverviewForm.cs` | **(2026-07-31 2차 검토 반영: 구조 수정)** 원가 선택지 목록(`costOptions`, 100~102행)은 `masterSku` 1개당 한 번만 만들어지는 전역 리스트이고, 그 아래 CSKU별(`sales`) 루프(104행)가 이를 반복 대입하는 구조다. "CSKU 개별원가"는 CSKU마다 다르거나 없을 수 있으므로 `costOptions`에 정적으로 추가하면 안 되고, `sales` 루프 **안에서** 해당 `s.CostPriceOverride`가 있을 때만 그 CSKU 전용 옵션을 그때그때 만들어 합쳐야 한다 **(이번 버전 포함)** |

**스키마 파괴적 변경 없음. 기존 데이터 그대로 동작.**

---

## 6. 테스트 계획

1. 마이그레이션 후 기존 DB에서 전 CSKU의 원가 표시값이 이전과 동일한지(전부 NULL=연동)
2. 개별관리 선택 → 값 수정 → 저장 시 `ItemTable.CostPrice`가 **변하지 않는지**
3. 개별관리 CSKU가 아닌 상태에서 마스터 원가를 바꿔도 그 CSKU 원가가 **따라 변하지 않는지**
4. 연동 상태 CSKU는 마스터 원가 변경에 따라가는지
5. 연동 복귀(체크 해제) 시 NULL이 저장되고 표시값이 마스터 원가로 돌아오는지
6. `ChannelSkuFieldHistory`에 연동↔개별관리 전환과 금액 변경이 각각 기록되는지, 0과 NULL이 구분되는지
7. 우선순위 검증: 매입처 지정 + 오버라이드 동시 존재 시 **매입처 매입가**가 스냅샷되는지
8. 스냅샷이 이미 있는 출고 라인은 오버라이드를 바꿔도 손익이 불변인지
9. 확정된 마감의 손익이 오버라이드 변경 후에도 불변인지
10. `ItemTable` 미등록 임시SKU에서도 오버라이드가 저장되는지
11. 연동 상태에서 원가 수정 시 경고에 표시되는 영향 채널/CSKU 수가 `GetAllByMsku` 결과와 일치하는지
12. 이력에 `(마스터 연동)` ↔ `0` 전환이 두 값의 차이로 정확히 1회 기록되는지(빈 문자열로 정규화되어 누락되지 않는지)
13. `CskuPickerDialog`에서 개별관리 CSKU를 고를 때 표시/반환 원가가 오버라이드 값인지
14. `PurchaseSalesOverviewForm`의 원가 선택지에 "CSKU 개별원가"가 노출되고, **그 CSKU에만** 적용되며 같은 Msku의 다른 CSKU 행에는 나타나지 않는지(2026-07-31 2차 검토 항목)

---

## 7. 단계별 진행

| 단계 | 산출물 | 완료 기준 |
|---|---|---|
| S1 | 스키마 + 모델 + 리포지토리(Upsert/SELECT/이력) | 테스트 1·2·6 통과 |
| S2 | `CostResolver` 신설 및 기존 2개 호출부 교체 | 테스트 7·8·9 통과 |
| S3 | `ChannelCskuForm` 개별관리 열 및 전환 동작 | 테스트 3·4·5·10 통과 |
| S4 | 연동 상태 경고 다이얼로그 + 전환 동작 | 테스트 11 통과 |
| S5 | 부속 호출부 정리(CskuPickerDialog, PurchaseSalesOverviewForm) — **이번 버전 포함** | 테스트 13·14 통과, 화면별 표시 원가 일치 확인 |

---

## 8. 확정 사항 (2026-07-31 검토 반영)

| 항목 | 결정 |
|---|---|
| 개별관리 열 명칭 | **"개별관리"** 로 확정 |
| 이력 NULL 표기 규약 | **`(마스터 연동)`** 문자열 + 숫자는 InvariantCulture `0.####` (4.3절) |
| 부속 호출부(S5) | **이번 버전에 포함** — CskuPickerDialog, PurchaseSalesOverviewForm |
| 원가 우선순위 | 매입처 매입가 > CSKU 오버라이드 > ItemTable.CostPrice (4.4절) |
| 기존 데이터 백필 | **하지 않음** — 전부 NULL 시작, 현행 동작 유지 |
| `CostResolver`의 형태 | **값 3개를 우선순위로 병합하는 순수 함수로 한정** — 리포지토리 조회는 호출부가 계속 담당(두 호출부의 매입가 조회 방식이 다르기 때문) |
| `PurchaseSalesOverviewForm`의 "CSKU 개별원가" 배치 | **CSKU별(`sales`) 루프 안에서 개별 처리** — 전역 `costOptions`에 정적으로 추가하지 않음 |
