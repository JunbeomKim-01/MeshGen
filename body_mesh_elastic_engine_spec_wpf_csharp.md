# CT 세그먼트 기반 Body Mesh Elastic Deformation Engine 설계 문서 (WPF + C#)

## 1. 문서 목적

이 문서는 **CT 기반 이미지 세그먼트로부터 생성한 body mesh를 elastic하게 변형하여 electrode mesh를 표면에 자연스럽게 부착하는 엔진**을
**WPF + C# 애플리케이션 환경에서 구현하기 위한 기준 설계 문서**이다.

핵심 목표는 다음과 같다.

- CT segmentation에서 생성한 **body surface mesh**를 입력으로 사용한다.
- electrode mesh를 body 표면에 부착하기 위해 **국소적이고 부드러운 비강체 변형(non-rigid deformation)** 을 수행한다.
- 1차 구현은 **surface deformation 중심**으로 설계하고, 이후 필요 시 **volumetric FEM 기반 물리 모델**로 확장 가능하게 한다.
- UI(WPF)와 엔진(geometry/deformation)을 분리하여 유지보수성과 테스트 용이성을 확보한다.
- C# 기준으로 먼저 구현하되, 성능 병목이 발생하면 추후 native solver backend로 확장 가능하게 한다.

---

## 2. 문제 정의

### 2.1 입력 데이터

- CT segmentation volume 또는 segmentation 결과물
- segmentation에서 추출한 **body surface mesh**
- 부착 대상인 **electrode mesh**

### 2.2 원하는 결과

- electrode mesh가 body 표면에 자연스럽게 밀착된다.
- deformation은 electrode 주변에서 강하게 발생하고, 멀리 떨어진 부위는 최대한 원형상을 유지한다.
- self-intersection, triangle flip, 과도한 stretching이 최소화된다.
- 향후 전기장 해석, dose 분석, contact 면적 계산 등에 재사용 가능해야 한다.

### 2.3 구현 관점 핵심 구분

이 문제는 크게 두 가지로 나뉜다.

1. **Surface-constrained deformation**
   - 표면만 부드럽게 휘게 만들면 충분한 경우
   - electrode 부착 자체가 목적일 때 가장 현실적이다.

2. **Volumetric elastic deformation**
   - 내부 조직까지 실제 탄성체처럼 일관되게 변형되어야 하는 경우
   - 압력, 응력, strain energy, volumetric consistency가 중요할 때 필요하다.

**1차 구현 목표는 1번(surface deformation)으로 한다.**

---

## 3. 권장 개발 전략

### 3.1 가장 현실적인 1차 전략

WPF + C# 환경에서는 다음 전략이 가장 좋다.

- **UI와 변형 엔진을 분리**한다.
- 처음에는 **surface deformation**만 구현한다.
- body mesh 전체를 FEM 탄성체로 만들기보다,
  **electrode contact patch를 handle constraint로 만들어 국소적으로 변형**한다.
- deformation solver는 우선순위를 다음과 같이 둔다.
  1. TPS 또는 Laplacian 계열의 쉬운 프로토타입
  2. Biharmonic displacement solver
  3. 필요 시 ARAP refinement
  4. 최종적으로 FEM 확장

### 3.2 개발 우선순위

1. body mesh 전처리
2. electrode rigid pre-alignment
3. contact patch 추출
4. handle constraint 생성
5. surface deformation 적용
6. 충돌/품질 검사
7. 결과 시각화 및 저장
8. 성능 개선 및 native backend 확장

---

## 4. 전체 아키텍처

```text
[WPF UI Layer]
  - MainWindow
  - ViewModels
  - Commands
  - Property Panels
  - Result Inspector

            |
            v

[Application Service Layer]
  - EngineFacade
  - SessionService
  - MeshImportService
  - DeformationWorkflowService
  - VisualizationService

            |
            v

[Core Engine Layer]
  - BodyMeshElasticEngine
  - BodyMeshPreprocessor
  - ElectrodeAligner
  - ContactPatchExtractor
  - ConstraintBuilder
  - SurfaceDeformer
  - CollisionInspector
  - MeshQualityValidator
  - ResultExporter

            |
            v

[Geometry / Solver Backend]
  - VTK wrapper or geometry adapter
  - C# numeric backend
  - Optional native solver backend (future)
```

### 4.1 설계 원칙

