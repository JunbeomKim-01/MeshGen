# MCP 구조 문서
## WPF / VTK / C# 기반 전극 부착 상호작용 시스템을 위한 MCP 아키텍처 설계

---

## 1. 문서 목적

본 문서는 **WPF + VTK 기반 3D 애플리케이션**에서 전극 배치 및 부착 기능을 구현할 때,  
**MCP(Model Context Protocol)** 를 어떤 구조로 배치해야 하는지 정리하기 위한 설계 문서이다.

특히 본 문서는 다음 요구사항을 반영한다.

- 전극 부착은 **VTK window 또는 VTK viewport에서 실제 상호작용**으로 수행되어야 한다.
- 사용자는 마우스 클릭, hover, preview, confirm/cancel 과정을 거쳐 전극을 배치해야 한다.
- MCP는 전극을 직접 렌더링하거나 부착하는 엔진이 아니라,  
  **해당 상호작용 세션을 시작하고 상태를 조회하고 확정/취소를 요청하는 제어 계층**이어야 한다.
- 기존 Core 서비스(Pipeline, Electrode, Attachment, Mesh, Export 등)는 MCP의 하위에서 재사용되어야 한다.

---

## 2. 핵심 결론

이 구조에서 가장 중요한 결론은 다음과 같다.

### 2.1 전극 부착 기능의 본체는 앱 내부에 있어야 한다
전극 부착은 다음 요소에 강하게 의존한다.

- 3D scene 렌더링
- surface picking
- preview actor
- 마우스 상호작용
- confirm / cancel
- 즉시 시각적 피드백

따라서 이 기능은 **WPF + VTK 애플리케이션 내부 기능**으로 구현되어야 한다.

### 2.2 MCP는 제어 및 상태 조회 계층이어야 한다
MCP는 다음 역할을 담당한다.

- interactive attachment session 시작
- 현재 session 상태 조회
- 현재 preview 상태 조회
- confirm / cancel 명령 전달
- attachment parameter 변경
- attachment quality report 조회
- pipeline 상태, mesh 상태, ROI 상태, export 상태 조회

즉 MCP는 **앱 기능을 외부 LLM/챗 인터페이스가 호출할 수 있게 하는 wrapper 계층**이다.

### 2.3 Interactive tool과 non-interactive tool을 분리해야 한다
전극 부착은 두 종류의 명령으로 나뉜다.

- **Interactive tool**: VTK 창이 열리고 사용자가 직접 상호작용해야 하는 도구
- **Non-interactive tool**: 좌표, 파일 경로, 파라미터가 주어지면 바로 실행 가능한 도구

이 둘을 구분하지 않으면 MCP 구조가 불명확해진다.

---

## 3. 전체 구조도

```text
┌───────────────────────────────────────────────────────────────┐
│                       User / Operator                        │
│  - 마우스 입력                                                │
│  - 키보드 입력                                                │
│  - 버튼 클릭                                                  │
│  - Chat 명령                                                  │
└───────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                       WPF Application                         │
│  - Toolbar / Ribbon                                           │
│  - Property Panel                                             │
│  - Status Panel                                               │
│  - Chat Panel (optional)                                      │
│  - VTK Viewport Host                                          │
└───────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                      VTK Interaction Layer                    │
│  - vtkRenderWindow                                            │
│  - vtkRenderer                                                │
│  - vtkInteractor                                              │
│  - vtkCellPicker                                              │
│  - Preview Mesh Actor                                         │
│  - Preview Electrode Actor                                    │
│  - Final Electrode Actor                                      │
└───────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌───────────────────────────────────────────────────────────────┐
│                        Application Core                       │
│  - PipelineService                                            │
│  - MeshService / MeshImportService / MeshExportService        │
│  - Nifti / Dicom Service                                      │
│  - ElectrodeRepository                                        │
│  - ElectrodePlacementController                               │
│  - AttachmentInteractionController                            │
│  - ElasticDeformationService                                  │
│  - VolumeMeshService                                          │
│  - SceneStateService                                          │
│  - AttachmentSessionStateService                              │
└───────────────────────────────────────────────────────────────┘
                              │
                 ┌────────────┴────────────┐
                 │                         │
                 ▼                         ▼
┌──────────────────────────────┐   ┌────────────────────────────┐
│       Scene / Study Data     │   │         MCP Server         │
│  - Study                     │   │  - Tool Exposure           │
│  - ROIs                      │   │  - Resource Exposure       │
│  - Surface Meshes            │   │  - Prompt Exposure         │
│  - Electrode List            │   │  - App Command Bridge      │
│  - Attachment Session State  │   │  - State Query Adapter     │
└──────────────────────────────┘   └────────────────────────────┘
                                              │
                                              ▼
┌───────────────────────────────────────────────────────────────┐
│                   LLM / Chat / AI Assistant                   │
│  - Ollama local LLM                                           │
│  - Chat UI                                                    │
│  - Remote LLM client (optional)                               │
└───────────────────────────────────────────────────────────────┘
```

