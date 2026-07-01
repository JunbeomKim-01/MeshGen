# MCP Tool 명세표
## Oncofield WPF/VTK + C# 애플리케이션용 MCP Surface Specification

---

## 1. 문서 목적

본 문서는 Oncofield 애플리케이션의 핵심 기능을 MCP(Model Context Protocol) 기준으로 노출하기 위한 **Tool / Resource 명세표**이다.

이 명세의 목적은 다음과 같다.

- WPF/VTK 앱 내부 기능을 MCP Tool로 일관되게 노출한다.
- Interactive 기능과 Non-interactive 기능을 구분한다.
- 각 Tool의 입력, 출력, 사전조건, 부작용을 명확히 정의한다.
- LLM, Chat UI, 외부 클라이언트가 동일한 방식으로 앱 기능을 호출할 수 있게 한다.

---

## 2. 설계 원칙

### 2.1 Tool과 Resource를 분리한다
- **Tool**: 상태를 바꾸거나 작업을 실행하는 명령
- **Resource**: 현재 상태를 읽는 읽기 전용 인터페이스

### 2.2 Interactive Tool과 Non-Interactive Tool을 분리한다
- **Interactive Tool**: VTK viewport/window와 사용자 상호작용이 필요함
- **Non-Interactive Tool**: 입력값만으로 바로 실행 가능함

### 2.3 MCP는 Wrapper 계층이다
Tool은 직접 핵심 비즈니스를 구현하지 않는다.  
Tool은 내부 Core Service를 호출하는 **얇은 wrapper** 여야 한다.

---

## 3. 공통 응답 형식 권장안

모든 Tool은 가능한 한 아래 공통 필드를 포함하는 응답을 반환하는 것을 권장한다.

```json
{
  "success": true,
  "message": "Human-readable summary",
  "warnings": [],
  "data": {}
}
```

### 공통 필드 의미
- `success`: 실행 성공 여부
- `message`: 사람이 읽기 쉬운 요약
- `warnings`: 치명적이지 않은 경고 목록
- `data`: Tool별 구조화된 결과

---

## 4. Tool 명세표

## 4.1 Pipeline / Import 계열

| Tool Name | Type | Interactive | 목적 | Backend Mapping | 주요 입력 | 주요 출력 | 사전조건 | 부작용 |
|---|---|---:|---|---|---|---|---|---|
| `import_dicom_study` | Tool | No | DICOM Study를 불러와 현재 study로 설정 | `PipelineService.ImportAsync` | `dicomDirectory` | `studyId`, `seriesCount`, `loaded` | 유효한 DICOM 폴더 경로 | 현재 study 상태 갱신 |
| `import_nifti_segmentation` | Tool | No | NIfTI segmentation(.nii/.nii.gz) 로드 | `PipelineService.ImportAsync` with `NiftiPaths` | `paths[]` | `loadedFiles`, `studyId`, `segmentationCount` | 유효한 파일 경로 | study / segmentation 상태 갱신 |
| `import_stl_mesh` | Tool | No | STL surface mesh를 로드하여 scene에 등록 | `MeshImportService.ImportStlAsync` | `path`, `meshRole?` | `meshId`, `triangleCount`, `bounds` | 유효한 STL 파일 | imported mesh 목록 갱신 |
| `import_poly_mesh` | Tool | No | POLY mesh 또는 PLC 입력 파일 로드 | `MeshImportService.ImportPolyAsync` | `path`, `meshRole?` | `meshId`, `pointCount`, `facetCount` | 유효한 POLY 파일 | imported mesh 목록 갱신 |

### 입력 예시: `import_dicom_study`
```json
{
  "dicomDirectory": "D:/Patient001/DICOM"
}
```

### 출력 예시: `import_stl_mesh`
```json
{
  "success": true,
  "message": "STL mesh imported.",
  "warnings": [],
  "data": {
    "meshId": "mesh_external_01",
    "triangleCount": 128430,
    "bounds": [0, 180, 0, 220, -40, 95]
  }
}
```

---

## 4.2 ROI / Study 계열

| Tool Name | Type | Interactive | 목적 | Backend Mapping | 주요 입력 | 주요 출력 | 사전조건 | 부작용 |
|---|---|---:|---|---|---|---|---|---|
| `list_rois` | Tool | No | 현재 study의 ROI 목록 반환 | `Study.Rois` | 없음 또는 `studyId?` | ROI 리스트 | study 로드됨 | 없음 |
| `set_external_roi` | Tool | No | 외부 body 기준 ROI를 설정 | `Study.ExternalRoiId = ...` | `roiId` | `externalRoiId`, `updated` | 해당 ROI 존재 | attachment / surface 기준 ROI 변경 |

