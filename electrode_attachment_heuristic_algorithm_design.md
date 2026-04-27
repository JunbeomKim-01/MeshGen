# Electrode Attachment Heuristic Algorithm Design
## Based on Boundary/Surface Sampling + Rigid Pose Fit + Local Body Deformation
### WPF / C# / VTK 기준 설계 문서

---

## 1. 문서 목적

본 문서는 다음 아이디어를 바탕으로 전극 부착 알고리즘을 설계한다.

1. 굴곡이 큰 body surface에서는 rigid electrode를 그대로 부착하면 한쪽이 뜨거나 body를 관통하는 문제가 발생한다.
2. 전극은 최소한 **external body 바깥에 존재해야 한다**는 규칙을 먼저 만족해야 한다.
3. 전극 하단면의 샘플 점들을 이용하여 실제 body surface의 대응 위치를 탐색한다.
4. 탐색된 접촉 후보점들로부터 전극의 최적 자세를 다시 계산한다.
5. 남는 penetration 또는 overlap은 전극 하단면을 기준으로 body를 국소적으로 변형하여 보정한다.

이 문서는 위 아이디어를 제품/엔진 구현이 가능한 수준으로 정리하고, 어디가 강점이고 어디가 위험한지 객관적으로 평가한 뒤, 보다 안정적인 알고리즘 형태로 재구성한다.

---

## 2. 핵심 판단

### 2.1 장점
이 아이디어는 다음 이유로 매우 실용적이다.

- FEM 없이 구현 가능하다.
- WPF/C# + VTK 구조에 잘 맞는다.
- 사용자가 원하는 결과인 **전극이 body에 자연스럽게 붙는 모양**을 빠르게 만들 수 있다.
- 물성치 없이도 동작 가능하다.
- 규칙 기반 heuristic attachment engine으로 MVP 구현에 적합하다.

### 2.2 한계
원래 아이디어를 그대로 구현하면 다음 문제가 발생할 수 있다.

- 곡면 위 접촉점을 **하나의 평면으로 모두 만족**시키는 것이 일반적으로 불가능하다.
- 전극의 **최외곽 vertex만** 사용하면 중앙 penetration이나 floating을 놓칠 수 있다.
- 마지막 단계에서 body mesh를 전극 하단면에 강제로 맞추면 표면이 잘린 듯한 부자연스러운 결과가 날 수 있다.
- raycasting 방향이 불안정하면 대응점이 튀거나 잘못된 면을 잡을 수 있다.

### 2.3 결론
따라서 이 아이디어는 다음처럼 해석하는 것이 가장 적절하다.

- 본 알고리즘은 **물리 기반 접촉 해석**이 아니라
- **rigid pose fitting + local surface-constrained deformation** 기반의 heuristic attachment이다.

이 정의를 명확히 하면 설계 방향이 명확해진다.

---

## 3. 기본 원칙

이 알고리즘은 아래 원칙을 따른다.

1. 전극은 항상 body 바깥에 있어야 한다.
2. rigid electrode의 자세는 접촉 후보점들에 대해 **최적 근사(best fit)** 로 계산한다.
3. body는 전극 하단면을 직접 절단하듯 수정하지 않고, **국소적 부드러운 변형(local smooth deformation)** 으로 보정한다.
4. 경계점만 보지 않고, **하단 경계 + 내부 샘플 포인트**를 함께 사용한다.
5. 결과 품질 평가는 penetration, floating gap, contact coverage, deformation smoothness로 수행한다.

---

## 4. 적용 대상

본 알고리즘은 다음 조건에 적합하다.

- needle electrode, pad electrode, flat electrode 등의 배치
- 피부 또는 장기 표면에 전극이 접촉하는 장면
- 변형은 필요하지만 FEM 수준의 물리성은 아직 요구되지 않는 경우
- 빠른 미리보기, 상호작용형 부착, sequential attachment가 필요한 경우

다음 경우에는 한계가 있다.