- **WPF는 화면과 사용자 흐름만 담당**한다.
- **엔진은 UI를 몰라야 한다.**
- deformation 로직은 `ViewModel`이 아니라 `Engine/Service` 계층에 둔다.
- Viewer와 deformation solver를 분리한다.
- solver 입력은 가능하면 `double[]`, `Vector3`, `int[]` 같은 **UI 비의존 순수 데이터 구조**를 사용한다.

---

## 5. 프로젝트 구조 제안

```text
BodyMeshElasticApp/
  BodyMeshElasticApp.Wpf/
    Views/
    ViewModels/
    Commands/
    Converters/
    Controls/
    App.xaml
    MainWindow.xaml

  BodyMeshElasticApp.Application/
    Services/
    Facades/
    DTOs/
    Interfaces/

  BodyMeshElasticApp.Core/
    Engine/
    Geometry/
    Deformation/
    Constraints/
    Validation/
    Models/
    Utils/

  BodyMeshElasticApp.Infrastructure/
    MeshIO/
    Rendering/
    VtkAdapter/
    NativeInterop/       (optional)
    Serialization/

  BodyMeshElasticApp.Tests/
    Core/
    Application/
```

### 5.1 프로젝트 역할

#### `BodyMeshElasticApp.Wpf`
- WPF 화면
- MVVM 바인딩
- 사용자 입력 수집
- 렌더 뷰 호스팅

#### `BodyMeshElasticApp.Application`
- 유스케이스 단위 서비스
- 세션 관리
- 워크플로 orchestration
- UI와 Core 사이 연결

#### `BodyMeshElasticApp.Core`
- 핵심 알고리즘
- deformation 계산
- constraint 생성
- 품질 검사
- geometry data model

#### `BodyMeshElasticApp.Infrastructure`
- 파일 입출력
- 렌더링 라이브러리 연결
- 외부 라이브러리 어댑터
- native interop

---

## 6. WPF 관점 권장 UI 구조

### 6.1 화면 구성 예시

- 좌측: 데이터 트리 / Mesh 목록
- 중앙: 3D Viewer
- 우측: electrode alignment / deformation 파라미터 패널
- 하단: 로그 / 품질 리포트 / 디버그 정보

### 6.2 MVVM 역할 분리

#### View
- 버튼, 슬라이더, 텍스트박스, 체크박스, 3D 뷰 호스트

#### ViewModel
- 상태 보관
- Command 연결
- Service 호출
- 결과 표시용 모델 전달

#### Service
- 실제 알고리즘 실행
- mesh import / preprocess / deform / validate 수행

### 6.3 금지할 구조

다음을 `MainWindow.xaml.cs`에 몰아넣지 않는다.

- mesh 전처리
- correspondence 계산
- solver 실행
- collision 검사
- export

이런 로직은 반드시 `Application/Core`로 이동시킨다.

---

## 7. 핵심 처리 파이프라인

## 7.1 Step 1 - Body mesh 생성 및 전처리

### 입력
- CT segmentation volume 또는 segmentation에서 이미 생성된 surface mesh

### 출력
- 분석/변형 가능한 body mesh

### 수행 항목
- segmentation에서 surface mesh 추출
- disconnected component 제거
- hole / topology 문제 점검
- smoothing
- 필요 시 decimation 또는 remeshing
- normal 재계산

### 구현 포인트
- 초기 mesh 품질이 deformation 품질을 크게 좌우한다.
- 과도한 smoothing은 해부학적 형상을 손상시킬 수 있으므로 제한적으로 적용한다.
- 가능한 한 watertight에 가까운 상태를 유지한다.

---

## 7.2 Step 2 - Electrode rigid pre-alignment

### 목적
elastic deformation 전에 electrode를 body 표면 근처로 대략 정렬한다.

### 이유
- deformation solver가 감당해야 하는 변형량 감소
- correspondence 오류 감소
- 안정적인 수렴 유도

### 방법
- 수동 landmark 기반 정렬
- electrode 중심/법선 방향 정렬
- ICP 기반 정렬(선택)

### 출력
- body 표면 근처로 이동된 electrode pose

---

## 7.3 Step 3 - Contact patch 추출

### 목적
body 전체를 변형하지 않고, electrode와 실제로 맞닿아야 하는 국소 영역만 deformation 대상으로 지정한다.

### 방법 예시
- electrode underside 샘플링
- 각 샘플 점에 대해 body surface에서 closest point 계산
- 일정 거리 이내 vertex를 contact 후보로 수집
- 1-ring 또는 geodesic 기반 확장으로 patch 생성

