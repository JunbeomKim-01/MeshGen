# MeshGen MCP 설계 문서
## WPF / VTK / C# 기반 CT Mesh 생성 및 전극 부착 프로그램의 MCP 설계

---

## 1. 문서 목적

본 문서는 **MeshGen**을 MCP(Model Context Protocol) 기반 도구로 노출하기 위한 설계 문서이다.

이번 설계의 범위는 전체 Oncofield 플랫폼이 아니라, 다음 역할을 담당하는 **MeshGen 단독 애플리케이션**이다.

```text
MeshGen
- DICOM import
- NIfTI segmentation import
- STL / POLY import
- ROI / label map 처리
- CT 기반 surface mesh 생성
- mesh preview
- 전극 배치
- 전극 부착
- volume mesh 또는 PLC 생성
- STL / POLY export
```

즉, MeshGen MCP의 목적은 다음과 같다.

1. MeshGen 내부 기능을 MCP Tool / Resource로 외부에 노출한다.
2. LLM 또는 Hub 프로그램이 MeshGen의 import, mesh 생성, preview, electrode placement, electrode attachment, export 기능을 호출할 수 있게 한다.
3. VTK 창에서 직접 상호작용이 필요한 기능과 자동 실행 가능한 기능을 명확히 분리한다.
4. 기존 WPF / VTK / C# 코드 구조를 유지하면서 MCP는 얇은 wrapper 계층으로 추가한다.

---

## 2. MeshGen의 책임 범위

## 2.1 MeshGen이 담당하는 것

MeshGen은 환자 영상 데이터와 segmentation 데이터를 이용해 FEM 해석에 사용할 수 있는 mesh 자산을 만드는 프로그램이다.

MeshGen의 핵심 책임은 다음과 같다.

| 구분 | MeshGen 책임 |
|---|---|
| 입력 | DICOM, NIfTI, STL, POLY |
| 데이터 처리 | ROI 목록 관리, external ROI 지정, label map 생성 |
| Surface 생성 | External, organ, target 등 ROI 기반 surface mesh 생성 |
| Mesh 확인 | VTK viewport에서 mesh preview, opacity/style 조정, 카메라 focus |
| 전극 생성 | 좌표 기반 전극 생성 또는 VTK 상호작용 기반 전극 배치 |
| 전극 부착 | elastic deformation 또는 surface attachment 방식으로 전극을 body surface에 부착 |
| Volume mesh | surface / PLC 기반 volume mesh 또는 TetGen 입력 생성 |
| 출력 | STL, POLY, mesh metadata, quality report |

---

## 2.2 MeshGen이 담당하지 않는 것

MeshGen MCP 설계에서 다음 기능은 MeshGen의 핵심 책임에서 제외한다.

| 항목 | 담당 프로그램 |
|---|---|
| 수동 contouring / segmentation 편집 | EdgeStudio |
| FEM 전기장 / 열 해석 solver 실행 | Oncofield |
| dose / field 결과 분석 | Oncofield |
| 전체 워크플로우 orchestration | Hub 또는 외부 MCP client |
| 라이선스 / 결제 / 사용자 계정 | 별도 platform layer |

MeshGen은 **해석 전 단계의 mesh preparation tool**로 정의한다.

---

## 3. MeshGen MCP의 핵심 결론

## 3.1 MCP는 MeshGen의 기능 wrapper이다

MCP Tool은 mesh 생성 알고리즘을 직접 구현하지 않는다.

```text
MCP Tool
  -> MeshGen Command Bridge
    -> MeshGen Core Service
      -> 기존 mesh / electrode / export 로직
```

즉 MCP는 다음을 담당한다.

- 외부 명령 수신
- 입력 DTO 검증
- MeshGen 내부 service 호출
- 실행 결과를 JSON으로 반환
- 현재 pipeline / viewport / attachment 상태를 Resource로 노출

---

## 3.2 Interactive 기능과 Non-interactive 기능을 분리한다

MeshGen에는 두 종류의 기능이 있다.

### Non-interactive 기능

입력값만으로 바로 실행 가능한 기능이다.

예:
- DICOM import
- NIfTI import
- STL / POLY import
- label map 생성
- surface mesh 생성
- 좌표 기반 전극 생성
- batch electrode attachment
- volume mesh 생성
- STL / POLY export

### Interactive 기능

VTK viewport에서 사용자의 마우스 조작이 필요한 기능이다.

예:
- mesh preview 확인
- 전극 배치 모드 진입
- surface picking
- hover preview
- confirm / cancel
- interactive electrode attachment session

따라서 MCP Tool도 다음처럼 분리해야 한다.

```text
Non-interactive Tool
- 호출 즉시 실행
- 결과 JSON 반환

Interactive Tool
- 세션 시작
- VTK viewport 활성화
- 사용자가 직접 조작
- MCP는 상태 조회 / 확정 / 취소만 수행
```

---

## 4. MeshGen MCP 전체 구조

```text
┌───────────────────────────────────────────────────────────────┐
│                      Hub / LLM / MCP Client                   │
│  - Chat command                                               │
│  - Workflow command                                           │
│  - Tool call                                                  │
└───────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                        MeshGen MCP Server                     │
│  - Tool Registry                                              │
│  - Resource Registry                                          │
│  - Request / Response DTO                                     │
│  - Validation                                                 │
│  - Command Bridge                                             │
└───────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                         MeshGen WPF App                       │
│  - MainWindow                                                 │
│  - VTK Viewport Host                                          │
│  - Pipeline Panel                                             │
│  - Mesh Preview Panel                                         │
│  - Electrode Panel                                            │
│  - Status Panel                                               │
└───────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                      VTK Interaction Layer                    │
│  - vtkRenderWindow                                            │
│  - vtkRenderer                                                │
│  - vtkInteractor                                              │
│  - vtkCellPicker                                              │
│  - Mesh Actor                                                 │
│  - Preview Actor                                              │
│  - Electrode Actor                                            │
│  - Attachment Preview Actor                                   │
└───────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                         MeshGen Core                          │
│  - ImportService                                              │
│  - RoiService                                                 │
│  - LabelMapService                                            │
│  - SurfaceMeshService                                         │
│  - MeshPreviewService                                         │
│  - ElectrodeRepository                                        │
│  - ElectrodePlacementController                               │
│  - AttachmentInteractionController                            │
│  - ElasticDeformationService                                  │
│  - VolumeMeshService                                          │
│  - MeshExportService                                          │
│  - SceneStateService                                          │
└───────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                         MeshGen State                         │
│  - Current Study                                              │
│  - ROIs                                                       │
│  - Label Maps                                                 │
│  - Surface Meshes                                             │
│  - Imported Meshes                                            │
│  - Electrode List                                             │
│  - Attachment Session                                         │
│  - Volume Mesh                                                │
│  - Export History                                             │
└───────────────────────────────────────────────────────────────┘
```