- 실제 압력, 응력, strain 해석이 필요한 경우
- 재료별 강성 차이를 정량적으로 반영해야 하는 경우
- 두꺼운 연조직 압입 현상을 물리적으로 계산해야 하는 경우
- 동적 시뮬레이션이 필요한 경우

---

## 5. 원래 아이디어에 대한 객관적 재해석

사용자 아이디어를 아래처럼 재해석한다.

### 원래 1~2
- 굴곡이 심한 곳에서 전극이 뜨거나 body를 뚫는다.
- 최소 penetration 규칙을 적용해 일단 전극을 body 바깥에 둔다.

### 원래 3~4
- 전극의 외곽 vertex를 raycasting해서 body의 실제 접촉 후보점을 찾는다.
- 해당 좌표를 수집한다.

### 원래 5~6
- 수집된 좌표를 모두 만족하는 평면을 구한다.
- 그 평면에 맞게 전극을 다시 배치한다.

### 원래 7~8
- 여전히 겹치거나 뚫고 나오는 부분이 생길 수 있다.
- 전극 하단면에 맞게 body를 수정한다.

이 흐름은 충분히 타당하지만, 다음과 같이 바꾸는 것이 더 안전하다.

---

## 6. 개선된 Attachment Algorithm 1.0

### 단계 A. Initial Safe Pose
전극이 최소한 body 바깥에 있도록 초기 자세를 만든다.

- 클릭 위치 또는 사용자 지정 위치를 기준으로 전극 중심을 잡는다.
- body surface normal을 기준으로 전극의 축 또는 면 방향을 정렬한다.
- signed distance 또는 projection 검사로 전극 전체가 body 외부에 있도록 normal 방향으로 밀어낸다.
- 이 단계의 목표는 **좋은 접촉**이 아니라 **비정상 penetration 방지**다.

### 단계 B. Electrode Bottom Sampling
전극 하단면에서 접촉 판단용 샘플 점들을 만든다.

샘플 종류:
- boundary vertices
- 내부 grid 샘플
- corner / extremal samples

권장:
- 경계점만 쓰지 않는다.
- 내부 샘플을 반드시 포함한다.

이유:
- boundary는 잘 맞지만 중앙이 뜨거나 뚫는 상황을 놓치지 않기 위해서다.

### 단계 C. Surface Correspondence Search
각 샘플 점이 실제로 붙어야 할 body surface 위치를 찾는다.

권장 방법:
- 샘플 점에서 전극 하단 법선 방향 또는 body normal 보정 방향으로 raycast
- 최근접 surface projection
- signed distance gradient 기반 최근접점 탐색

각 샘플에 대해 아래 정보를 저장한다.

- source sample point
- hit point
- hit normal
- hit distance
- hit cell id
- success / fail 상태

### 단계 D. Best-Fit Rigid Pose Estimation
수집한 hit point 집합에 대해 전극 rigid pose를 다시 계산한다.

중요:
- 이 단계는 “모든 점을 지나는 평면”을 구하는 것이 아니다.
- 실제 의미는 **best-fit plane / best-fit rigid transform** 이다.

권장 구현:
- pad electrode: best-fit plane + in-plane rotation
- needle electrode: anchor point + averaged normal
- 일반 rigid electrode: Kabsch / Procrustes 기반 rigid fit

이 단계의 목표는
- 모든 접촉점을 완벽히 만족시키는 것이 아니라
- 현재 shape에서 가능한 **최적 근사 자세**를 찾는 것이다.

### 단계 E. Penetration and Gap Evaluation
새로운 pose에서 전극 하단면 전체 샘플에 대해 품질을 검사한다.

검사 항목:
- maximum penetration depth
- mean penetration depth
- maximum floating gap
- mean floating gap
- contact coverage ratio
- outlier point count

### 단계 F. Local Body Deformation
겹침 또는 과도한 gap이 남아 있는 body 영역만 국소적으로 변형한다.

