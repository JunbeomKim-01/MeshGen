# Mesh Generation Pipeline Implementation Plan (WPF)

본 문서는 Medical 프로젝트의 시각화 및 데이터 처리 로직을 참조하여, CT/RTSS 데이터를 기반으로 최종 Volume Mesh를 생성하는 WPF 어플리케이션 개발을 위한 실행 계획서입니다.

Medical 프로젝트 참조 위치  = D:\code\fieldlab-medical_1\src

## 1. 기술 스택 및 개발 전제
- **Framework**: .NET 8.0 / WPF
- **VTK Engine**: ActiViz.NET 9.1 (Kitware.VTK)
- **UI Integration**: `WindowsFormsHost` + `vtkRenderWindowControl` (WPF 표준 방식)
- **Geometry Library**: TetGen (C++ Wrapper) 또는 vtkTetrahedralMesh

## 2. 전체 파이프라인 단계 (Workflow)

### [Step 1] CT + RTSS 데이터 임포트
- **목표**: DICOM CT 이미지와 RTStruct(구조물 정보) 파일을 로드하여 내부 데이터 모델 구축
- **참조 (Medical)**: `DicomLoader`, `RtssParser` 클래스
- **구현**:
    - Fellow Oak DICOM (fo-dicom)을 통한 메타데이터 추출
    - VTK `vtkDICOMImageReader`를 사용한 3D 볼륨 데이터 로드
    - RTSS의 ROI(Region of Interest) 좌표계를 VTK Patient Coordinate System으로 변환

### [Step 2] 레이블맵 생성 (ROI + External) 및 해상도 설정
- **목표**: 각 조직(ROI)과 외부 윤곽(External)을 복셀(Voxel) 단위의 LabelMap으로 변환
- **구현**:
    - `vtkImageStencil`을 사용하여 폐쇄된 폴리곤(RTSS) 내부를 마스킹
    - 사용자 정의 해상도(Spacing)에 따른 리샘플링 (`vtkImageReslice`)
    - 조직별 고유 ID를 부여한 `vtkImageData` 생성

### [Step 3] Surface Mesh 생성
- **목표**: LabelMap으로부터 각 조직의 경계면(Triangular Mesh) 추출
- **참조 (Medical)**: `VtkSurfaceReconstructionService`
- **구현**:
    - `vtkDiscreteMarchingCubes`를 사용하여 조직별 표면 추출
    - `vtkWindowedSincPolyDataFilter`를 통한 표면 평활화(Smoothing)
    - `vtkDecimatePro`를 사용한 폴리곤 최적화 (실시간 시각화 성능 확보)

### [Step 4] 전극(Electrode) 스펙 설정
- **목표**: 전극의 물리적 파라미터(형태, 크기, 개수) 정의
- **구현**:
    - 전극 모델링을 위한 파라미터 클래스 설계 (Radius, Height, Type 등)
    - 전극 부착 위치 선정을 위한 사용자 인터렉션 레이아웃 설계

### [Step 5] 전극 매쉬 생성
- **목표**: 설정된 스펙에 따른 3D 전극 모델 생성
- **구현**:
    - `vtkCylinderSource` 또는 `vtkDiskSource`를 활용한 기본 전극 형상 생성
    - 복합 전극의 경우 여러 소스를 합치는 `vtkAppendPolyData` 사용

### [Step 6] 전극 매쉬 부착 (Snapping & Merging)
- **목표**: 생성된 전극을 피부(External) 표면에 정밀하게 밀착시키고 결합
- **구현**:
    - `vtkCellLocator`를 사용하여 피부 표면의 가장 가까운 지점 검색
    - 전극 하단면을 피부 곡률에 맞게 투영(Projection)
    - 피부와 전극 간의 Boolean 합집합 연산 (`vtkBooleanOperationPolyDataFilter`)

### [Step 7] Volume Mesh 생성 (Tetrahedralization)
- **목표**: 모든 표면 메쉬 내부를 사면체(Tetrahedron)로 채운 최종 시뮬레이션 모델 생성
- **참조 (MeshGen)**: `MeshGen.Adapters.TetGen`
- **구현**:
    - 다중 레이어(Multi-layered) PLC(Piecewise Linear Complex) 구성
    - TetGen 라이브러리를 호출하여 내부 볼륨 메싱 실행
    - 결과물을 `vtkUnstructuredGrid` 형식으로 변환하여 시각화 및 저장

## 3. 시각화 및 UI 가이드라인 (Medical 참조)
- **Render Window**: `vtkRenderWindowControl`을 WPF `Grid` 내부에 배치
- **Interaction**: `VtkCameraInteractor`와 `VtkActorInteractor`를 통한 전극 위치 미세 조정 지원
- **Shading**: 각 조직별로 고유 색상 및 투명도(Opacity)를 부여하여 내부 구조 가시성 확보

## 4. 에이전트 실행 주의사항
- **에러 체크**: 각 단계 완료 후 메쉬의 유효성(Manifoldness, Hole 존재 여부)을 반드시 체크할 것.
- **성능 최적화**: Step 6와 7은 연산량이 많으므로 백그라운드 스레드에서 실행하고 UI는 Progress를 표시할 것.
