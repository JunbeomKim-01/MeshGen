# External Outer/Inner Surface 기반 전극 부착 문제 정리
## WPF / C# / VTK 기준 설계 메모

---

## 1. 문서 목적

본 문서는 `External` mesh가 실제로는 **공기와 닿는 바깥 Surface(Outer Surface)** 와  
**내부 장기와 닿는 안쪽 Surface(Inner Surface)** 를 함께 포함하고 있을 때,  
전극 부착 과정에서 전극이 잘못하여 **Inner Surface 기준으로 붙는 문제**를 정리하고  
그 원인과 해결 방향을 설계 관점에서 명확히 정리하기 위한 문서이다.

---

## 2. 문제 현상

현재 전극 부착 과정에서 다음과 같은 문제가 발생한다.

- 사용자가 body 표면에 전극을 배치한다.
- 하지만 전극이 실제로는 공기와 맞닿는 바깥 Surface가 아니라 **안쪽 Surface** 기준으로 부착된다.
- 그 결과 최종적으로 보면 전극이 **Outer Surface를 뚫고 들어간 것처럼 보이거나**,  
  반대로 body가 전극을 뚫고 나온 것처럼 보인다.

즉 시각적으로는 전극이 body 외부에 자연스럽게 붙는 것이 아니라,  
**잘못된 내부 표면을 기준으로 정렬된 상태**가 발생한다.

---

## 3. 핵심 가설

가장 가능성이 높은 원인은 다음과 같다.

> 전극이 최초 placement 시점에 이미 Outer Surface를 일부 뚫은 상태로 생성되었고,  
> 그 이후 raycasting 또는 surface correspondence search가  
> Outer Surface가 아니라 Inner Surface를 hit 하게 된다.

즉 문제의 본질은 단순히 raycasting 자체가 아니라,

1. **부착 대상 surface 정의가 애매하고**
2. **초기 placement가 안전하지 않으며**
3. **raycast가 outer first-hit를 강제하지 않는 것**

의 조합일 가능성이 높다.

---

## 4. 구조적 원인

### 4.1 External mesh가 두 개의 surface를 포함함
`External` segmentation 또는 mesh가 실제로는 다음 두 면을 포함할 수 있다.

- **Outer Surface**: 공기와 닿는 진짜 body 외피
- **Inner Surface**: 내부 장기 또는 body 내부 구조와 맞닿는 면

따라서 `External` 전체를 그냥 attachment 대상으로 사용하면,  
알고리즘 입장에서는 어느 surface가 진짜 접촉 대상인지 구분되지 않는다.

### 4.2 Initial placement가 outer 바깥을 보장하지 않음
전극이 처음 생성되는 순간 이미 Outer Surface 안쪽으로 들어가 있으면,  
그 뒤의 raycasting은 자연스럽게 Outer보다 Inner를 먼저 보거나,  
Outer를 지나쳐 Inner를 hit 하게 된다.

### 4.3 Raycasting이 first outer hit를 강제하지 않음
raycasting이 아래 조건을 명확히 강제하지 않으면 잘못된 대응점이 나온다.

- 시작점은 outer surface 바깥
- 방향은 body 쪽
- 사용 hit는 첫 번째 유효한 surface hit
- hit normal 방향 검증 필요

이 조건이 없으면 inner surface가 correspondence로 채택될 수 있다.

---

## 5. 문제를 가장 단순하게 설명하면

현재 문제는 다음처럼 정리할 수 있다.

1. `External`은 실제로 하나의 표면이 아니다.
2. 전극이 처음부터 outer surface를 일부 뚫고 생성된다.
3. 그 상태에서 raycasting을 하면 outer가 아니라 inner를 잡는다.
4. 따라서 전극은 inner surface 기준으로 정렬된다.
5. 최종적으로는 outer surface 기준으로 전극이 body를 뚫는 것처럼 보인다.

---

## 6. 가장 중요한 설계 원칙