---

## 5. MeshGen 내부 프로젝트 구조 권장안

```text
MeshGen.App
  ├─ UI
  │   ├─ MainWindow.xaml
  │   ├─ PipelinePanel.xaml
  │   ├─ MeshPreviewPanel.xaml
  │   ├─ ElectrodePanel.xaml
  │   ├─ AttachmentPanel.xaml
  │   └─ StatusPanel.xaml
  │
  ├─ Vtk
  │   ├─ VtkViewportHost.cs
  │   ├─ VtkRenderService.cs
  │   ├─ VtkInteractorAdapter.cs
  │   ├─ MeshActorManager.cs
  │   ├─ MeshPreviewActorManager.cs
  │   ├─ ElectrodeActorManager.cs
  │   ├─ AttachmentPreviewActorManager.cs
  │   └─ VtkPickingService.cs
  │
  ├─ Core
  │   ├─ Import
  │   │   ├─ DicomImportService.cs
  │   │   ├─ NiftiImportService.cs
  │   │   ├─ StlImportService.cs
  │   │   └─ PolyImportService.cs
  │   │
  │   ├─ Study
  │   │   ├─ Study.cs
  │   │   ├─ Roi.cs
  │   │   ├─ RoiService.cs
  │   │   └─ SceneStateService.cs
  │   │
  │   ├─ Mesh
  │   │   ├─ LabelMapService.cs
  │   │   ├─ SurfaceMeshService.cs
  │   │   ├─ MeshPreviewService.cs
  │   │   ├─ MeshQueryService.cs
  │   │   ├─ VolumeMeshService.cs
  │   │   └─ MeshExportService.cs
  │   │
  │   ├─ Electrode
  │   │   ├─ ElectrodeSpec.cs
  │   │   ├─ ElectrodeLayer.cs
  │   │   ├─ ElectrodeRepository.cs
  │   │   ├─ ElectrodePlacementController.cs
  │   │   └─ ElectrodeFactory.cs
  │   │
  │   └─ Attachment
  │       ├─ AttachmentParameters.cs
  │       ├─ AttachmentInteractionController.cs
  │       ├─ AttachmentSessionStateService.cs
  │       ├─ AttachmentPreviewService.cs
  │       ├─ ElasticDeformationService.cs
  │       └─ AttachmentReportService.cs
  │
  └─ Integration
      ├─ Mcp
      │   ├─ MeshGenMcpHost.cs
      │   ├─ McpToolRegistry.cs
      │   ├─ McpResourceRegistry.cs
      │   ├─ McpCommandBridge.cs
      │   └─ McpStateAdapter.cs
      │
      └─ Ipc
          ├─ NamedPipeServer.cs
          ├─ GrpcMeshGenService.cs
          └─ AppCommandDispatcher.cs
```

---

## 6. MeshGen MCP 서버 프로젝트 구조

MeshGen MCP 서버를 별도 프로젝트로 분리할 경우 다음 구조를 권장한다.

```text
MeshGen.McpServer
  ├─ Program.cs
  │
  ├─ Tools
  │   ├─ ImportTools.cs
  │   ├─ RoiTools.cs
  │   ├─ LabelMapTools.cs
  │   ├─ SurfaceMeshTools.cs
  │   ├─ MeshPreviewTools.cs
  │   ├─ ElectrodeTools.cs
  │   ├─ AttachmentTools.cs
  │   ├─ VolumeMeshTools.cs
  │   └─ ExportTools.cs
  │
  ├─ Resources
  │   ├─ PipelineStatusResource.cs
  │   ├─ RoiListResource.cs
  │   ├─ ImportedMeshesResource.cs
  │   ├─ SurfaceMeshesResource.cs
  │   ├─ ElectrodeListResource.cs
  │   ├─ MeshPreviewStateResource.cs
  │   └─ AttachmentSessionResource.cs
  │
  ├─ Dtos
  │   ├─ CommonToolResponse.cs
  │   ├─ ImportRequests.cs
  │   ├─ MeshRequests.cs
  │   ├─ ElectrodeRequests.cs
  │   ├─ AttachmentRequests.cs
  │   └─ ExportRequests.cs
  │
  └─ Bridge
      ├─ IAppCommandClient.cs
      ├─ IAppStateClient.cs
      ├─ NamedPipeAppCommandClient.cs
      └─ GrpcAppCommandClient.cs
```

---

## 7. 앱 내장 MCP vs 별도 MCP 서버

## 7.1 방식 A: MeshGen WPF 앱 내부에 MCP 서버 포함

```text
MeshGen.exe
  ├─ WPF UI
  ├─ VTK Viewport
  ├─ Core Services
  └─ MCP Host
```

### 장점

- 구현이 빠르다.
- 내부 객체 접근이 쉽다.
- 초기 프로토타입에 적합하다.
- 별도 IPC 설계가 필요 없다.

### 단점

- MCP 요청과 UI thread 충돌을 조심해야 한다.
- MCP 서버 오류가 MeshGen 앱에 영향을 줄 수 있다.
- 장기적으로 결합도가 높아진다.

### 적합한 단계

- MVP
- 내부 개발용
- 단일 PC 데모
- Hub와 빠르게 연결하는 초기 단계

---

## 7.2 방식 B: MeshGen MCP 서버를 별도 프로세스로 분리

```text
MeshGen.McpServer.exe
       │
       │ Named Pipe / gRPC / Local HTTP
       ▼
MeshGen.App.exe
```

### 장점

- 안정성이 높다.
- MCP와 UI를 분리할 수 있다.
- Hub / 외부 client 연결이 쉽다.
- 장애 격리가 가능하다.
- 장기 제품 구조에 적합하다.

### 단점

