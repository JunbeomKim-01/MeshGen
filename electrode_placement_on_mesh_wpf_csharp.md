# Electrode Placement on Mesh - WPF/C# + VTK 설계 문서

## 1. 목적

사용자가 3D 뷰어에서 **body mesh 표면을 클릭한 지점에 전극을 생성**할 수 있도록 하는 기능을 설계한다.

본 기능의 목표는 다음과 같다.

- Mesh 클릭 위치에서 정확한 표면 좌표를 얻는다.
- 클릭 지점의 **표면 법선(normal)** 을 계산한다.
- 전극을 해당 위치에 생성하고, 전극 축을 표면 법선 방향에 정렬한다.
- 생성 전 **미리보기(ghost electrode)** 를 제공한다.
- 생성된 전극이 나중에 **body mesh elastic deformation** 이후에도 표면에 재부착될 수 있도록 anchor 정보를 저장한다.

---

## 2. 구현 목표

### 2.1 기본 목표
- 마우스로 body mesh를 클릭하면 전극이 생성된다.
- 전극은 클릭 지점의 표면 위에 놓인다.
- 전극 방향은 클릭 지점의 법선을 따른다.
- 잘못된 객체를 클릭했을 때는 생성되지 않는다.

### 2.2 확장 목표
- 마우스 이동 중 전극 미리보기 제공
- 마우스 휠 또는 속성 패널로 전극 길이/반경 조절
- needle electrode, pad electrode 등 다양한 전극 타입 지원
- 변형된 body mesh에도 전극이 계속 따라붙도록 anchor 기반 재배치 지원

---

## 3. 전체 구조

```text
[WPF UI]
  ├─ Toolbar / Placement Button / Property Panel
  ├─ VTK Render Host
  └─ ViewModel
        ↓
[Placement Layer]
  ├─ ElectrodePlacementController
  ├─ MeshHitTestService
  ├─ ElectrodeFactory
  ├─ ElectrodeTransformBuilder
  └─ ElectrodeRepository
        ↓
[VTK Layer]
  ├─ vtkRenderer
  ├─ vtkCellPicker
  ├─ vtkPolyData / vtkActor
  └─ vtkTransform
```

---

## 4. 핵심 아이디어

전극 생성은 아래 순서로 진행한다.

1. 사용자가 배치 모드에 진입한다.
2. 마우스 커서 아래의 body mesh를 `vtkCellPicker`로 pick 한다.
3. Pick된 위치와 표면 법선을 얻는다.
4. 전극 로컬 좌표계를 기준으로 월드 변환을 계산한다.
5. 전극 미리보기 또는 실제 actor를 생성한다.
6. 전극 생성 시 클릭 당시의 **CellId / Normal / Anchor 정보**를 저장한다.

핵심은 전극이 단순히 월드 좌표에 생성되는 것이 아니라,
**"body mesh의 특정 표면 지점에 부착된 객체"** 로 생성되어야 한다는 점이다.

---

## 5. Picker 선택

### 5.1 권장 Picker
전극 배치에는 `vtkCellPicker`를 사용한다.

이유:
- Pick 위치를 월드 좌표로 얻을 수 있다.
- Pick된 `CellId`를 얻을 수 있다.
- Pick된 표면의 normal을 얻을 수 있다.
- Pick list를 이용해 body mesh actor만 선택 대상으로 제한할 수 있다.

### 5.2 비권장 Picker
`vtkPropPicker`는 단순 actor picking에는 유용하지만,
정확한 표면 좌표 및 `CellId` 추적이 필요한 전극 배치에는 적합하지 않다.

---

## 6. 배치 모드 설계

### 6.1 상태 정의

```csharp
public enum PlacementMode
{
    None,
    ElectrodePlacement
}
```

### 6.2 동작 규칙
- `None`: 일반 카메라 조작 모드
- `ElectrodePlacement`: 전극 배치 모드
  - 마우스 이동: pick + 미리보기 갱신
  - 좌클릭: 전극 확정
  - 우클릭 또는 ESC: 취소

---

## 7. 전극 로컬 좌표계 정의

전극 mesh는 생성 시 로컬 좌표계를 명확히 정의해야 한다.

### 권장 규칙
- 로컬 원점 `(0, 0, 0)` = **전극이 표면에 닿는 접촉 중심점**
- 로컬 `+Z` = **전극이 바깥으로 뻗는 방향**

이렇게 정의하면 클릭 지점 `P`, 표면 법선 `N`이 있을 때:
- 전극 축을 `+Z -> N` 으로 정렬
- 전극 위치를 `P + N * clearance` 로 설정

이 방식이 가장 단순하고 확장성이 좋다.

---

## 8. 배치 알고리즘

### 8.1 입력
- Screen mouse position `(x, y)`
- Target body mesh actor
- Electrode specification (길이, 반경, 타입)

### 8.2 출력
- Electrode actor
- Electrode placement record

### 8.3 알고리즘