### 원칙 1. 전극 부착 기준은 `External 전체`가 아니라 `Outer Surface` 여야 한다
전극이 실제로 붙어야 하는 대상은 내부면이 아니라  
**공기와 닿는 outer skin-like boundary** 이다.

따라서 attachment, pick, raycast, clearance 검사는  
반드시 **Outer Surface 전용 mesh** 를 기준으로 수행해야 한다.

### 원칙 2. Initial placement 시 전극은 반드시 Outer Surface 바깥에 있어야 한다
전극 생성 직후 최소한 다음이 성립해야 한다.

- 전극 하단 샘플 점이 outer surface 내부에 있지 않다
- 최소 clearance를 유지한다

즉 전극은 attachment 시작 전에  
**“적어도 outer surface를 뚫지 않는다”** 가 보장되어야 한다.

### 원칙 3. Raycasting은 outer surface의 first valid hit만 사용해야 한다
아무 hit나 쓰면 안 된다.  
사용되는 correspondence는 반드시 **Outer Surface 기준의 첫 번째 유효 hit** 여야 한다.

---

## 7. 권장 해결 방향

### 7.1 Attachment용 Outer Surface mesh를 별도로 둔다
가장 권장되는 방식이다.

- 기존 `External mesh` 전체를 attachment 대상으로 사용하지 않는다.
- 공기와 닿는 **Outer Surface만 분리한 mesh** 를 따로 만든다.
- 전극 placement, pick, raycast, distance check는 모두 이 mesh를 기준으로 한다.

이렇게 하면 inner/outer 혼동을 구조적으로 차단할 수 있다.

### 7.2 Initial Safe Placement를 도입한다
전극이 사용자가 클릭한 위치에 생성된 직후, 다음 절차를 수행한다.

1. 전극 하단 샘플점 생성
2. 각 샘플에 대해 outer surface signed distance 평가
3. 음수 또는 허용 clearance 미만이면
4. 전극을 outer normal 방향으로 바깥쪽으로 이동

즉 correspondence search 이전에  
**전극이 이미 outer 바깥에 있다는 조건을 먼저 만족** 시켜야 한다.

### 7.3 Raycasting 규칙을 명확히 한다
raycasting은 다음 규칙을 따라야 한다.

- ray 시작점은 outer surface 바깥
- ray 방향은 body 안쪽 또는 electrode 하단 법선 반대 방향
- ray 길이는 제한한다
- outer surface 기준 first hit만 채택한다
- inner surface hit는 무시한다
- 필요하면 hit normal 방향도 검증한다

---

## 8. 권장 attachment 흐름

```text
1. User selects placement point
2. Use Outer Surface mesh only
3. Create initial electrode pose
4. Check if the electrode penetrates Outer Surface
5. If penetrated, move electrode outward until clearance is satisfied
6. Generate bottom sample points
7. Raycast against Outer Surface only
8. Use first valid hit for each sample
9. Estimate best-fit electrode pose
10. Apply local body deformation if needed
11. Validate final penetration / gap
```

---

## 9. 디버깅 시 반드시 확인할 항목

### 9.1 External mesh가 실제로 두 겹인지
단면 보기 또는 투명 렌더링으로 다음을 확인한다.

- 바깥 surface
- 안쪽 surface

둘 다 존재하는가

### 9.2 Initial placement 직후 전극 하단 샘플점의 위치
전극 생성 직후 샘플점들이

- outer surface 바깥에 있는지
- 이미 body 내부로 들어간 상태인지

를 signed distance나 시각화로 확인한다.

### 9.3 Ray 시작점과 hit 결과 시각화
ray line을 그려서 다음을 본다.

- ray가 어디서 시작하는지
- outer를 먼저 맞는지
- inner를 맞는지
- 어느 지점을 correspondence로 채택했는지

### 9.4 Hit normal 방향
hit point의 normal glyph를 시각화해  
outer surface normal인지 inner surface normal인지 확인한다.