---

## 4. 역할 분리

## 4.1 WPF + VTK의 역할

WPF + VTK는 **실제 전극 부착 상호작용의 중심**이다.

담당 기능:
- 3D mesh 렌더링
- camera control
- body surface picking
- 마우스 hover 기반 preview
- 전극 preview 갱신
- 전극 배치 모드 처리
- confirm / cancel
- attachment 결과 표시
- mesh preview 표시
- 최종 actor scene 반영

즉, **전극을 실제로 붙이는 행위는 VTK 창 또는 VTK viewport 내부에서 수행된다.**

## 4.2 MCP의 역할

MCP는 **앱을 제어하고 상태를 읽는 인터페이스**다.

담당 기능:
- interactive attachment session 시작
- attachment session 상태 조회
- mesh preview 요청
- pipeline 상태 조회
- electrode list, ROI list, imported mesh 상태 조회
- parameter 설정
- confirm / cancel 전달
- export / import / meshing command 전달

즉 MCP는 **실행 엔진이 아니라 orchestration 및 tool exposure 계층**이다.

---

## 5. Interactive Attachment Session 구조

전극 부착은 단일 함수 호출이 아니라 **상태 기반 상호작용 세션**으로 보는 것이 적절하다.

### 5.1 상태 머신

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

### 5.2 상태 설명

- **Idle**  
  일반 상태. 전극 부착 세션이 열려 있지 않다.

- **AttachmentSessionStarting**  
  VTK viewport 또는 attachment window가 준비되고, 필요한 mesh/electrode/state를 로드하는 중이다.

- **AttachmentInteractive**  
  사용자가 마우스를 움직이며 preview를 보고 surface 위에서 전극 위치를 조정하는 상태다.

- **PreviewValid**  
  현재 preview pose가 outer surface 기준으로 유효하다.

- **PreviewInvalid**  
  현재 preview pose가 penetration 또는 invalid hit 때문에 부착 불가 상태다.

- **Confirmed**  
  사용자가 현재 preview를 확정했다.

- **Attached**  
  attachment engine 실행 후 최종 전극과 body deformation 결과가 scene에 반영되었다.

- **Cancelled**  
  사용자가 세션을 취소했다.

### 5.3 MCP의 역할

MCP는 이 상태를 **조회하거나 전환 요청**할 수 있다.

예:
- `start_interactive_attachment_session()` → `AttachmentSessionStarting`
- 사용자 hover / click → `AttachmentInteractive`
- `get_attachment_session_state()` → 현재 valid / invalid 상태 조회
- `confirm_attachment_session()` → `Confirmed`
- `cancel_attachment_session()` → `Cancelled`

---

## 6. 실제 상호작용 흐름

## 6.1 사용자 중심 흐름

```text
1. User clicks "Attach Electrode" in WPF UI
2. App enters attachment interaction mode
3. VTK viewport activates picker and preview actor
4. User moves mouse over body mesh
5. App computes preview pose
6. Initial safe placement is applied
7. Preview validity is evaluated
8. User clicks to confirm
9. Attachment engine runs
10. Final electrode and body deformation are applied
11. Scene is rendered
```