### 출력
- `ContactVertexIds`
- `ContactBoundaryVertexIds`
- `ContactPatch`

---

## 7.4 Step 4 - Constraint 생성

### 목적
solver가 따라야 할 제약 조건을 정의한다.

### 제약 종류

#### A. Contact handles
- electrode에 맞춰 실제로 이동해야 하는 body 점들
- 가장 강한 제약

#### B. Soft neighborhood constraints
- contact patch 주변 링 영역
- 완만한 전이를 유도

#### C. Fixed anchors
- body의 멀리 떨어진 영역
- 전체 body가 함께 끌려가는 현상을 방지

### 핵심 원칙
- **contact 영역과 anchor 영역을 동시에 지정**해야 한다.
- anchor가 없으면 전체 mesh가 이동하거나 회전할 수 있다.

---

## 7.5 Step 5 - Surface deformation solver

1차 구현은 다음 두 가지 선택지 중 하나로 시작하는 것을 권장한다.

### 선택지 A - TPS 기반 프로토타입
- 대응점(source-target landmark)이 있다면 빠르게 구현 가능
- contact patch에 landmark를 충분히 뿌릴 수 있을 때 유리
- 장점: 구현이 빠름
- 단점: 제약이 복잡해질수록 세밀한 제어가 어렵다

### 선택지 B - Biharmonic displacement solver
- 보다 일반적인 handle-based deformation
- contact handle과 fixed anchor를 함께 처리하기 좋다
- shape preservation 측면에서 더 안정적이다

### 권장 결론
- **초기 프로토타입:** TPS 또는 단순 Laplacian
- **실사용 엔진:** Biharmonic displacement

---

## 7.6 Step 6 - ARAP refinement (선택)

### 적용 시점
다음과 같은 경우 biharmonic만으로 부족할 수 있다.

- 변형량이 큼
- 회전 성분이 큼
- patch가 넓고 geometry가 복잡함
- triangle distortion이 눈에 띔

### 역할
- local rigidity를 더 잘 유지
- triangle distortion 감소
- contact 주변 shape quality 개선

### 전략
- biharmonic 결과를 초기값으로 사용
- 그 위에 ARAP 반복 보정 수행

---

## 7.7 Step 7 - 충돌 및 품질 검사

### 검사 항목
- electrode penetration
- triangle flip
- self-intersection
- local area distortion
- normal 방향 뒤집힘
- 과도한 displacement

### 조치 예시
- penetration 발생 시 back-off
- local smoothing
- constraint weight 재조정
- ARAP 재실행

---

## 7.8 Step 8 - 결과 산출

### 출력물
- deformed body mesh
- original body mesh
- aligned electrode mesh
- contact patch
- displacement field
- deformation quality report

### 저장 형식
- `.json`, `.csv` : 수치 결과
- `.stl`, `.ply`, `.obj`, `.vtp` : mesh 결과

---

## 8. Core 도메인 모델 설계

## 8.1 기본 데이터 구조

```csharp
public sealed class MeshData
{
    public List<Vector3> Vertices { get; } = new();
    public List<int> Indices { get; } = new();
    public List<Vector3>? Normals { get; set; }
}

public sealed class ConstraintPoint
{
    public int VertexId { get; set; }
    public Vector3 TargetPosition { get; set; }
    public double Weight { get; set; }
    public bool IsHardConstraint { get; set; }
}

public sealed class ContactPatch
{
    public List<int> VertexIds { get; } = new();
    public List<int> BoundaryVertexIds { get; } = new();
}
```

---

## 8.2 엔진 설정 모델

```csharp
public sealed class ElasticEngineConfig
{
    public int SmoothingIterations { get; set; } = 20;
    public double SmoothingLambda { get; set; } = 0.2;

    public double ContactDistanceThresholdMm { get; set; } = 2.0;
    public int ContactExpansionRing { get; set; } = 2;

    public bool UseTpsPrototype { get; set; } = false;
    public bool UseBiharmonic { get; set; } = true;
    public bool UseArapRefinement { get; set; } = true;
    public int ArapIterations { get; set; } = 5;

    public double HardConstraintWeight { get; set; } = 1.0;
    public double SoftConstraintWeight { get; set; } = 0.25;

    public bool EnableCollisionCheck { get; set; } = true;
    public bool EnableQualityValidation { get; set; } = true;
}
```

---

## 8.3 세션 모델