1. `vtkCellPicker.Pick(x, y, 0, renderer)` 수행
2. Pick 실패 시 중단
3. Pick 결과에서 아래 값을 읽는다.
   - `PickPosition`
   - `PickNormal`
   - `CellId`
4. Normal 정규화
5. 전극 local axis `(0,0,1)` 을 `PickNormal`에 맞추는 회전 계산
6. 클릭 위치에 clearance를 더해 전극 위치 계산
7. `vtkTransform` 생성
8. 미리보기 또는 실제 actor에 transform 적용
9. 생성 기록 저장

---

## 9. 클래스 설계

### 9.1 MeshHitResult

```csharp
public sealed class MeshHitResult
{
    public bool Success { get; set; }
    public double[] Position { get; set; } = new double[3];
    public double[] Normal { get; set; } = new double[3];
    public long CellId { get; set; }
}
```

### 9.2 MeshHitTestService
역할:
- 마우스 위치로부터 body mesh surface hit test 수행
- Hit point / normal / cell id 반환

책임:
- `vtkCellPicker` 생성 및 설정
- Pick list 관리
- Pick tolerance 관리

### 9.3 ElectrodeFactory
역할:
- needle electrode, pad electrode 등 다양한 전극 polydata 생성

책임:
- 형상 생성
- 로컬 원점 규칙 보장
- 파라미터 기반 전극 mesh 생성

### 9.4 ElectrodeTransformBuilder
역할:
- Hit point + normal로부터 월드 transform 계산

책임:
- 로컬 축 정렬
- clearance 적용
- 필요 시 roll 각도 반영

### 9.5 ElectrodePlacementController
역할:
- 배치 모드 전체 제어

책임:
- 마우스 이벤트 처리
- preview actor 관리
- 클릭 시 전극 확정
- 취소 처리

### 9.6 ElectrodeRepository
역할:
- 생성된 전극 객체와 메타데이터 저장

책임:
- 추가/삭제/조회
- 선택 상태 관리
- persistence용 DTO 제공

---

## 10. 저장해야 하는 데이터

전극을 생성할 때 단순히 world position만 저장하면 안 된다.

### 필수 저장값
- `ElectrodeId`
- `ElectrodeType`
- `WorldPosition`
- `WorldNormal`
- `BodyMeshId`
- `PickedCellId`
- `Length`
- `Radius`
- `Clearance`

### 강력 권장 저장값
- `AnchorBarycentric`
- `AnchorVertexIds`
- `SurfaceFrameTangent`
- `SurfaceFrameBitangent`

이유:
body mesh가 elastic deformation으로 변형되면,
전극을 단순 world position에 두는 방식은 표면에서 떨어질 수 있다.
반면 특정 triangle의 barycentric anchor를 저장하면,
mesh 변형 후 해당 triangle의 새 위치에서 전극을 재계산할 수 있다.

---

## 11. Preview Actor 설계

### 목적
사용자가 클릭 전에 전극이 어디에 어떻게 놓일지 볼 수 있게 한다.

### 구현 방식
- 미리보기 전극 actor를 하나 생성해 둔다.
- 배치 모드 중 마우스 이동마다 pick 수행
- hit 성공 시 preview actor 위치/방향 갱신
- hit 실패 시 preview actor 숨김

### 시각적 표현
- 반투명 재질
- 실제 전극보다 밝은 색
- 선택 상태와 구분되는 색상 사용

---

## 12. 전극 타입별 차이

### 12.1 Needle Electrode
특징:
- 축대칭
- 위치 + 법선만으로 거의 충분

필요 정보:
- Position
- Normal
- Length
- Radius

### 12.2 Pad Electrode
특징:
- 법선만으로는 방향이 결정되지 않음
- 표면 접평면 안에서의 회전(roll) 필요

추가 설계:
- 첫 클릭: 위치 + 법선 결정
- 드래그 또는 별도 입력: roll angle 결정
- 또는 카메라 up vector를 접평면에 projection하여 초기 tangent로 사용

---

## 13. Transform 계산 방식

### 13.1 목표
전극 로컬 `+Z`를 `PickNormal`에 정렬한다.

### 13.2 계산 절차
- `localAxis = (0,0,1)`
- `targetAxis = normalized(PickNormal)`
- `rotationAxis = cross(localAxis, targetAxis)`
- `angle = acos(dot(localAxis, targetAxis))`
- `vtkTransform.RotateWXYZ(angle, rotationAxis)`
- `vtkTransform.Translate(hit + normal * clearance)`

### 13.3 예외 처리
- dot가 거의 1이면 회전 생략
- dot가 거의 -1이면 180도 회전 처리
- cross 길이가 매우 작으면 fallback 축 사용

---

## 14. 법선 안정화

Mesh가 거칠거나 삼각형 품질이 낮으면 클릭 위치에 따라 법선이 튈 수 있다.

### 해결책
- body mesh에 대해 `vtkPolyDataNormals` 사전 계산
- 필요 시 point normal 사용
- 경계가 강한 경우 cell normal 사용
- sharp edge splitting 여부를 상황에 따라 조절