## 6.2 MCP가 포함된 흐름

```text
1. LLM requests "start interactive attachment session"
2. MCP sends command to app
3. App activates VTK interaction mode
4. User interacts in viewport
5. MCP can query current preview/session state
6. LLM requests "confirm attachment session"
7. App runs attachment engine
8. MCP reads quality report and returns summary
```

---

## 7. Interactive tool과 Non-interactive tool 구분

## 7.1 Interactive tool

이 도구들은 **VTK 창의 상호작용 세션을 전제로 한다.**

예시:
- `start_interactive_attachment_session`
- `get_attachment_session_state`
- `get_attachment_preview_state`
- `confirm_attachment_session`
- `cancel_attachment_session`
- `enter_electrode_placement_mode`
- `confirm_electrode_placement`
- `cancel_electrode_placement`

이 도구들은 실제 클릭을 대신하지 않는다.  
대신 앱에게 **특정 상호작용 상태로 진입하거나, 현재 상태를 보고하거나, 확정/취소를 수행하라**고 명령한다.

## 7.2 Non-interactive tool

이 도구들은 좌표, 파일 경로, 파라미터가 이미 주어졌다면 즉시 실행 가능하다.

예시:
- `import_dicom_study`
- `import_nifti_segmentation`
- `import_stl_mesh`
- `import_poly_mesh`
- `build_label_map`
- `build_surface_mesh`
- `place_electrode_at_pose`
- `set_electrode_layers`
- `attach_electrodes_batch`
- `generate_volume_mesh`
- `export_surface_stl`
- `export_volume_poly`
- `preview_mesh`
- `hide_mesh_preview`
- `clear_mesh_previews`

---

## 8. 기존 서비스와 MCP 매핑

다음은 현재 서비스와 MCP surface 사이의 권장 매핑이다.

### 8.1 Import / Pipeline

- `import_dicom_study`  
  → `PipelineService.ImportAsync`

- `import_nifti_segmentation`  
  → `PipelineService.ImportAsync` with `NiftiPaths`

- `import_stl_mesh`  
  → `MeshImportService.ImportStlAsync`

- `import_poly_mesh`  
  → `MeshImportService.ImportPolyAsync`

### 8.2 ROI / Study

- `list_rois`  
  → `Study.Rois` 반환

- `set_external_roi`  
  → `Study.ExternalRoiId` 설정

### 8.3 Surface / Label

- `build_label_map`  
  → `BuildLabelMapAsync`

- `build_surface_mesh`  
  → `BuildSurfaceAsync`

### 8.4 Electrode / Placement / Attachment

- `place_electrode_at_pose`  
  → `ElectrodeRepository.CreateAndAdd`

- `set_electrode_layers`  
  → `ElectrodeSpec.Layers` 갱신

- `start_interactive_attachment_session`  
  → `AttachmentInteractionController.StartSession(...)`

- `confirm_attachment_session`  
  → `AttachmentInteractionController.ConfirmSession(...)`

- `cancel_attachment_session`  
  → `AttachmentInteractionController.CancelSession(...)`

- `attach_electrodes_batch`  
  → `ElasticDeformationService.AttachAsync`

### 8.5 Volume Mesh / Export

- `generate_volume_mesh`  
  → `VolumeMeshService.GenerateAsync`

- `export_surface_stl`  
  → `MeshExportService.WriteStl`

- `export_volume_poly`  
  → `MeshExportService.WritePoly`

### 8.6 Mesh Preview

- `preview_mesh`  
  → `MeshPreviewService.Show(...)`

- `hide_mesh_preview`  
  → `MeshPreviewService.Hide(...)`

- `clear_mesh_previews`  
  → `MeshPreviewService.ClearAll()`

- `set_mesh_preview_style`  
  → `MeshPreviewService.UpdateStyle(...)`

- `focus_mesh_in_viewport`  
  → `ViewportService.FocusMesh(...)`

---

## 9. Resource 설계

다음 정보는 tool보다는 **resource**로 제공하는 것이 더 자연스럽다.