```csharp
public sealed class ElasticEngineSession
{
    public MeshData? OriginalBodyMesh { get; set; }
    public MeshData? PreprocessedBodyMesh { get; set; }
    public MeshData? ElectrodeMesh { get; set; }
    public MeshData? AlignedElectrodeMesh { get; set; }
    public MeshData? DeformedBodyMesh { get; set; }

    public ContactPatch? ContactPatch { get; set; }
    public List<int> AnchorVertexIds { get; } = new();
}
```

---

## 9. 엔진 모듈 설계

## 9.1 `IBodyMeshElasticEngine`

```csharp
public interface IBodyMeshElasticEngine
{
    void SetBodyMesh(MeshData bodyMesh);
    void SetElectrodeMesh(MeshData electrodeMesh);

    Task PreprocessBodyMeshAsync(CancellationToken cancellationToken = default);
    Task AlignElectrodeRigidAsync(CancellationToken cancellationToken = default);
    Task ExtractContactPatchAsync(CancellationToken cancellationToken = default);
    Task BuildConstraintsAsync(CancellationToken cancellationToken = default);
    Task DeformSurfaceAsync(CancellationToken cancellationToken = default);
    Task RefineDeformationAsync(CancellationToken cancellationToken = default);
    Task ValidateResultAsync(CancellationToken cancellationToken = default);

    MeshData? GetDeformedBodyMesh();
    ContactPatch? GetContactPatch();
}
```

---

## 9.2 `BodyMeshPreprocessor`

### 책임
- mesh cleaning
- smoothing
- normal 재계산
- topology sanity check

```csharp
public interface IBodyMeshPreprocessor
{
    MeshData Clean(MeshData mesh);
    MeshData Smooth(MeshData mesh, int iterations, double lambda);
    MeshData RecomputeNormals(MeshData mesh);
    bool ValidateTopology(MeshData mesh);
}
```

---

## 9.3 `ElectrodeAligner`

### 책임
- electrode rigid pre-alignment
- manual transform 적용
- landmark 기반 초기 정렬
- optional ICP

```csharp
public interface IElectrodeAligner
{
    MeshData Align(MeshData bodyMesh, MeshData electrodeMesh, ElectrodeAlignmentOptions options);
}
```

---

## 9.4 `ContactPatchExtractor`

### 책임
- closest point 계산
- contact candidate 생성
- patch 확장

```csharp
public interface IContactPatchExtractor
{
    ContactPatch Extract(MeshData bodyMesh, MeshData electrodeMesh, ContactPatchOptions options);
}
```

---

## 9.5 `ConstraintBuilder`

### 책임
- hard/soft/fixed constraint 생성
- solver 입력용 target displacement 생성

```csharp
public interface IConstraintBuilder
{
    IReadOnlyList<ConstraintPoint> Build(
        MeshData bodyMesh,
        MeshData alignedElectrodeMesh,
        ContactPatch contactPatch,
        IReadOnlyList<int> anchorVertexIds,
        ElasticEngineConfig config);
}
```

---

## 9.6 `SurfaceDeformer`

### 책임
- TPS solve
- biharmonic solve
- optional ARAP refinement

```csharp
public interface ISurfaceDeformer
{
    MeshData Deform(
        MeshData bodyMesh,
        IReadOnlyList<ConstraintPoint> constraints,
        ElasticEngineConfig config);
}
```

---

## 9.7 `MeshQualityValidator`

### 책임
- deformation 품질 평가
- distortion / flipped triangle / displacement 통계 계산

```csharp
public sealed class DeformationQualityReport
{
    public double MaxDisplacement { get; set; }
    public double MeanDisplacement { get; set; }
    public double MaxAreaStretch { get; set; }
    public double FlippedTriangleRatio { get; set; }
    public bool HasSelfIntersection { get; set; }
    public bool HasPenetration { get; set; }
}

public interface IMeshQualityValidator
{
    DeformationQualityReport Validate(
        MeshData originalMesh,
        MeshData deformedMesh,
        MeshData electrodeMesh);
}
```

---

## 10. WPF + C# 구현 시 기술 선택 가이드

## 10.1 Viewer와 Solver를 분리하라

가장 중요한 원칙은 다음과 같다.

- **Viewer는 보는 용도**
- **Solver는 계산 용도**

즉, 렌더링 라이브러리 객체를 solver 내부로 끌고 들어가지 않는다.

좋은 흐름:

1. 렌더링 객체 또는 파일에서 mesh를 읽는다.
2. 이를 `MeshData` 같은 Core 데이터로 변환한다.
3. Core에서 계산한다.
4. 결과 mesh를 다시 Viewer용 데이터로 변환한다.