### 출력 예시: `list_rois`
```json
{
  "success": true,
  "message": "18 ROIs found.",
  "warnings": [],
  "data": {
    "rois": [
      { "roiId": "roi_external", "name": "External" },
      { "roiId": "roi_liver", "name": "Liver" }
    ]
  }
}
```

---

## 4.3 Label / Surface 계열

| Tool Name | Type | Interactive | 목적 | Backend Mapping | 주요 입력 | 주요 출력 | 사전조건 | 부작용 |
|---|---|---:|---|---|---|---|---|---|
| `build_label_map` | Tool | No | segmentation / ROI 기반 label map 생성 | `BuildLabelMapAsync` | `roiIds[]?`, `spacing?` | `labelMapId`, `dimensions`, `labels` | study 또는 segmentation 로드 | label map 상태 갱신 |
| `build_surface_mesh` | Tool | No | label map 또는 ROI로부터 surface mesh 생성 | `BuildSurfaceAsync` | `sourceId`, `roiIds[]?`, `smoothingIterations?`, `decimationRatio?` | `meshIds[]`, `triangleCounts` | label map 또는 source 준비됨 | surface mesh 생성 및 등록 |

### 입력 예시: `build_surface_mesh`
```json
{
  "sourceId": "labelmap_01",
  "roiIds": ["roi_external", "roi_liver"],
  "smoothingIterations": 10,
  "decimationRatio": 0.2
}
```

---

## 4.4 Mesh Preview 계열

| Tool Name | Type | Interactive | 목적 | Backend Mapping | 주요 입력 | 주요 출력 | 사전조건 | 부작용 |
|---|---|---:|---|---|---|---|---|---|
| `preview_mesh` | Tool | Yes | 특정 mesh를 VTK viewport에 preview actor로 표시 | `MeshPreviewService.Show(...)` | `meshId`, `style?`, `opacity?`, `color?` | `previewActorId`, `visible` | mesh 존재, viewport 준비됨 | preview actor 생성/갱신 |
| `hide_mesh_preview` | Tool | Yes | 특정 mesh preview를 숨김 | `MeshPreviewService.Hide(...)` | `meshId` 또는 `previewActorId` | `hidden` | preview 존재 | preview visibility 변경 |
| `clear_mesh_previews` | Tool | Yes | 모든 mesh preview actor 제거 | `MeshPreviewService.ClearAll()` | 없음 | `clearedCount` | viewport 준비됨 | 모든 preview actor 제거 |
| `set_mesh_preview_style` | Tool | Yes | preview 표시 스타일 변경 | `MeshPreviewService.UpdateStyle(...)` | `meshId`, `style`, `opacity?`, `color?` | `updated` | preview 존재 | preview style 변경 |
| `focus_mesh_in_viewport` | Tool | Yes | 특정 mesh가 잘 보이도록 카메라 맞춤 | `ViewportService.FocusMesh(...)` | `meshId` | `cameraUpdated` | mesh 존재 | 카메라 상태 변경 |
| `get_mesh_info` | Tool | No | mesh의 기하 정보 조회 | `MeshQueryService.GetInfo(...)` | `meshId` | `pointCount`, `triangleCount`, `bounds`, `isClosed?` | mesh 존재 | 없음 |

### `preview_mesh` 입력 예시
```json
{
  "meshId": "mesh_liver_01",
  "style": "surface",
  "opacity": 0.45
}
```

---

## 4.5 Electrode Placement 계열

| Tool Name | Type | Interactive | 목적 | Backend Mapping | 주요 입력 | 주요 출력 | 사전조건 | 부작용 |
|---|---|---:|---|---|---|---|---|---|
| `place_electrode_at_pose` | Tool | No | 주어진 pose에 전극 생성 | `ElectrodeRepository.CreateAndAdd` | `position[3]`, `normal[3]`, `electrodeType`, `spec?` | `electrodeId`, `worldPose` | scene / target mesh 준비됨 | electrode 목록 갱신 |
| `set_electrode_layers` | Tool | No | 전극 레이어 설정 변경 | `ElectrodeSpec.Layers` 갱신 | `electrodeId`, `layers[]` | `layerCount`, `updated` | electrode 존재 | electrode spec 변경 |
| `enter_electrode_placement_mode` | Tool | Yes | VTK viewport를 전극 배치 모드로 전환 | `ElectrodePlacementController.EnterMode()` | `electrodeType?`, `spec?` | `modeEntered`, `sessionId?` | viewport 준비됨 | picker/preview 활성화 |
| `confirm_electrode_placement` | Tool | Yes | 현재 preview를 실제 전극으로 확정 | `ElectrodePlacementController.Confirm()` | 없음 또는 `sessionId?` | `electrodeId`, `placed` | placement mode 활성화, preview 존재 | 전극 actor 생성 |
| `cancel_electrode_placement` | Tool | Yes | 현재 배치 모드를 취소 | `ElectrodePlacementController.Cancel()` | 없음 또는 `sessionId?` | `cancelled` | placement mode 활성화 | preview 제거, mode 종료 |