### 권장 전략
- 피부/장기와 같이 연속적인 표면: point normal 우선
- 전극 방향이 지나치게 흔들리면 이웃 cell 기반 평균 normal 사용

---

## 15. UI 이벤트 흐름

### 15.1 배치 시작
- 사용자가 `Place Electrode` 버튼 클릭
- PlacementMode = ElectrodePlacement
- Preview actor 생성 및 활성화

### 15.2 마우스 이동
- `MeshHitTestService.Pick(mouseX, mouseY)` 수행
- hit 성공 시 preview actor 갱신
- 실패 시 preview actor 숨김

### 15.3 좌클릭
- 현재 preview 상태를 확정
- Electrode actor 생성
- Repository에 저장
- Renderer에 추가

### 15.4 취소
- ESC 또는 우클릭
- preview actor 제거 또는 숨김
- PlacementMode = None

---

## 16. 변형 Mesh와의 연동

body mesh가 elastic deformation으로 변형될 수 있다면,
전극은 mesh 표면의 특정 위치에 **재부착**될 수 있어야 한다.

### 권장 방식
생성 시 아래를 저장한다.
- pick된 triangle의 vertex ids
- 해당 triangle에서의 barycentric coordinates
- pick 당시 법선

### 재계산 방식
1. 변형 후 triangle의 새 vertex 위치를 읽는다.
2. barycentric coordinates로 새 world position 계산
3. 변형된 triangle normal 재계산
4. 새 위치/법선으로 전극 transform 다시 계산

이렇게 하면 body 변형 후에도 전극이 자연스럽게 따라간다.

---

## 17. 예외 상황 처리

### 17.1 pick 실패
- 배경 클릭
- 다른 actor 클릭
- body mesh 외 영역 클릭

처리:
- 전극 생성 금지
- preview 숨김

### 17.2 normal 이상
- 길이가 0에 가까운 normal
- 뒤집힌 normal

처리:
- fallback normal 사용
- mesh normal 재계산
- body 외부 방향 기준으로 normal 방향 검증

### 17.3 전극 관통
- clearance가 부족해 전극이 mesh 내부로 파고드는 경우

처리:
- minimum clearance 적용
- 생성 후 penetration 검사
- 필요 시 법선 방향으로 자동 보정

### 17.4 비정상 mesh
- non-manifold
- hole 존재
- self-intersection

처리:
- 배치 전 mesh validation 단계 권장

---

## 18. 최소 구현 범위 (MVP)

### 1단계
- Needle electrode만 지원
- 클릭으로 위치 + 법선 기반 생성
- Preview actor 지원
- PickedCellId 저장

### 2단계
- Body actor만 pick list에 제한
- 전극 길이/반경 조절 UI 추가
- placement 취소/확정 UX 개선

### 3단계
- Barycentric anchor 저장
- 변형 mesh 추적 재배치
- Pad electrode 및 roll 조절 추가

---

## 19. 테스트 항목

### 19.1 기능 테스트
- body mesh 클릭 시 전극 생성되는가
- body 외 공간 클릭 시 생성되지 않는가
- 전극이 클릭 위치에 놓이는가
- 전극 축이 표면 법선을 따르는가

### 19.2 시각 테스트
- preview actor가 커서를 잘 따라오는가
- 빠른 마우스 이동 시 깜빡임 없는가
- 카메라 회전 후에도 pick 정확도가 유지되는가

### 19.3 데이터 테스트
- 생성된 전극의 world position 저장되는가
- picked cell id가 올바르게 저장되는가
- deformation 후 anchor 기반 재계산이 가능한가

### 19.4 예외 테스트
- mesh hole 근처에서 이상 동작 없는가
- normal이 급격히 튀는 영역에서 방향이 안정적인가
- 비정상 mesh 입력 시 안전하게 실패하는가

---

## 20. 권장 구현 순서

1. `MeshHitTestService` 구현
2. `ElectrodeFactory` 구현
3. `ElectrodeTransformBuilder` 구현
4. `ElectrodePlacementController` 구현
5. preview actor 연결
6. repository + persistence 연결
7. anchor 저장 및 deformation 연동 추가

---

## 21. 결론

전극 배치 기능은 단순히 클릭 좌표에 객체를 생성하는 기능이 아니라,
**body mesh의 표면 지오메트리에 정합된 상태로 전극을 생성하고 관리하는 기능**이다.

따라서 구현 시 다음 원칙을 지켜야 한다.

- `vtkCellPicker` 기반의 정확한 surface picking 사용
- 전극 로컬 좌표계를 명확히 정의
- Position뿐 아니라 Normal, CellId, Anchor를 저장
- Preview actor 기반 UX 제공
- 향후 body deformation에 대응할 수 있도록 anchor 기반 재배치 구조 확보

이 원칙을 따르면 현재의 WPF/C# + VTK 구조에서 안정적이고 확장 가능한 전극 배치 기능을 구현할 수 있다.