---

## 10.2 C# 순수 구현 vs native backend

### A. 순수 C# 구현

추천 상황:
- 먼저 알고리즘을 빨리 검증하고 싶을 때
- patch 규모가 아주 크지 않을 때
- 구조를 단순하게 유지하고 싶을 때

구성 예:
- `System.Numerics.Vector3`
- sparse matrix 연산 라이브러리
- custom Laplacian / biharmonic solver

장점:
- 디버깅 쉬움
- WPF와 통합 쉬움
- 배포 단순

단점:
- 대형 mesh에서는 성능 한계 가능
- ARAP / 고급 solver 구현 비용 증가

### B. C# + native solver backend

추천 상황:
- 대형 mesh
- 반복 계산 많음
- 고성능 solver 필요

구성 예:
- C# Core API 유지
- native C++ solver DLL 분리
- P/Invoke 또는 C++/CLI wrapper 사용

장점:
- 성능 좋음
- 기존 geometry library 활용 가능

단점:
- 빌드/배포 복잡
- interop 비용 발생

### 권장 결론

- **V1:** C# 중심 구현
- **V2:** 병목 구간만 native backend로 교체

---

## 11. 개발 로드맵

## Phase 1 - 최소 기능 구현

목표:
- body mesh 로드
- electrode mesh 로드
- 수동 정렬
- contact patch 표시
- TPS 또는 단순 Laplacian deformation
- 결과 렌더링

완료 기준:
- electrode가 body 표면에 대략 밀착
- deformation 결과가 시각적으로 자연스러움

---

## Phase 2 - 엔진 구조 안정화

목표:
- MVVM 분리
- Core/Application/Infrastructure 계층 분리
- session / config / export 구조 확립
- 품질 리포트 추가

완료 기준:
- UI 없이도 Core unit test 가능
- 저장/불러오기 가능

---

## Phase 3 - 고급 deformation

목표:
- biharmonic displacement solver
- ARAP refinement
- anchor 자동 선택
- 충돌 검사 개선

완료 기준:
- 큰 변형에서도 triangle quality 유지
- 반복 실험 가능한 수준의 안정성 확보

---

## Phase 4 - FEM 확장

목표:
- tet mesh 생성
- 선형 탄성 또는 hyperelastic 모델 연결
- 내부 변형장 계산

완료 기준:
- surface 변형을 넘어 volumetric consistency 확보

---

## 12. 테스트 전략

### 12.1 Unit Test

대상:
- contact patch 추출
- constraint 생성
- anchor 선택
- displacement 계산
- quality metric 계산

### 12.2 Integration Test

대상:
- body mesh import → preprocess → align → deform → validate 전체 흐름

### 12.3 Viewer Test

대상:
- 원본/변형 mesh 토글
- contact patch overlay
- electrode penetration 시각화

### 12.4 Regression Test

대상:
- 동일 입력에서 deformation 결과가 급격히 달라지지 않는지 검증

---

## 13. 구현상 주의점

1. 처음부터 FEM으로 가지 않는다.
2. deformation 로직을 WPF code-behind에 넣지 않는다.
3. 렌더링 객체를 Core 엔진의 기본 데이터 구조로 삼지 않는다.
4. anchor 없는 deformation을 피한다.
5. body 전체를 변형시키기보다 contact 중심 국소 변형부터 시작한다.
6. 대형 mesh는 downsample 버전으로 먼저 계산하고 최종 단계에서 정교화하는 전략을 고려한다.
7. 모든 단계에서 intermediate result를 저장 가능하게 만든다.

---

## 14. 최종 권장 결론

WPF + C# 환경에서 이 엔진을 구현할 때 가장 좋은 방향은 다음과 같다.

- **WPF는 UI와 Viewer 호스팅만 담당**한다.
- **Core 엔진은 C# 순수 도메인 모델 중심으로 설계**한다.
- 처음에는 **surface deformation 기반 엔진**을 만든다.
- deformation 방식은 **contact patch + fixed anchor + biharmonic displacement**를 중심으로 설계한다.
- 성능이 부족해질 때만 native backend를 추가한다.

즉, 구현 전략을 한 줄로 요약하면 다음과 같다.

> **WPF 앱 위에 C# 기반 deformation engine을 올리고, 초기 버전은 surface handle deformation으로 구현한 뒤, 필요할 때만 native solver나 FEM으로 확장한다.**