### `place_electrode_at_pose` 출력 예시
```json
{
  "success": true,
  "message": "Electrode created.",
  "warnings": [],
  "data": {
    "electrodeId": "elec_01",
    "worldPose": {
      "position": [12.2, 38.5, 4.0],
      "normal": [0.0, 0.0, 1.0]
    }
  }
}
```

---

## 4.6 Interactive Attachment Session 계열

| Tool Name | Type | Interactive | 목적 | Backend Mapping | 주요 입력 | 주요 출력 | 사전조건 | 부작용 |
|---|---|---:|---|---|---|---|---|---|
| `start_interactive_attachment_session` | Tool | Yes | VTK 상호작용 기반 attachment 세션 시작 | `AttachmentInteractionController.StartSession(...)` | `electrodeIds[]?`, `targetMeshId?`, `parameters?` | `sessionId`, `started` | viewport, target mesh, electrode 준비됨 | session 상태 생성, preview 활성화 |
| `get_attachment_session_state` | Tool | No | 현재 interactive attachment 세션 상태 조회 | `AttachmentSessionStateService.GetState()` | `sessionId?` | `sessionActive`, `previewValid`, `warnings`, `selectedElectrodeId` | session 존재 | 없음 |
| `get_attachment_quality_preview` | Tool | No | 현재 preview의 gap/penetration 품질 요약 조회 | `AttachmentPreviewService.GetQualityPreview()` | `sessionId?` | `maxPenetrationMm`, `meanGapMm`, `coverageRatio` | session 존재, preview 계산됨 | 없음 |
| `confirm_attachment_session` | Tool | Yes | 현재 interactive preview를 확정하고 attachment 실행 | `AttachmentInteractionController.ConfirmSession(...)` | `sessionId?` | `attachedCount`, `qualityReport` | session 활성화, preview valid 또는 confirm 허용 | attachment 수행, body/electrode 상태 갱신 |
| `cancel_attachment_session` | Tool | Yes | 현재 interactive attachment 세션 취소 | `AttachmentInteractionController.CancelSession(...)` | `sessionId?` | `cancelled` | session 활성화 | preview 제거, session 종료 |
| `set_attachment_parameters` | Tool | No | attachment 파라미터 갱신 | `AttachmentParameterService.Update(...)` | `contactThresholdMm?`, `expansionRings?`, `smoothIterations?`, `hardWeight?`, `softWeight?` | `updatedParameters` | 없음 | 이후 preview/attachment 계산에 영향 |

### `set_attachment_parameters` 입력 예시
```json
{
  "contactThresholdMm": 2.0,
  "expansionRings": 2,
  "smoothIterations": 2,
  "hardWeight": 8.0,
  "softWeight": 0.35
}
```

### `get_attachment_session_state` 출력 예시
```json
{
  "success": true,
  "message": "Attachment session is active.",
  "warnings": ["Outer surface penetration detected"],
  "data": {
    "sessionActive": true,
    "previewValid": false,
    "selectedElectrodeId": "elec_01",
    "maxPenetrationMm": 1.8
  }
}
```

---

## 4.7 Batch Attachment / Volume Mesh / Export 계열

| Tool Name | Type | Interactive | 목적 | Backend Mapping | 주요 입력 | 주요 출력 | 사전조건 | 부작용 |
|---|---|---:|---|---|---|---|---|---|
| `attach_electrodes_batch` | Tool | No | 여러 전극에 대해 비상호작용 attachment 일괄 수행 | `ElasticDeformationService.AttachAsync` | `electrodeIds[]?`, `parameters?` | `attachedCount`, `qualityReport` | body / electrode / external ROI 준비 | body deformation, electrode 상태 갱신 |
| `generate_volume_mesh` | Tool | No | surface 또는 PLC 입력으로 volume mesh 생성 | `VolumeMeshService.GenerateAsync` | `sourceMeshIds[]`, `options?` | `volumeMeshId`, `cellCount` | 필요한 surface/PLC 준비 | volume mesh 생성 |
| `export_surface_stl` | Tool | No | surface mesh를 STL로 내보냄 | `MeshExportService.WriteStl` | `meshId`, `outputPath`, `overwrite?` | `written`, `outputPath` | mesh 존재 | 파일 생성 |
| `export_volume_poly` | Tool | No | volume/PLC mesh를 POLY로 내보냄 | `MeshExportService.WritePoly` | `meshId`, `outputPath`, `overwrite?` | `written`, `outputPath` | mesh 존재 | 파일 생성 |