- IPC 설계가 필요하다.
- 상태 동기화가 필요하다.
- 초기 구현 비용이 증가한다.

### 적합한 단계

- 상용 제품
- 여러 프로그램을 Hub에서 제어하는 구조
- EdgeStudio / Oncofield와 함께 운영되는 통합 환경

---

## 7.3 권장 방향

초기에는 **WPF 앱 내장 MCP**로 빠르게 구현하고, 구조는 처음부터 **Command Bridge 인터페이스 기반**으로 만든다.

이렇게 하면 나중에 별도 프로세스로 분리할 때 Core Tool 코드를 크게 바꾸지 않아도 된다.

```text
초기 MVP
MCP Tool -> InProcessCommandBridge -> MeshGen Core

장기 구조
MCP Tool -> IpcCommandBridge -> MeshGen App -> MeshGen Core
```

---

## 8. Command Bridge 설계

MCP Tool은 MeshGen Core Service를 직접 호출하지 않고 Command Bridge를 통해 호출한다.

```text
MCP Tool
  │
  ▼
McpCommandBridge
  │
  ▼
AppCommandDispatcher
  │
  ├─ ImportService
  ├─ RoiService
  ├─ LabelMapService
  ├─ SurfaceMeshService
  ├─ MeshPreviewService
  ├─ ElectrodePlacementController
  ├─ AttachmentInteractionController
  ├─ ElasticDeformationService
  ├─ VolumeMeshService
  └─ MeshExportService
```

### 8.1 Command Bridge의 책임

- MCP 요청 DTO를 내부 command로 변환
- UI thread 실행이 필요한 명령 분리
- long-running task는 background task로 실행
- 실행 결과를 공통 response로 변환
- 예외를 MCP response의 `success=false` 형태로 반환

---

## 9. 공통 응답 형식

모든 MeshGen MCP Tool은 가능한 한 동일한 응답 구조를 사용한다.

```json
{
  "success": true,
  "message": "Human-readable summary",
  "warnings": [],
  "data": {}
}
```

### 필드 설명

| 필드 | 의미 |
|---|---|
| `success` | 실행 성공 여부 |
| `message` | 사람이 읽을 수 있는 요약 |
| `warnings` | 치명적이지 않은 경고 목록 |
| `data` | Tool별 결과 데이터 |

---

## 10. MeshGen MCP Tool 목록

---

# 10.1 Import Tools

## `import_dicom_study`

DICOM study를 MeshGen으로 불러온다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `DicomImportService.ImportAsync` 또는 `PipelineService.ImportAsync` |
| 입력 | `dicomDirectory` |
| 출력 | `studyId`, `seriesCount`, `imageSpacing`, `imageDimensions` |
| 사전조건 | 유효한 DICOM 폴더 |
| 부작용 | 현재 study 상태 갱신 |

### 입력 예시

```json
{
  "dicomDirectory": "D:/Patient001/DICOM"
}
```

### 출력 예시

```json
{
  "success": true,
  "message": "DICOM study imported.",
  "warnings": [],
  "data": {
    "studyId": "study_001",
    "seriesCount": 1,
    "imageDimensions": [512, 512, 180],
    "imageSpacing": [0.976, 0.976, 1.5]
  }
}
```

---

## `import_nifti_segmentation`

NIfTI segmentation 파일을 불러온다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `NiftiImportService.ImportAsync` |
| 입력 | `paths[]` |
| 출력 | `loadedFiles`, `segmentationCount`, `roiCandidates` |
| 사전조건 | 유효한 `.nii` 또는 `.nii.gz` 파일 |
| 부작용 | ROI / segmentation 상태 갱신 |

### 입력 예시

```json
{
  "paths": [
    "D:/Patient001/seg_external.nii.gz",
    "D:/Patient001/seg_liver.nii.gz"
  ]
}
```

---

## `import_stl_mesh`

STL surface mesh를 MeshGen scene에 등록한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `StlImportService.ImportAsync` |
| 입력 | `path`, `meshRole?` |
| 출력 | `meshId`, `triangleCount`, `bounds` |
| 사전조건 | 유효한 STL 파일 |
| 부작용 | imported mesh 목록 갱신 |

### 입력 예시

```json
{
  "path": "D:/Patient001/external.stl",
  "meshRole": "external"
}
```

---

## `import_poly_mesh`

POLY 파일 또는 PLC 입력 파일을 MeshGen에 불러온다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `PolyImportService.ImportAsync` |
| 입력 | `path`, `meshRole?` |
| 출력 | `meshId`, `pointCount`, `facetCount`, `regionCount?` |
| 사전조건 | 유효한 POLY 파일 |
| 부작용 | imported mesh 목록 갱신 |

---

# 10.2 ROI / Study Tools

## `list_rois`

현재 study 또는 segmentation에서 사용 가능한 ROI 목록을 반환한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `RoiService.GetRois()` |
| 입력 | `studyId?` |
| 출력 | `rois[]` |
| 사전조건 | study 또는 segmentation 로드됨 |
| 부작용 | 없음 |

### 출력 예시

```json
{
  "success": true,
  "message": "ROIs found.",
  "warnings": [],
  "data": {
    "rois": [
      {
        "roiId": "roi_external",
        "name": "External",
        "labelValue": 1,
        "type": "body"
      },
      {
        "roiId": "roi_liver",
        "name": "Liver",
        "labelValue": 2,
        "type": "organ"
      }
    ]
  }
}
```

---

## `set_external_roi`

전극 부착 기준이 되는 외부 body ROI를 지정한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `RoiService.SetExternalRoi(roiId)` |
| 입력 | `roiId` |
| 출력 | `externalRoiId`, `updated` |
| 사전조건 | 해당 ROI 존재 |
| 부작용 | body surface 생성 및 attachment 기준 변경 |

---

# 10.3 Label Map / Surface Mesh Tools

## `build_label_map`

DICOM image와 segmentation / ROI 정보를 이용해 label map을 생성한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `LabelMapService.BuildAsync` |
| 입력 | `roiIds[]?`, `spacing?`, `outputLabelMapId?` |
| 출력 | `labelMapId`, `dimensions`, `labels` |
| 사전조건 | DICOM 또는 segmentation 로드됨 |
| 부작용 | label map 상태 생성 / 갱신 |