### 9.1 pipeline/status
현재 study / ROI / surface / electrode / attachment / volume mesh 상태를 요약한 JSON

예:
```json
{
  "studyLoaded": true,
  "roiCount": 18,
  "externalRoiId": "roi_external",
  "surfaceBuilt": true,
  "electrodeCount": 3,
  "attachmentCompleted": false,
  "volumeMeshGenerated": false
}
```

### 9.2 study/rois
현재 study에 로드된 ROI 목록

### 9.3 study/electrodes
현재 전극 목록, electrode type, layer 수, placement 상태

### 9.4 study/imported_meshes
불러온 STL / POLY / surface mesh 목록

### 9.5 viewport/mesh_preview_state
현재 viewport에 preview 중인 mesh 목록, style, opacity, visibility

### 9.6 attachment/session_status
현재 interactive attachment session 상태 요약

예:
```json
{
  "sessionActive": true,
  "selectedElectrodeId": "elec_01",
  "previewValid": false,
  "warnings": ["Outer surface penetration detected"],
  "maxPenetrationMm": 1.8
}
```

---

## 10. 권장 클래스 구조

## 10.1 앱 내부 프로젝트 구조

```text
Oncofield.App
  ├─ UI
  │   ├─ MainWindow
  │   ├─ ChatPanelView
  │   ├─ PipelinePanel
  │   ├─ PlacementToolbar
  │   └─ AttachmentToolbar
  │
  ├─ Vtk
  │   ├─ VtkViewportHost
  │   ├─ VtkInteractorAdapter
  │   ├─ BodyMeshActorManager
  │   ├─ ElectrodeActorManager
  │   ├─ MeshPreviewActorManager
  │   └─ AttachmentPreviewActorManager
  │
  ├─ Core
  │   ├─ PipelineService
  │   ├─ MeshImportService
  │   ├─ MeshExportService
  │   ├─ VolumeMeshService
  │   ├─ ElectrodeRepository
  │   ├─ ElectrodePlacementController
  │   ├─ AttachmentInteractionController
  │   ├─ ElasticDeformationService
  │   ├─ SceneStateService
  │   ├─ AttachmentSessionStateService
  │   └─ AttachmentReportService
  │
  └─ Integration
      ├─ OllamaChatService
      ├─ McpCommandBridge
      └─ McpStateAdapter
```

## 10.2 MCP 프로젝트 구조

```text
Oncofield.McpServer
  ├─ Tools
  │   ├─ PipelineTools
  │   ├─ MeshTools
  │   ├─ PreviewTools
  │   ├─ ElectrodeTools
  │   └─ AttachmentTools
  │
  ├─ Resources
  │   ├─ PipelineStatusResource
  │   ├─ RoiListResource
  │   ├─ ImportedMeshesResource
  │   ├─ ElectrodeListResource
  │   └─ AttachmentSessionResource
  │
  └─ Bridge
      ├─ AppCommandClient
      └─ AppStateClient
```

---

## 11. Command Bridge 구조

MCP는 실제로 앱 내부 controller/service를 직접 호출하지 않고,  
**Command Bridge** 를 통해 명령과 상태 조회를 전달하는 구조가 가장 안전하다.

```text
LLM / MCP Tool
      │
      ▼
McpCommandBridge
      │
      ▼
Application Command Dispatcher
      │
      ├─ PipelineService
      ├─ ElectrodePlacementController
      ├─ AttachmentInteractionController
      ├─ ElasticDeformationService
      ├─ MeshPreviewService
      └─ ReportService
```

예시 명령:
- `StartAttachmentSession`
- `ConfirmAttachmentSession`
- `CancelAttachmentSession`
- `PreviewMesh`
- `SetAttachmentParameters`
- `ImportDicomStudy`
- `ExportSurfaceStl`

---

## 12. 앱과 MCP 연결 방식

## 12.1 동일 프로세스 내부

WPF 앱 내부에 MCP 호스트를 함께 띄운다.

장점:
- 구현 단순
- 상태 공유 쉬움
- 빠른 프로토타이핑 가능