### `attach_electrodes_batch` 출력 예시
```json
{
  "success": true,
  "message": "Batch attachment completed.",
  "warnings": [],
  "data": {
    "attachedCount": 3,
    "maxPenetrationMm": 0.4,
    "meanGapMm": 0.9
  }
}
```

---

## 5. Resource 명세표

| Resource Name | 목적 | 내용 | 갱신 시점 | 예시 |
|---|---|---|---|---|
| `pipeline/status` | 현재 전체 파이프라인 상태 요약 | study, ROI, surface, electrode, attachment, volume mesh 상태 | import / build / attach / export 이후 | 현재 study 로드 여부, electrode 수 |
| `study/rois` | 현재 study ROI 목록 | roiId, name, type, visible? | ROI 변경 시 | External, Liver 등 |
| `study/electrodes` | 현재 전극 목록 | electrodeId, type, layerCount, placed/attached 상태 | placement / attachment 이후 | 전극 ID와 상태 목록 |
| `study/imported_meshes` | 불러온 mesh 목록 | meshId, format, role, bounds, pointCount | import 이후 | STL/POLY mesh 목록 |
| `study/current_external_roi` | 현재 외부 기준 ROI | externalRoiId, name | `set_external_roi` 이후 | External ROI |
| `viewport/mesh_preview_state` | viewport preview 상태 | preview mesh 목록, style, opacity, visible | preview tool 호출 후 | 현재 preview actor 정보 |
| `attachment/session_status` | interactive attachment 세션 요약 | sessionActive, previewValid, warnings, selectedElectrodeId | interactive session 진행 중 | 현재 attachment preview 유효성 |

### Resource 예시: `pipeline/status`
```json
{
  "studyLoaded": true,
  "roiCount": 18,
  "externalRoiId": "roi_external",
  "labelMapBuilt": true,
  "surfaceBuilt": true,
  "electrodeCount": 3,
  "attachmentCompleted": false,
  "volumeMeshGenerated": false
}
```

---

## 6. 권장 입력 DTO 목록

### 6.1 ImportDicomStudyRequest
```json
{
  "dicomDirectory": "D:/Patient001/DICOM"
}
```

### 6.2 ImportNiftiSegmentationRequest
```json
{
  "paths": [
    "D:/Patient001/seg_external.nii.gz",
    "D:/Patient001/seg_liver.nii.gz"
  ]
}
```

### 6.3 PlaceElectrodeAtPoseRequest
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

### 6.4 AttachmentParameters
```json
{
  "contactThresholdMm": 2.0,
  "expansionRings": 2,
  "smoothIterations": 2,
  "hardWeight": 8.0,
  "softWeight": 0.35
}
```

---

## 7. 권장 구현 우선순위

### 1단계: 읽기/기본 실행
- `pipeline/status`
- `study/rois`
- `study/electrodes`
- `import_dicom_study`
- `import_nifti_segmentation`
- `import_stl_mesh`
- `import_poly_mesh`
- `get_mesh_info`

### 2단계: Surface / Preview
- `build_label_map`
- `build_surface_mesh`
- `preview_mesh`
- `hide_mesh_preview`
- `clear_mesh_previews`
- `focus_mesh_in_viewport`

### 3단계: Placement
- `place_electrode_at_pose`
- `enter_electrode_placement_mode`
- `confirm_electrode_placement`
- `cancel_electrode_placement`
- `set_electrode_layers`

### 4단계: Interactive Attachment
- `start_interactive_attachment_session`
- `get_attachment_session_state`
- `get_attachment_quality_preview`
- `confirm_attachment_session`
- `cancel_attachment_session`
- `set_attachment_parameters`

### 5단계: Batch / Export
- `attach_electrodes_batch`
- `generate_volume_mesh`
- `export_surface_stl`
- `export_volume_poly`

---

## 8. 최종 요약

이 MCP surface는 다음 세 가지 층으로 나뉜다.

1. **Pipeline / Mesh / ROI 관리**
2. **Electrode Placement / Interactive Attachment**
3. **Volume Meshing / Export / Status Reporting**

핵심 구조는 다음과 같다.

- 전극을 실제로 배치하고 부착하는 것은 **WPF + VTK 애플리케이션 내부**
- MCP는 해당 기능을 **Tool / Resource로 외부에 노출**
- Interactive 기능은 **세션 기반**
- 상태 조회는 **Resource 중심**
- 계산 엔진은 기존 Core Service를 그대로 재사용

---

## 9. 한 줄 결론

**MCP Tool은 앱 내부 Core 기능을 얇게 감싸는 명령 인터페이스이며, 전극 부착처럼 상호작용이 필요한 기능은 Interactive Session Tool로, 현재 상태 조회는 Resource로 분리하는 것이 가장 올바른 설계다.**