### 입력 예시

```json
{
  "roiIds": ["roi_external", "roi_liver"],
  "spacing": [1.0, 1.0, 1.0],
  "outputLabelMapId": "labelmap_main"
}
```

---

## `build_surface_mesh`

label map 또는 ROI로부터 surface mesh를 생성한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `SurfaceMeshService.BuildAsync` |
| 입력 | `sourceId`, `roiIds[]?`, `smoothingIterations?`, `decimationRatio?` |
| 출력 | `meshIds[]`, `triangleCounts` |
| 사전조건 | label map 또는 segmentation 준비됨 |
| 부작용 | surface mesh 생성 및 scene 등록 |

### 입력 예시

```json
{
  "sourceId": "labelmap_main",
  "roiIds": ["roi_external", "roi_liver"],
  "smoothingIterations": 10,
  "decimationRatio": 0.2
}
```

---

# 10.4 Mesh Preview Tools

Mesh preview 기능은 VTK viewport를 사용하므로 화면 상태를 변경한다. 다만 사용자 클릭이 필수는 아니므로 대부분은 semi-interactive tool로 볼 수 있다.

## `preview_mesh`

특정 mesh를 VTK viewport에 preview actor로 표시한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `MeshPreviewService.Show` |
| 입력 | `meshId`, `style?`, `opacity?`, `color?` |
| 출력 | `previewActorId`, `visible` |
| 사전조건 | mesh 존재, viewport 준비됨 |
| 부작용 | VTK scene에 preview actor 생성 / 갱신 |

### 입력 예시

```json
{
  "meshId": "mesh_external_01",
  "style": "surface",
  "opacity": 0.45,
  "color": [0.8, 0.8, 0.8]
}
```

---

## `hide_mesh_preview`

특정 mesh preview를 숨긴다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `MeshPreviewService.Hide` |
| 입력 | `meshId` 또는 `previewActorId` |
| 출력 | `hidden` |
| 사전조건 | preview actor 존재 |
| 부작용 | preview visibility 변경 |

---

## `clear_mesh_previews`

모든 mesh preview actor를 제거한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `MeshPreviewService.ClearAll` |
| 입력 | 없음 |
| 출력 | `clearedCount` |
| 사전조건 | viewport 준비됨 |
| 부작용 | preview actor 제거 |

---

## `set_mesh_preview_style`

preview 표시 스타일을 변경한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `MeshPreviewService.UpdateStyle` |
| 입력 | `meshId`, `style`, `opacity?`, `color?` |
| 출력 | `updated` |
| 사전조건 | preview actor 존재 |
| 부작용 | VTK actor property 변경 |

---

## `focus_mesh_in_viewport`

특정 mesh가 잘 보이도록 VTK camera를 맞춘다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `VtkRenderService.FocusMesh` |
| 입력 | `meshId` |
| 출력 | `cameraUpdated` |
| 사전조건 | mesh 존재 |
| 부작용 | camera 상태 변경 |

---

## `get_mesh_info`

mesh의 기본 기하 정보를 조회한다.

| 항목 | 내용 |
|---|---|
| Type | Tool 또는 Resource 성격 |
| Interactive | No |
| Backend Mapping | `MeshQueryService.GetInfo` |
| 입력 | `meshId` |
| 출력 | `pointCount`, `triangleCount`, `bounds`, `isClosed`, `nonManifoldEdges?` |
| 사전조건 | mesh 존재 |
| 부작용 | 없음 |

---

# 10.5 Electrode Placement Tools

## `place_electrode_at_pose`

좌표와 normal vector가 주어졌을 때 전극을 생성한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `ElectrodeRepository.CreateAndAdd` |
| 입력 | `position[3]`, `normal[3]`, `electrodeType`, `spec?` |
| 출력 | `electrodeId`, `worldPose` |
| 사전조건 | scene 또는 target mesh 준비됨 |
| 부작용 | 전극 목록 갱신, 전극 actor 생성 |

### 입력 예시

```json
{
  "position": [12.2, 38.5, 4.0],
  "normal": [0.0, 0.0, 1.0],
  "electrodeType": "pad",
  "spec": {
    "widthMm": 30.0,
    "heightMm": 20.0,
    "thicknessMm": 2.0
  }
}
```

---

## `set_electrode_layers`

전극의 layer 구조를 설정한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `ElectrodeSpec.Layers` 갱신 |
| 입력 | `electrodeId`, `layers[]` |
| 출력 | `layerCount`, `updated` |
| 사전조건 | electrode 존재 |
| 부작용 | electrode spec 변경 |

### 입력 예시

```json
{
  "electrodeId": "elec_01",
  "layers": [
    {
      "name": "conductive_gel",
      "thicknessMm": 1.0,
      "material": "gel"
    },
    {
      "name": "electrode_pad",
      "thicknessMm": 2.0,
      "material": "carbon_rubber"
    }
  ]
}
```

---

## `enter_electrode_placement_mode`

VTK viewport를 전극 배치 모드로 전환한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `ElectrodePlacementController.EnterMode` |
| 입력 | `electrodeType?`, `spec?`, `targetMeshId?` |
| 출력 | `modeEntered`, `sessionId` |
| 사전조건 | viewport 준비됨, target mesh 존재 |
| 부작용 | picker / preview actor 활성화 |

---

## `confirm_electrode_placement`

현재 preview 상태의 전극 위치를 실제 전극으로 확정한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `ElectrodePlacementController.Confirm` |
| 입력 | `sessionId?` |
| 출력 | `electrodeId`, `placed` |
| 사전조건 | placement mode 활성화, preview 존재 |
| 부작용 | 전극 actor 생성, electrode list 갱신 |

---

## `cancel_electrode_placement`

전극 배치 모드를 취소한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `ElectrodePlacementController.Cancel` |
| 입력 | `sessionId?` |
| 출력 | `cancelled` |
| 사전조건 | placement mode 활성화 |
| 부작용 | preview actor 제거, placement mode 종료 |

---

# 10.6 Interactive Attachment Tools

전극 부착은 VTK 창에서 사용자가 직접 확인하면서 수행하는 것이 적절하다. 따라서 MCP는 부착을 직접 렌더링하지 않고, 상호작용 세션을 제어한다.

## Attachment 상태 머신