여기서 중요한 점:
- body를 전극 하단면에 강제로 잘라 맞추지 않는다.
- 전극 하단면을 hard constraint로 두고
- 주변 표면은 soft constraint와 smoothing으로 따라오게 한다.

즉 이 단계는
- clipping
- boolean cutting
- hard snapping

보다

- projection + relaxation
- Laplacian smoothing
- biharmonic / harmonic displacement
- local weighted deformation

에 가깝다.

### 단계 G. Final Quality Check
최종적으로 아래를 평가한다.

- penetration 없음 또는 허용치 이하
- floating gap 허용치 이하
- body 표면 변형이 국소적이며 자연스러운가
- 삼각형 flip / self-intersection 없음
- 사용자 시각적으로 전극이 잘 붙어 보이는가

---

## 7. 왜 “하나의 평면 방정식”이 위험한가

원래 아이디어의 5단계는 매우 자연스럽지만, 수학적으로는 주의가 필요하다.

### 문제
body surface가 곡면이면, 여러 접촉점은 일반적으로 하나의 평면 위에 있지 않다.

즉 실제로는 다음 중 하나가 된다.

- best-fit plane
- least-squares plane
- weighted plane
- local tangent approximation

따라서 “모든 점을 만족하는 평면”은 대부분 존재하지 않는다.

### 현실적 해석
그래서 rigid 전극 배치 문제는 다음으로 보는 것이 맞다.

- 접촉 후보점들에 대해
- rigid electrode가 가질 수 있는 최적 자세를 찾는다.

즉 **plane fitting** 은 최적화 과정의 일부일 뿐, 절대 해가 아니다.

---

## 8. Boundary Vertex만 사용하면 안 되는 이유

외곽 vertex만 사용하면 다음 문제가 발생한다.

### 경우 1. 중앙 penetration
경계는 body 바깥에 있지만 중앙이 안쪽으로 파고드는 경우

### 경우 2. 중앙 floating
경계는 닿는데 중앙은 몸에서 떨어지는 경우

### 경우 3. Saddle surface
body curvature가 단순 convex/concave가 아니면 boundary만 보고는 면 상태를 잘못 판단한다.

### 해결책
하단면에 대해 다음 샘플링을 같이 수행한다.

- boundary ring
- central grid points
- densified support points

권장 예:
- 원형 electrode: 바깥 원주 + 내부 2~3개 동심 링 + 중심점
- 사각 pad: 4개 모서리 + 4개 edge midpoint + 내부 3x3 또는 5x5 샘플

---

## 9. Step 8 “하단 경계면에 맞게 수정”의 위험성과 개선

원래 아이디어의 마지막 단계는 개념적으로 타당하지만, 구현 방식에 따라 품질 차이가 매우 크다.

### 위험한 구현
- 전극 하단 plane으로 body vertex를 직접 projection
- 겹친 삼각형을 단순히 잘라냄
- 전극 하단면에 맞춰 body를 hard snap

이 방식은 다음 문제를 낳을 수 있다.

- 단차 발생
- 표면이 눌린 것이 아니라 잘린 것처럼 보임
- triangle quality 악화
- 급격한 normal 변화
- mesh artifacts 증가

### 권장 구현
전극 하단면 또는 접촉 patch를 hard target으로 두고,
주변 body 영역은 soft deformation으로 따라오게 한다.

필요 파라미터:
- contact threshold
- expansion rings
- hard weight
- soft weight
- smoothing iterations
- max displacement
- minimum clearance

즉 현재 사용 중인 UI 파라미터 체계와도 잘 맞는다.

---

## 10. 권장 파이프라인

```text
1. User places electrode
2. Create initial safe pose (non-penetrating)
3. Sample bottom surface of electrode
4. Search body correspondence for each sample
5. Estimate best-fit rigid pose
6. Evaluate penetration / gap
7. Build contact patch on body
8. Apply local surface deformation
9. Smooth and validate mesh
10. Save quality report
```