단점:
- UI thread / background command thread 분리 주의
- MCP 예외가 앱에 영향 가능
- 장기 유지보수 시 결합도 증가

## 12.2 별도 프로세스

MCP 서버를 별도 프로세스로 두고, 앱과는 IPC / Named Pipe / gRPC 등으로 통신한다.

장점:
- 안정성 높음
- UI와 MCP 역할 분리 명확
- 외부 연결 확장 쉬움
- 장애 격리 가능

단점:
- 초기 구현 복잡도 증가

## 12.3 권장 방향

초기 개발은 동일 프로세스도 가능하지만,  
장기적으로는 **별도 프로세스 + App Command Bridge** 구조가 가장 안정적이다.

---

## 13. 왜 이 구조가 적절한가

### 13.1 전극 부착은 본질적으로 UI interaction 문제다
전극 부착은 다음이 필요하다.

- surface hover
- click
- preview
- validity 표시
- confirm / cancel
- scene actor update

이는 단일 tool 호출로 끝나는 문제가 아니라 **실시간 상호작용 문제**다.

### 13.2 MCP는 orchestration 계층으로서 가치가 크다
MCP를 이용하면 LLM은 다음을 할 수 있다.

- 현재 scene 상태 설명
- 현재 preview가 왜 invalid인지 설명
- 사용자가 놓친 파라미터를 추천
- attachment quality report를 요약
- import / export / meshing workflow를 단계적으로 안내

즉 MCP는 “렌더링 대체자”가 아니라 **상태를 이해하고 명령을 연결하는 오케스트레이터**로 쓰는 것이 맞다.

### 13.3 점진적 확장이 가능하다
처음에는 다음만 열 수 있다.

- 상태 조회
- 품질 리포트
- 파이프라인 명령
- mesh preview

그 다음 점차:

- placement mode 제어
- interactive attachment session 제어
- 자동 리포트 설명
- LLM 기반 assistant workflow

로 확장할 수 있다.

---

## 14. 최종 권장 MCP Tool 목록

### Pipeline / Import
- `import_dicom_study`
- `import_nifti_segmentation`
- `import_stl_mesh`
- `import_poly_mesh`
- `list_rois`
- `set_external_roi`
- `build_label_map`
- `build_surface_mesh`

### Mesh Preview
- `preview_mesh`
- `hide_mesh_preview`
- `clear_mesh_previews`
- `set_mesh_preview_style`
- `focus_mesh_in_viewport`
- `get_mesh_info`

### Electrode
- `place_electrode_at_pose`
- `set_electrode_layers`
- `enter_electrode_placement_mode`
- `confirm_electrode_placement`
- `cancel_electrode_placement`

### Interactive Attachment
- `start_interactive_attachment_session`
- `get_attachment_session_state`
- `get_attachment_quality_preview`
- `confirm_attachment_session`
- `cancel_attachment_session`
- `set_attachment_parameters`

### Batch Attachment / Meshing / Export
- `attach_electrodes_batch`
- `generate_volume_mesh`
- `export_surface_stl`
- `export_volume_poly`

### Resources
- `pipeline/status`
- `study/rois`
- `study/electrodes`
- `study/imported_meshes`
- `viewport/mesh_preview_state`
- `attachment/session_status`

---

## 15. 최종 요약 구조

```text
[User / LLM / Chat]
        │
        ▼
[WPF UI] <──────> [MCP Server]
        │              │
        ▼              ▼
[VTK Interaction]  [Command / State Bridge]
        │              │
        └──────┬───────┘
               ▼
        [Application Core]
   - PipelineService
   - ElectrodePlacementController
   - AttachmentInteractionController
   - ElasticDeformationService
   - MeshPreviewService
   - VolumeMeshService
   - ExportService
               │
               ▼
      [Study / Mesh / Electrode / Session State]
```

---

## 16. 한 줄 결론

**전극 부착은 WPF + VTK 창에서 실제 상호작용으로 수행하고, MCP는 그 상호작용 세션을 시작하고 상태를 읽고 확정/취소를 명령하는 보조 제어 인터페이스로 두는 것이 가장 올바른 구조다.**