```text
Idle
 └─> AttachmentSessionStarting
       └─> AttachmentInteractive
             ├─> PreviewValid
             ├─> PreviewInvalid
             ├─> Confirmed
             │     └─> Attached
             └─> Cancelled
```

---

## `start_interactive_attachment_session`

VTK 상호작용 기반 전극 부착 세션을 시작한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `AttachmentInteractionController.StartSession` |
| 입력 | `electrodeIds[]?`, `targetMeshId?`, `parameters?` |
| 출력 | `sessionId`, `started` |
| 사전조건 | viewport, body mesh, electrode 준비됨 |
| 부작용 | attachment session 생성, preview 활성화 |

### 입력 예시

```json
{
  "electrodeIds": ["elec_01", "elec_02"],
  "targetMeshId": "mesh_external_01",
  "parameters": {
    "contactThresholdMm": 2.0,
    "expansionRings": 2,
    "smoothIterations": 2,
    "hardWeight": 8.0,
    "softWeight": 0.35
  }
}
```

---

## `get_attachment_session_state`

현재 interactive attachment session 상태를 조회한다.

| 항목 | 내용 |
|---|---|
| Type | Tool 또는 Resource 성격 |
| Interactive | No |
| Backend Mapping | `AttachmentSessionStateService.GetState` |
| 입력 | `sessionId?` |
| 출력 | `sessionActive`, `previewValid`, `warnings`, `selectedElectrodeId` |
| 사전조건 | session 존재 |
| 부작용 | 없음 |

### 출력 예시

```json
{
  "success": true,
  "message": "Attachment session is active.",
  "warnings": ["Outer surface penetration detected"],
  "data": {
    "sessionActive": true,
    "previewValid": false,
    "selectedElectrodeId": "elec_01",
    "maxPenetrationMm": 1.8,
    "meanGapMm": 0.6
  }
}
```

---

## `get_attachment_quality_preview`

현재 preview 상태의 attachment 품질을 반환한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `AttachmentPreviewService.GetQualityPreview` |
| 입력 | `sessionId?` |
| 출력 | `maxPenetrationMm`, `meanGapMm`, `coverageRatio`, `contactAreaMm2?` |
| 사전조건 | session 존재, preview 계산됨 |
| 부작용 | 없음 |

---

## `confirm_attachment_session`

현재 interactive preview를 확정하고 실제 attachment를 실행한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `AttachmentInteractionController.ConfirmSession` |
| 입력 | `sessionId?`, `allowWarnings?` |
| 출력 | `attachedCount`, `qualityReport` |
| 사전조건 | session 활성화, preview valid 또는 warning 허용 |
| 부작용 | electrode attachment 실행, body/electrode mesh 갱신 |

---

## `cancel_attachment_session`

현재 attachment session을 취소한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | Yes |
| Backend Mapping | `AttachmentInteractionController.CancelSession` |
| 입력 | `sessionId?` |
| 출력 | `cancelled` |
| 사전조건 | session 활성화 |
| 부작용 | preview 제거, session 종료 |

---

## `set_attachment_parameters`

attachment 파라미터를 갱신한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `AttachmentParameterService.Update` |
| 입력 | `contactThresholdMm?`, `expansionRings?`, `smoothIterations?`, `hardWeight?`, `softWeight?` |
| 출력 | `updatedParameters` |
| 사전조건 | 없음 또는 session 존재 |
| 부작용 | 이후 preview / attachment 계산에 영향 |

---

# 10.7 Batch Attachment / Volume Mesh / Export Tools

## `attach_electrodes_batch`

여러 전극에 대해 비상호작용 방식으로 attachment를 수행한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `ElasticDeformationService.AttachAsync` |
| 입력 | `electrodeIds[]?`, `targetMeshId?`, `parameters?` |
| 출력 | `attachedCount`, `qualityReport` |
| 사전조건 | body mesh, electrode 준비됨 |
| 부작용 | electrode mesh / body surface 갱신 |

### 출력 예시

```json
{
  "success": true,
  "message": "Batch attachment completed.",
  "warnings": [],
  "data": {
    "attachedCount": 2,
    "maxPenetrationMm": 0.4,
    "meanGapMm": 0.9,
    "coverageRatio": 0.96
  }
}
```

---

## `generate_volume_mesh`

surface mesh 또는 POLY/PLC 데이터를 이용해 volume mesh를 생성한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `VolumeMeshService.GenerateAsync` |
| 입력 | `sourceMeshIds[]`, `options?` |
| 출력 | `volumeMeshId`, `nodeCount`, `cellCount`, `qualitySummary` |
| 사전조건 | surface 또는 PLC 준비됨 |
| 부작용 | volume mesh 생성 |

### 입력 예시

```json
{
  "sourceMeshIds": ["mesh_external_01", "mesh_liver_01", "mesh_elec_01"],
  "options": {
    "maxTetVolume": 2.0,
    "minDihedralAngle": 10.0,
    "preserveBoundary": true
  }
}
```

---

## `export_surface_stl`

surface mesh를 STL 파일로 저장한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `MeshExportService.WriteStl` |
| 입력 | `meshId`, `outputPath`, `overwrite?` |
| 출력 | `written`, `outputPath`, `fileSizeBytes?` |
| 사전조건 | mesh 존재 |
| 부작용 | 파일 생성 |

---

## `export_volume_poly`

volume mesh 또는 PLC mesh를 POLY 파일로 저장한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `MeshExportService.WritePoly` |
| 입력 | `meshId`, `outputPath`, `overwrite?` |
| 출력 | `written`, `outputPath`, `fileSizeBytes?` |
| 사전조건 | mesh 존재 |
| 부작용 | 파일 생성 |

---

## `export_mesh_package`

Oncofield solver로 넘기기 위한 mesh package를 한 번에 export한다.

| 항목 | 내용 |
|---|---|
| Type | Tool |
| Interactive | No |
| Backend Mapping | `MeshExportService.ExportPackageAsync` |
| 입력 | `outputDirectory`, `includeStl`, `includePoly`, `includeMetadata`, `overwrite?` |
| 출력 | `exportedFiles[]`, `manifestPath` |
| 사전조건 | 필요한 mesh 생성 완료 |
| 부작용 | STL / POLY / JSON metadata 생성 |

### 출력 예시