---

## 11. 데이터 구조 설계

### 11.1 ElectrodeSamplePoint
```csharp
public sealed class ElectrodeSamplePoint
{
    public int SampleId { get; set; }
    public double[] LocalPosition { get; set; } = new double[3];
    public double[] WorldPosition { get; set; } = new double[3];
    public bool IsBoundary { get; set; }
}
```

### 11.2 SurfaceHitResult
```csharp
public sealed class SurfaceHitResult
{
    public bool Success { get; set; }
    public int SampleId { get; set; }
    public double[] HitPoint { get; set; } = new double[3];
    public double[] HitNormal { get; set; } = new double[3];
    public double Distance { get; set; }
    public long CellId { get; set; }
}
```

### 11.3 AttachmentQualityReport
```csharp
public sealed class AttachmentQualityReport
{
    public double MaxPenetrationMm { get; set; }
    public double MeanPenetrationMm { get; set; }
    public double MaxGapMm { get; set; }
    public double MeanGapMm { get; set; }
    public double ContactCoverageRatio { get; set; }
    public int PenetrationCount { get; set; }
    public int FloatingCount { get; set; }
    public bool Passed { get; set; }
    public string Summary { get; set; } = "";
}
```

### 11.4 AttachmentParameters
```csharp
public sealed class AttachmentParameters
{
    public double ContactThresholdMm { get; set; } = 2.0;
    public int ExpansionRings { get; set; } = 2;
    public int SmoothIterations { get; set; } = 2;
    public double HardWeight { get; set; } = 8.0;
    public double SoftWeight { get; set; } = 0.35;
    public double MaxDisplacementMm { get; set; } = 5.0;
    public double MinimumClearanceMm { get; set; } = 0.2;
}
```

---

## 12. 클래스 구조 제안

### 12.1 ElectrodeBottomSampler
역할:
- 전극 하단면의 boundary / inner samples 생성

### 12.2 SurfaceCorrespondenceService
역할:
- 각 샘플 점에 대해 body surface 대응점 탐색

### 12.3 ElectrodePoseOptimizer
역할:
- hit 결과들로부터 best-fit rigid pose 계산

### 12.4 AttachmentQualityEvaluator
역할:
- penetration, gap, coverage를 평가하여 quality report 생성

### 12.5 BodyAttachmentDeformer
역할:
- body contact patch를 국소적으로 변형

### 12.6 SequentialAttachmentController
역할:
- 전극 여러 개를 순차적으로 부착
- 각 단계 결과를 누적 적용

---

## 13. 알고리즘 의사코드

```text
function AttachElectrode(electrode, bodyMesh, parameters):

    pose0 = BuildInitialSafePose(electrode, bodyMesh, parameters.MinimumClearanceMm)

    samples = SampleElectrodeBottom(electrode, pose0)

    hits = []
    for each sample in samples:
        hit = FindSurfaceCorrespondence(sample, bodyMesh)
        if hit.Success:
            hits.append(hit)

    pose1 = EstimateBestFitRigidPose(electrode, samples, hits)

    report1 = EvaluateAttachmentQuality(electrode, pose1, bodyMesh, parameters)

    if report1.Passed:
        return pose1, bodyMesh, report1

    patch = BuildBodyContactPatch(bodyMesh, hits, parameters.ContactThresholdMm, parameters.ExpansionRings)

    bodyMesh2 = DeformLocalBody(
        bodyMesh,
        patch,
        electrode,
        pose1,
        parameters.HardWeight,
        parameters.SoftWeight,
        parameters.SmoothIterations,
        parameters.MaxDisplacementMm
    )

    report2 = EvaluateAttachmentQuality(electrode, pose1, bodyMesh2, parameters)

    return pose1, bodyMesh2, report2
```

---

## 14. Sequential Attachment와의 연결

전극이 여러 개일 경우 순차 부착이 필요하다.