---

## 10. 가장 가능성 높은 원인 우선순위

현재 증상을 기준으로 하면 다음 순서로 의심하는 것이 합리적이다.

### 1순위
`External` mesh 자체가 outer/inner 두 surface를 모두 포함한다.

### 2순위
전극 initial placement가 이미 outer surface를 일부 뚫고 시작한다.

### 3순위
raycast가 outer first-hit가 아니라 inner hit도 허용한다.

### 4순위
normal 방향 또는 face orientation이 일관되지 않다.

---

## 11. 구현 관점에서의 수정 포인트

### 11.1 데이터 구조
현재 `ExternalMesh` 하나만 쓰고 있다면 다음처럼 분리하는 것이 좋다.

```csharp
public sealed class SurfaceSet
{
    public MeshData ExternalFullMesh { get; set; }
    public MeshData OuterSurfaceMesh { get; set; }
    public MeshData? InnerSurfaceMesh { get; set; }
}
```

### 11.2 Placement 단계
`ElectrodePlacementController`는 클릭 시  
반드시 `OuterSurfaceMesh` 를 기준으로만 initial placement를 수행한다.

### 11.3 Correspondence 단계
`SurfaceCorrespondenceService`는 아래 규칙을 가져야 한다.

- target surface = `OuterSurfaceMesh`
- first-hit only
- hit normal validation
- maximum ray length
- fallback rejection if invalid

### 11.4 Validation 단계
전극 부착 직후 다음을 보고한다.

- outer penetration count
- max penetration depth
- gap statistics
- invalid hit count
- inner-hit rejection count

---

## 12. 추천 상태 점검 함수

```csharp
public sealed class ElectrodePlacementValidationResult
{
    public bool IsOutsideOuterSurface { get; set; }
    public int PenetratingSampleCount { get; set; }
    public double MaxPenetrationMm { get; set; }
    public double MinClearanceMm { get; set; }
    public bool HasInnerSurfaceHit { get; set; }
}
```

추천 함수 예시:

- `ValidateInitialPlacementAgainstOuterSurface()`
- `PushElectrodeOutsideOuterSurface()`
- `RaycastOuterSurfaceFirstHit()`
- `RejectInnerSurfaceCorrespondence()`

---

## 13. 품질 기준

전극 부착 결과가 정상이라고 판단하기 위한 최소 기준은 다음과 같다.

1. 전극 하단 샘플 전체가 outer surface를 뚫지 않는다.
2. correspondence는 outer surface 기준으로만 구성된다.
3. inner surface correspondence는 0개여야 한다.
4. 최종적으로 전극은 시각적으로 outer surface 위에 놓여 있어야 한다.
5. local deformation 후에도 outer surface 기준 penetration이 없어야 한다.

---

## 14. 최종 결론

현재 문제의 본질은 다음과 같이 요약할 수 있다.

> `External`이 실제로는 outer/inner 두 surface를 포함하고 있는데,  
> 전극이 initial placement 시점에 이미 outer surface를 뚫은 상태로 생성되고,  
> 그 결과 이후 raycasting이 inner surface를 기준으로 correspondence를 구성하고 있다.

따라서 해결책은 명확하다.

- `External 전체`가 아니라 **Outer Surface 전용 mesh** 를 attachment 기준으로 사용하고
- initial placement 단계에서 전극이 반드시 **Outer Surface 바깥** 에 있도록 강제하며
- raycasting은 **Outer Surface의 first valid hit만** 사용해야 한다.

이 3가지를 적용하면  
현재의 inner-surface attachment 문제는 구조적으로 크게 줄일 수 있다.

---

## 15. 한 줄 요약

**문제는 전극이 붙어야 할 대상 surface를 outer로 제한하지 않았고, initial placement도 outer 바깥을 보장하지 못해서 inner surface에 잘못 붙는 것이다.**