```json
{
  "success": true,
  "message": "Mesh package exported.",
  "warnings": [],
  "data": {
    "exportedFiles": [
      "D:/Patient001/mesh/external.stl",
      "D:/Patient001/mesh/liver.stl",
      "D:/Patient001/mesh/electrodes.stl",
      "D:/Patient001/mesh/domain.poly",
      "D:/Patient001/mesh/manifest.json"
    ],
    "manifestPath": "D:/Patient001/mesh/manifest.json"
  }
}
```

---

## 11. MeshGen MCP Resource 목록

Resource는 상태를 바꾸지 않는 읽기 전용 인터페이스다.

| Resource Name | 목적 | 내용 |
|---|---|---|
| `meshgen/pipeline/status` | 전체 pipeline 상태 | study, ROI, label map, surface, electrode, attachment, volume mesh 상태 |
| `meshgen/study/info` | 현재 study 정보 | studyId, image dimensions, spacing, loaded series |
| `meshgen/study/rois` | ROI 목록 | roiId, name, labelValue, type, visibility |
| `meshgen/study/current_external_roi` | 외부 기준 ROI | externalRoiId, name |
| `meshgen/label_maps` | label map 목록 | labelMapId, dimensions, labels |
| `meshgen/surface_meshes` | 생성된 surface mesh 목록 | meshId, role, pointCount, triangleCount, bounds |
| `meshgen/imported_meshes` | import된 mesh 목록 | meshId, format, role, sourcePath |
| `meshgen/electrodes` | 전극 목록 | electrodeId, type, layerCount, placed/attached 상태 |
| `meshgen/viewport/preview_state` | VTK preview 상태 | preview actor 목록, style, opacity, visibility |
| `meshgen/attachment/session_status` | attachment session 상태 | active, previewValid, warnings, selectedElectrodeId |
| `meshgen/attachment/last_report` | 최근 attachment 결과 | quality report, penetration, gap, coverage |
| `meshgen/export/history` | export 이력 | output paths, timestamp, mesh package info |

---

## 12. Resource 예시

## 12.1 `meshgen/pipeline/status`

```json
{
  "studyLoaded": true,
  "studyId": "study_001",
  "dicomLoaded": true,
  "niftiLoaded": true,
  "roiCount": 18,
  "externalRoiId": "roi_external",
  "labelMapBuilt": true,
  "surfaceMeshCount": 5,
  "electrodeCount": 2,
  "attachmentCompleted": true,
  "volumeMeshGenerated": false,
  "lastExportPath": null
}
```

---

## 12.2 `meshgen/electrodes`

```json
{
  "electrodes": [
    {
      "electrodeId": "elec_01",
      "type": "pad",
      "placed": true,
      "attached": true,
      "layerCount": 2,
      "position": [12.2, 38.5, 4.0],
      "normal": [0.0, 0.0, 1.0]
    }
  ]
}
```

---

## 12.3 `meshgen/attachment/session_status`

```json
{
  "sessionActive": true,
  "sessionId": "attach_session_001",
  "selectedElectrodeId": "elec_01",
  "targetMeshId": "mesh_external_01",
  "previewValid": false,
  "warnings": [
    "Outer surface penetration detected"
  ],
  "maxPenetrationMm": 1.8,
  "meanGapMm": 0.6,
  "coverageRatio": 0.83
}
```

---

## 13. MeshGen MCP Tool 요약표

| Category | Tool Name | Interactive | 목적 |
|---|---|---:|---|
| Import | `import_dicom_study` | No | DICOM study 로드 |
| Import | `import_nifti_segmentation` | No | NIfTI segmentation 로드 |
| Import | `import_stl_mesh` | No | STL mesh 로드 |
| Import | `import_poly_mesh` | No | POLY mesh 로드 |
| ROI | `list_rois` | No | ROI 목록 조회 |
| ROI | `set_external_roi` | No | body 기준 ROI 지정 |
| Label | `build_label_map` | No | label map 생성 |
| Surface | `build_surface_mesh` | No | ROI 기반 surface mesh 생성 |
| Preview | `preview_mesh` | Yes | VTK viewport에 mesh 표시 |
| Preview | `hide_mesh_preview` | Yes | preview 숨김 |
| Preview | `clear_mesh_previews` | Yes | 모든 preview 제거 |
| Preview | `set_mesh_preview_style` | Yes | preview style 변경 |
| Preview | `focus_mesh_in_viewport` | Yes | 카메라 focus |
| Mesh Query | `get_mesh_info` | No | mesh 정보 조회 |
| Electrode | `place_electrode_at_pose` | No | 좌표 기반 전극 생성 |
| Electrode | `set_electrode_layers` | No | 전극 layer 설정 |
| Electrode | `enter_electrode_placement_mode` | Yes | VTK 전극 배치 모드 시작 |
| Electrode | `confirm_electrode_placement` | Yes | preview 전극 확정 |
| Electrode | `cancel_electrode_placement` | Yes | 전극 배치 취소 |
| Attachment | `start_interactive_attachment_session` | Yes | VTK 기반 부착 세션 시작 |
| Attachment | `get_attachment_session_state` | No | 부착 세션 상태 조회 |
| Attachment | `get_attachment_quality_preview` | No | 부착 preview 품질 조회 |
| Attachment | `confirm_attachment_session` | Yes | 부착 확정 |
| Attachment | `cancel_attachment_session` | Yes | 부착 취소 |
| Attachment | `set_attachment_parameters` | No | 부착 파라미터 변경 |
| Batch | `attach_electrodes_batch` | No | 비상호작용 부착 실행 |
| Volume Mesh | `generate_volume_mesh` | No | volume mesh 생성 |
| Export | `export_surface_stl` | No | STL export |
| Export | `export_volume_poly` | No | POLY export |
| Export | `export_mesh_package` | No | Oncofield 전달용 package export |

---

## 14. MeshGen MCP 대표 워크플로우

## 14.1 DICOM + NIfTI에서 surface mesh 생성

```text
1. import_dicom_study
2. import_nifti_segmentation
3. list_rois
4. set_external_roi
5. build_label_map
6. build_surface_mesh
7. preview_mesh
8. get_mesh_info
```

---

## 14.2 전극을 VTK에서 직접 배치하고 부착