### 이유
- 첫 번째 전극이 body를 변형시킨다.
- 두 번째 전극은 이미 변형된 body를 기준으로 붙어야 한다.
- 순서를 무시하면 전극끼리 또는 body와의 충돌이 어색해질 수 있다.

### 권장 규칙
- attachment는 electrode list 순서대로 수행
- 각 단계에서 body deformation 결과를 다음 단계의 입력으로 사용
- 최종적으로 전체 quality report를 종합

### UI와의 연결
현재 사용 중인 설정값은 본 알고리즘에 적절하다.

- Contact Threshold
- Expansion Rings
- Smooth Iteration
- Hard Weight
- Soft Weight

이 파라미터들은 FEM 물성치가 아니라
**surface-constrained deformation control parameter** 로 이해하는 것이 맞다.

---

## 15. 품질 지표 설계

다음 항목을 보고서로 출력하는 것을 권장한다.

### 필수
- maximum penetration depth
- mean penetration depth
- maximum floating gap
- mean floating gap
- contact coverage ratio
- number of invalid points

### 선택
- deformed body area
- average normal deviation
- local smoothness score
- triangle quality change
- self-intersection count

### 시각화
- contact patch highlight
- penetration region highlight
- deformed body toggle
- quality summary text

---

## 16. 테스트 전략

### 16.1 정상 케이스
- 완만한 convex body surface
- 작은 평면 electrode
- 중앙 클릭 후 정상 부착

### 16.2 어려운 케이스
- 곡률이 큰 concave 영역
- saddle-like surface
- edge 근처 부착
- 다중 전극 연속 배치

### 16.3 실패 케이스
- 대응점 raycast 실패
- 너무 적은 hit point
- extreme curvature로 rigid fit 불가능
- body mesh가 non-manifold 또는 self-intersected

### 16.4 확인 항목
- 전극이 body를 관통하지 않는가
- 과도하게 뜨지 않는가
- body 변형이 자연스러운가
- 삼각형 품질이 유지되는가
- 성능이 interactive 수준인가

---

## 17. 이 알고리즘이 적합한 범위

### 잘 맞는 범위
- heuristic attachment
- 시각적으로 자연스러운 접촉 표현
- 빠른 전극 배치 및 수정
- 물성치 없이 동작하는 엔진
- 연구용/제품용 인터랙션 기능

### 맞지 않는 범위
- biomechanical truth
- tissue elasticity calibration
- stress/strain 해석
- force-driven contact mechanics
- dynamic simulation

즉 이 알고리즘은 “실제 탄성체 시뮬레이션”이 아니라
**attachment-oriented geometric deformation engine** 으로 보는 것이 가장 정확하다.

---

## 18. 최종 결론

사용자의 원래 아이디어는 매우 좋은 출발점이다.

특히 다음 두 가지가 핵심적으로 좋다.

1. 전극은 적어도 body 바깥에 있어야 한다는 안전 규칙
2. 부착 후 남는 겹침은 body 쪽을 국소적으로 수정한다는 사고방식

다만 아래 두 부분은 반드시 수정해서 구현하는 것이 좋다.

- “모든 점을 만족하는 평면 방정식” → **best-fit rigid pose estimation** 으로 재해석
- “전극 하단 경계면에 맞게 body 수정” → **local smooth deformation** 으로 구현

따라서 최종 설계는 다음처럼 요약할 수 있다.

> 전극 하단면을 샘플링하고, body surface 대응점을 찾고,  
> 그 대응점들에 대해 전극의 최적 rigid pose를 계산한 뒤,  
> 남는 overlap과 floating은 국소적인 body surface deformation으로 보정한다.

이 구조는 현재의 WPF/C# + VTK 환경에서 구현 가능성이 높고,
직관적인 UI 파라미터와도 잘 맞으며,
향후 FEM 기반 고도화 이전 단계의 엔진으로 충분히 경쟁력 있다.