```text
1. preview_mesh(mesh_external_01)
2. enter_electrode_placement_mode
3. User picks point on VTK viewport
4. confirm_electrode_placement
5. set_electrode_layers
6. start_interactive_attachment_session
7. User adjusts / checks preview in VTK viewport
8. get_attachment_quality_preview
9. confirm_attachment_session
10. export_mesh_package
```

---

## 14.3 좌표 기반 자동 전극 생성 후 batch attachment

```text
1. import_stl_mesh 또는 build_surface_mesh
2. place_electrode_at_pose
3. set_electrode_layers
4. attach_electrodes_batch
5. generate_volume_mesh
6. export_volume_poly
```

---

## 14.4 Oncofield solver로 전달할 mesh package 생성

```text
1. build_surface_mesh
2. attach_electrodes_batch 또는 confirm_attachment_session
3. generate_volume_mesh
4. export_surface_stl
5. export_volume_poly
6. export_mesh_package
```

결과물 예시:

```text
Patient001_MeshPackage/
  ├─ external.stl
  ├─ liver.stl
  ├─ electrodes.stl
  ├─ domain.poly
  ├─ regions.json
  ├─ materials.json
  ├─ electrodes.json
  └─ manifest.json
```

---

## 15. VTK Interactive 처리 원칙

## 15.1 MCP가 직접 마우스 이벤트를 대신하지 않는다

MCP는 VTK viewport 안에서 마우스를 움직이거나 클릭하는 역할을 직접 수행하지 않는다.

대신 다음만 한다.

```text
- interaction mode 시작
- 현재 preview 상태 조회
- 현재 preview 품질 조회
- confirm 요청
- cancel 요청
- parameter 변경
```

실제 hover, pick, drag, preview 업데이트는 MeshGen WPF / VTK 내부 controller가 담당한다.

---

## 15.2 UI Thread 분리

VTK actor 생성, camera 변경, render call은 UI thread에서 실행해야 한다.

따라서 Command Dispatcher는 명령을 다음 두 종류로 분리한다.

| 명령 유형 | 실행 위치 |
|---|---|
| UI / VTK 명령 | WPF Dispatcher thread |
| heavy computation | background task |
| file IO | background task |
| 결과 scene 반영 | WPF Dispatcher thread |

예:

```text
build_surface_mesh
  1. background task에서 mesh 계산
  2. 계산 완료 후 UI thread에서 actor 등록

preview_mesh
  1. UI thread에서 actor 생성 / visibility 변경

attach_electrodes_batch
  1. background task에서 deformation 계산
  2. UI thread에서 final actor 갱신
```

---

## 15.3 Interactive session timeout

전극 배치나 부착 세션은 사용자가 멈출 수 있으므로 timeout 정책이 필요하다.

권장 정책:

```text
- session 시작 후 기본 timeout: 30분
- 사용자 입력이 있으면 timeout 연장
- MCP client가 cancel 호출 가능
- 앱 종료 시 active session 자동 cancel
```

---

## 16. DTO 설계

## 16.1 CommonToolResponse

```csharp
public sealed class CommonToolResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public List<string> Warnings { get; init; } = new();
    public T? Data { get; init; }
}
```

---

## 16.2 ImportDicomStudyRequest

```csharp
public sealed class ImportDicomStudyRequest
{
    public string DicomDirectory { get; init; } = string.Empty;
}
```

---

## 16.3 ImportNiftiSegmentationRequest

```csharp
public sealed class ImportNiftiSegmentationRequest
{
    public List<string> Paths { get; init; } = new();
}
```

---

## 16.4 BuildSurfaceMeshRequest

```csharp
public sealed class BuildSurfaceMeshRequest
{
    public string SourceId { get; init; } = string.Empty;
    public List<string> RoiIds { get; init; } = new();
    public int SmoothingIterations { get; init; } = 10;
    public double DecimationRatio { get; init; } = 0.0;
}
```

---

## 16.5 PlaceElectrodeAtPoseRequest

```csharp
public sealed class PlaceElectrodeAtPoseRequest
{
    public double[] Position { get; init; } = new double[3];
    public double[] Normal { get; init; } = new double[3];
    public string ElectrodeType { get; init; } = "pad";
    public ElectrodeSpecDto? Spec { get; init; }
}
```

---

## 16.6 AttachmentParametersDto

```csharp
public sealed class AttachmentParametersDto
{
    public double ContactThresholdMm { get; init; } = 2.0;
    public int ExpansionRings { get; init; } = 2;
    public int SmoothIterations { get; init; } = 2;
    public double HardWeight { get; init; } = 8.0;
    public double SoftWeight { get; init; } = 0.35;
}
```

---

## 16.7 ExportMeshPackageRequest

```csharp
public sealed class ExportMeshPackageRequest
{
    public string OutputDirectory { get; init; } = string.Empty;
    public bool IncludeStl { get; init; } = true;
    public bool IncludePoly { get; init; } = true;
    public bool IncludeMetadata { get; init; } = true;
    public bool Overwrite { get; init; } = false;
}
```

---

## 17. C# MCP Tool 구현 예시 구조

아래 코드는 실제 패키지/API에 따라 달라질 수 있으므로 구조 예시로만 사용한다.

```csharp
public sealed class ImportTools
{
    private readonly IMeshGenCommandBridge _bridge;

    public ImportTools(IMeshGenCommandBridge bridge)
    {
        _bridge = bridge;
    }

    public async Task<CommonToolResponse<ImportDicomResult>> ImportDicomStudy(
        ImportDicomStudyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DicomDirectory))
        {
            return new CommonToolResponse<ImportDicomResult>
            {
                Success = false,
                Message = "DICOM directory is required."
            };
        }

        return await _bridge.ImportDicomStudyAsync(request, cancellationToken);
    }
}
```

---

## 18. App Command Dispatcher 예시

```csharp
public sealed class MeshGenCommandDispatcher : IMeshGenCommandBridge
{
    private readonly DicomImportService _dicomImportService;
    private readonly SurfaceMeshService _surfaceMeshService;
    private readonly MeshPreviewService _meshPreviewService;
    private readonly Dispatcher _uiDispatcher;

    public async Task<CommonToolResponse<PreviewMeshResult>> PreviewMeshAsync(
        PreviewMeshRequest request,
        CancellationToken cancellationToken)
    {
        return await _uiDispatcher.InvokeAsync(() =>
        {
            return _meshPreviewService.Show(request.MeshId, request.Style, request.Opacity);
        });
    }

    public async Task<CommonToolResponse<BuildSurfaceMeshResult>> BuildSurfaceMeshAsync(
        BuildSurfaceMeshRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Task.Run(() =>
        {
            return _surfaceMeshService.Build(request, cancellationToken);
        }, cancellationToken);

        await _uiDispatcher.InvokeAsync(() =>
        {
            _meshPreviewService.RegisterMeshActors(result.MeshIds);
        });

        return result;
    }
}
```

---

## 19. 구현 우선순위

## 1단계: 최소 MCP 연결

목표: MeshGen 상태를 외부에서 읽고 기본 import를 수행한다.

- `meshgen/pipeline/status`
- `import_dicom_study`
- `import_nifti_segmentation`
- `import_stl_mesh`
- `import_poly_mesh`
- `list_rois`
- `set_external_roi`

---

## 2단계: Surface Mesh 생성

목표: DICOM/NIfTI에서 surface mesh를 만들 수 있게 한다.

- `build_label_map`
- `build_surface_mesh`
- `get_mesh_info`
- `meshgen/surface_meshes`

---

## 3단계: VTK Mesh Preview

목표: Hub 또는 LLM 명령으로 MeshGen viewport에서 mesh를 확인할 수 있게 한다.

- `preview_mesh`
- `hide_mesh_preview`
- `clear_mesh_previews`
- `set_mesh_preview_style`
- `focus_mesh_in_viewport`
- `meshgen/viewport/preview_state`

---

## 4단계: 전극 배치

목표: 좌표 기반 및 VTK 상호작용 기반 전극 배치를 모두 지원한다.

- `place_electrode_at_pose`
- `set_electrode_layers`
- `enter_electrode_placement_mode`
- `confirm_electrode_placement`
- `cancel_electrode_placement`
- `meshgen/electrodes`

---

## 5단계: 전극 부착

목표: 전극을 body surface에 부착하고 품질 리포트를 제공한다.

- `start_interactive_attachment_session`
- `get_attachment_session_state`
- `get_attachment_quality_preview`
- `confirm_attachment_session`
- `cancel_attachment_session`
- `set_attachment_parameters`
- `attach_electrodes_batch`
- `meshgen/attachment/session_status`
- `meshgen/attachment/last_report`

---

## 6단계: Volume Mesh / Export

목표: Oncofield solver로 넘길 수 있는 mesh package를 생성한다.

- `generate_volume_mesh`
- `export_surface_stl`
- `export_volume_poly`
- `export_mesh_package`
- `meshgen/export/history`

---

## 20. MVP 기준

MeshGen MCP의 1차 MVP는 다음 기능을 만족하면 된다.

```text
1. DICOM import 가능
2. NIfTI segmentation import 가능
3. ROI 목록 조회 가능
4. External ROI 설정 가능
5. Surface mesh 생성 가능
6. VTK viewport에서 mesh preview 가능
7. 전극을 좌표 기반으로 생성 가능
8. STL / POLY export 가능
9. pipeline/status resource 제공
```

2차 MVP에서는 다음을 추가한다.

```text
1. VTK 기반 전극 배치 모드
2. Interactive attachment session
3. Attachment quality preview
4. Confirm / cancel attachment
5. Mesh package export
```

---

## 21. MeshGen MCP에서 가장 중요한 설계 원칙

## 21.1 MeshGen은 mesh preparation 프로그램이다

MeshGen은 contouring 도구도 아니고 FEM solver도 아니다.

MeshGen의 핵심 가치는 다음이다.

```text
DICOM / NIfTI / STL / POLY
    -> ROI / label map
    -> surface mesh
    -> electrode placement
    -> electrode attachment
    -> volume mesh / POLY
    -> Oncofield solver input
```

---

## 21.2 MCP는 기존 기능을 얇게 감싸야 한다

MCP Tool 안에 mesh 알고리즘을 넣지 않는다.

나쁜 구조:

```text
MCP Tool 내부에서 marching cubes, smoothing, attachment 직접 구현
```

좋은 구조:

```text
MCP Tool -> Command Bridge -> SurfaceMeshService / AttachmentService 호출
```

---

## 21.3 VTK 상호작용은 MeshGen 내부에서 처리한다

전극 부착은 단순 API 호출이 아니라 사용자의 시각적 판단이 필요한 작업이다.

따라서 다음 구조가 맞다.

```text
MCP: start session / query state / confirm / cancel
VTK: hover / picking / preview / rendering / user click
Core: attachment calculation / mesh update / report
```

---

## 21.4 Hub와 연결하더라도 MeshGen의 독립성을 유지한다

Hub가 MeshGen을 제어하더라도 MeshGen은 단독 실행 가능한 프로그램이어야 한다.

```text
Hub 없이도 MeshGen UI에서 모든 작업 가능
Hub 또는 LLM은 MCP를 통해 같은 기능을 호출
```

이 구조가 유지보수와 제품 안정성에 가장 유리하다.

---

## 22. 최종 권장 구조

```text
[Hub / LLM / MCP Client]
        │
        ▼
[MeshGen MCP Server]
        │
        ▼
[MeshGen Command Bridge]
        │
        ├───────────────┐
        ▼               ▼
[MeshGen Core]      [VTK UI Thread]
        │               │
        ├─ Import        ├─ Mesh Preview
        ├─ ROI           ├─ Electrode Placement
        ├─ Label Map     ├─ Attachment Preview
        ├─ Surface Mesh  └─ Scene Rendering
        ├─ Electrode
        ├─ Attachment
        ├─ Volume Mesh
        └─ Export
        │
        ▼
[STL / POLY / Metadata Export]
        │
        ▼
[Oncofield Solver Input]
```

---

## 23. 한 줄 결론

**MeshGen MCP는 DICOM/NIfTI/STL/POLY를 입력받아 surface mesh, 전극 배치, 전극 부착, volume mesh, STL/POLY export를 수행하는 MeshGen의 기능을 Tool/Resource로 노출하는 wrapper 계층이며, VTK 상호작용이 필요한 전극 배치와 부착은 세션 기반 Interactive Tool로 설계하는 것이 가장 적절하다.**
