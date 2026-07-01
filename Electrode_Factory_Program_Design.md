# Electrode Factory Program Design

## 1. 목적

현재 MeshGen은 원형, 사각형, 삼각형 같은 간단한 전극 도형을 내부 `ElectrodeFactory`에서 생성한다.

향후에는 별도 프로그램인 **Electrode Factory**를 만들어 사용자가 전극 형상, 레이어, 물성, 접촉면, 기준축을 커스텀할 수 있게 한다.

Electrode Factory의 산출물을 MeshGen이 **Electrode Preset**으로 import하고, 사용자는 기존 전극 배치/부착 workflow에서 커스텀 전극을 선택해 body surface에 부착할 수 있어야 한다.

최종 목표:

```text
Electrode Factory
 -> custom electrode mesh package export
 -> MeshGen Electrode Preset import
 -> surface placement
 -> attachment/deformation
 -> FEM export
```

## 2. 전체 구조

```text
[Electrode Factory]
  - 전극 geometry 편집
  - layer/material 설정
  - contact face 지정
  - local coordinate system 지정
  - preview/validation
  - electrode preset package export

[MeshGen]
  - electrode preset import
  - preset library 관리
  - 커스텀 전극 placement preview
  - body surface에 부착
  - FEM metadata 유지
  - FEM package export
```

## 3. 핵심 요구사항

### Electrode Factory 요구사항

- 사용자가 전극 mesh를 직접 만들거나 외부 mesh를 import할 수 있어야 한다.
- 전극 layer를 정의할 수 있어야 한다.
- 각 layer에 물성을 지정할 수 있어야 한다.
- 전극의 contact face를 명시할 수 있어야 한다.
- 전극의 local coordinate system을 저장해야 한다.
- MeshGen이 전극을 body surface normal에 맞춰 배치할 수 있도록 anchor 정보를 제공해야 한다.
- FEM export에서 region/material/boundary marker가 유지될 수 있도록 metadata를 포함해야 한다.

### MeshGen 요구사항

- Electrode Factory 산출물을 preset으로 import한다.
- imported preset을 기존 기본 도형 전극과 동일하게 선택할 수 있다.
- imported preset으로 placement preview를 생성한다.
- body surface normal 기준으로 전극 pose를 계산한다.
- attachment/deformation 후에도 layer/material/contact marker를 유지한다.
- FEM package export 시 커스텀 전극의 material table과 boundary marker가 포함된다.

## 4. 용어 정의

### Electrode Preset

MeshGen에서 재사용 가능한 전극 정의 단위.

포함 정보:

- mesh geometry
- layer 정보
- material 정보
- contact face marker
- local coordinate system
- preview thumbnail
- validation result

### Local Coordinate System

전극 mesh가 MeshGen에서 배치될 때 사용하는 기준 좌표계.

권장 규칙:

```text
local +Z = 전극이 body surface에서 바깥쪽으로 솟는 방향
local Z = 0 plane = skin/contact 기준면
local origin = contact footprint 중심
```

MeshGen placement 시:

```text
local +Z -> picked body surface normal
local origin -> picked surface point + clearance
```

### Contact Face

body surface 또는 gel/skin과 닿는 전극 하단 face.

FEM에서는 전극 접촉 boundary condition에 사용된다.

예:

```text
BoundaryMarker = ElectrodeContactBoundary(placementId)
```

### Layer

전극을 구성하는 재료 영역.

예:

```text
Hydrogel
Ceramic
Metal
Insulator
```

각 layer는 FEM region/material로 변환된다.

## 5. Electrode Factory 기능 설계

### 5.1 프로젝트 생성

사용자는 새 electrode project를 만든다.

입력:

- electrode name
- author
- description
- unit
- default scale

예:

```json
{
  "name": "Custom Ring Electrode",
  "unit": "mm",
  "description": "Ring electrode with hydrogel base and metal ring"
}
```

### 5.2 Geometry 생성 방식

Electrode Factory는 세 가지 geometry 생성 방식을 지원한다.

#### A. Parametric Builder

기본 primitive 조합:

- cylinder
- box
- sphere cap
- ring
- cone
- extruded polygon
- rounded pad
- needle

사용자는 치수와 layer를 지정한다.

#### B. Mesh Import

외부 mesh import:

- `.stl`
- `.obj`
- `.ply`
- `.vtk`
- `.vtu`

import 후 Electrode Factory에서 다음을 지정한다.

- scale
- local origin
- local +Z direction
- contact face
- layer/material region

#### C. Script / Template

고급 사용자를 위한 scripted electrode 생성.

예:

```text
ring(radiusOuter, radiusInner, thickness)
pad(width, depth, thickness, fillet)
needle(radius, length, bevel)
```

## 6. Electrode Factory UI 설계

### 6.1 주요 화면

```text
Left panel:
  - electrode project tree
  - layer list
  - mesh parts

Center:
  - 3D viewport
  - axis gizmo
  - contact face highlight

Right panel:
  - selected part properties
  - material properties
  - FEM metadata
  - validation results

Bottom:
  - timeline/log
  - export button
```

### 6.2 필수 UI 기능

- layer 추가/삭제/순서 변경
- material property 편집
- face selection mode
- contact face 지정
- outer face 자동 감지
- local origin 지정
- local +Z axis 지정
- mesh repair/triangulate
- validation 실행
- package export

## 7. Electrode Preset Package 포맷

Electrode Factory는 하나의 폴더 또는 zip package를 출력한다.

권장 확장자:

```text
.egpreset
```

내부 구조:

```text
custom_electrode.egpreset/
  preset.json
  mesh.vtp
  mesh.stl
  materials.json
  thumbnail.png
  validation.json
```

zip 형태로 배포할 경우:

```text
custom_electrode.egpreset
```

실제로는 zip 파일이며 MeshGen에서 압축 해제 후 읽는다.

## 8. preset.json 스키마

```json
{
  "schemaVersion": 1,
  "presetId": "custom-ring-electrode",
  "name": "Custom Ring Electrode",
  "description": "Hydrogel base with metal ring",
  "unit": "mm",
  "meshFile": "mesh.vtp",
  "fallbackMeshFile": "mesh.stl",
  "thumbnailFile": "thumbnail.png",
  "localCoordinateSystem": {
    "origin": [0.0, 0.0, 0.0],
    "upAxis": [0.0, 0.0, 1.0],
    "contactPlaneNormal": [0.0, 0.0, -1.0]
  },
  "placement": {
    "defaultClearanceMm": 0.5,
    "footprintRadiusMm": 10.0,
    "allowRoll": true,
    "allowScale": false
  },
  "layers": [
    {
      "layerId": 0,
      "name": "Hydrogel",
      "regionRole": "ContactMedium",
      "conductivity": 0.1,
      "permittivity": 10000.0,
      "thermalConductivity": 0.5,
      "density": 1000.0,
      "specificHeat": 3600.0
    },
    {
      "layerId": 1,
      "name": "Metal",
      "regionRole": "Conductor",
      "conductivity": 59600000.0,
      "permittivity": 1.0,
      "thermalConductivity": 15.0,
      "density": 1900.0,
      "specificHeat": 800.0
    }
  ],
  "cellData": {
    "layerArray": "LayerIndex",
    "contactArray": "ContactFace",
    "boundaryArray": "BoundaryMarker"
  },
  "validation": {
    "requiresClosedSurface": true,
    "requiresTriangulatedMesh": true,
    "requiresContactFace": true
  }
}
```

## 9. Mesh 파일 metadata 계약

Electrode Factory에서 출력하는 mesh는 cell data를 포함해야 한다.

필수 cell data:

| 이름 | 타입 | 설명 |
|---|---|---|
| `LayerIndex` | int | cell이 속한 electrode layer |
| `ContactFace` | int | contact face 여부. 1 = contact |
| `BoundaryMarker` | int | optional. Factory 단계에서 지정한 boundary role |

MeshGen import 후 placement ID가 정해지면 다음 cell data를 재계산한다.

| 이름 | 설명 |
|---|---|
| `RegionId` | placement ID + layer index 기반 region ID |
| `MaterialId` | region/material table lookup ID |
| `BoundaryMarker` | placement ID 기반 contact/outer boundary marker |

즉 Electrode Factory는 template metadata를 제공하고, MeshGen은 실제 placement ID를 반영한 runtime metadata를 만든다.

## 10. MeshGen Electrode Preset 기능

### 10.1 Preset Library

MeshGen에 preset library 폴더를 둔다.

예:

```text
MeshGenData/
  ElectrodePresets/
    custom-ring-electrode/
      preset.json
      mesh.vtp
      materials.json
      thumbnail.png
```

기능:

- preset import
- preset delete
- preset reload
- preset validation
- preset thumbnail 표시

### 10.2 UI 변경

현재 electrode panel에 다음 기능 추가.

```text
Electrode Source
  - Built-in
  - Preset

Built-in Shape
  - Circle
  - Rectangle
  - Triangle

Preset
  - Custom Ring Electrode
  - Needle Array
  - Imported Pad

Buttons
  - Import Preset
  - Reload Presets
  - Open Preset Folder
```

사용자는 preset을 선택한 뒤 기존과 동일하게 body surface를 클릭해 배치한다.

## 11. MeshGen 내부 모델 추가

### ElectrodePreset

```csharp
public sealed class ElectrodePreset
{
    public string PresetId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Unit { get; init; } = "mm";
    public string MeshPath { get; init; } = string.Empty;
    public string? ThumbnailPath { get; init; }
    public ElectrodeLocalCoordinateSystem LocalCoordinateSystem { get; init; } = new();
    public ElectrodePlacementDefaults Placement { get; init; } = new();
    public List<ElectrodePresetLayer> Layers { get; init; } = new();
}
```

### ElectrodePresetLayer

```csharp
public sealed class ElectrodePresetLayer
{
    public int LayerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string RegionRole { get; init; } = string.Empty;
    public double Conductivity { get; init; }
    public double Permittivity { get; init; }
    public double ThermalConductivity { get; init; }
    public double Density { get; init; }
    public double SpecificHeat { get; init; }
}
```

### ElectrodePresetRepository

역할:

- preset folder scan
- `.egpreset` import/unzip
- `preset.json` parse
- mesh file path resolve
- duplicate preset ID 처리

### ElectrodePresetMeshLoader

역할:

- `.vtp`, `.vtk`, `.stl`, `.obj` load
- triangulate
- validate cell data
- local coordinate transform normalize

## 12. MeshGen placement 흐름 변경

현재:

```text
ElectrodeFactory.Build(spec)
 -> transformBuilder.BuildTransform(surfacePoint, normal, clearance)
 -> AddCommittedElectrode
```

변경:

```text
if BuiltIn:
    ElectrodeFactory.Build(spec)
else Preset:
    ElectrodePresetMeshLoader.Load(preset)

Normalize local coordinate system
Apply placement transform
Assign placement ID
Tag runtime FEM metadata
AddCommittedElectrode
```

## 13. Preset placement transform

Electrode Factory에서 저장한 local coordinate system:

```text
local origin
local upAxis
contactPlaneNormal
```

MeshGen에서 surface pick:

```text
surface point P
surface normal N
clearance C
```

배치:

```text
Rotate local upAxis to N
Translate local origin to P + N * C
Optional roll around N
```

기존 `ElectrodeTransformBuilder`를 확장한다.

```csharp
vtkTransform BuildPresetTransform(
    ElectrodePreset preset,
    Point3D surfacePoint,
    Vector3D surfaceNormal,
    double clearance,
    double rollDegrees);
```

## 14. Runtime FEM metadata 부여

MeshGen에서 preset 전극이 placement ID를 받으면 다음을 수행한다.

```text
for each cell:
    layerIndex = cell.LayerIndex
    RegionId = ElectrodeLayerRegion(placementId, layerIndex)
    MaterialId = RegionId

    if ContactFace == 1:
        BoundaryMarker = ElectrodeContactBoundary(placementId)
    else:
        BoundaryMarker = ElectrodeOuterBoundary(placementId)
```

기존 `FemMetadataService.TagElectrodeSurface()`를 preset mesh에도 사용하되, `ContactFace` array를 우선 사용하도록 확장한다.

## 15. FEM export 연결

MeshGen FEM export는 preset 전극을 built-in 전극과 동일하게 처리해야 한다.

필수:

- `RegionId`
- `MaterialId`
- `BoundaryMarker`
- material table row

Preset material table 생성 규칙:

```text
for each placement:
    for each preset layer:
        regionId = ElectrodeLayerRegion(placementId, layerId)
        material row = preset layer material
```

즉 같은 preset을 여러 번 배치하면 placement마다 region ID는 다르지만 물성값은 동일할 수 있다.

## 16. Electrode Factory validation

Electrode Factory export 전에 반드시 검사한다.

### Geometry 검사

- mesh point count > 0
- triangle cell count > 0
- non-manifold edge 검사
- duplicate face 검사
- degenerate triangle 검사
- normal orientation 검사

### Metadata 검사

- 모든 cell에 `LayerIndex` 있음
- `LayerIndex`가 preset layer 목록에 존재
- contact face가 하나 이상 있음
- contact face가 local Z=0 근처에 있음
- local upAxis가 zero vector가 아님
- material 값이 유효함

### FEM 검사

- conductive layer가 하나 이상 있음
- contact medium layer가 하나 이상 있거나 명시적으로 없음 허용
- thermal properties가 모두 양수

## 17. MeshGen preset import validation

MeshGen import 시에도 다시 검사한다.

- `preset.json` schemaVersion 지원 여부
- mesh file 존재 여부
- mesh load 가능 여부
- mesh unit이 mm인지 확인
- cell data `LayerIndex` 존재 여부
- layer index와 material table 일치 여부
- contact face marker 존재 여부

실패 시 import를 막고 사용자에게 validation report를 보여준다.

## 18. 추천 구현 단계

### Phase 1 - MeshGen Preset Import MVP

- `ElectrodePreset` 모델 추가
- preset folder scan
- `preset.json` parse
- `.vtp` 또는 `.stl` mesh load
- preset dropdown UI
- preset mesh placement
- FEM metadata tagging

### Phase 2 - Electrode Factory MVP

- WPF 또는 WinUI desktop app
- primitive 기반 전극 생성
- layer/material editing
- contact face 자동 지정
- preset package export

### Phase 3 - Advanced Factory

- external mesh import
- face selection
- local coordinate editor
- validation report
- thumbnail generation
- zip `.egpreset` export

### Phase 4 - FEM 강화

- preset layer별 closed volume 검증
- layer interface marker
- anisotropic material support
- contact impedance parameter
- thermal contact resistance parameter

## 19. 코드 변경 대상

### MeshGen

추가:

```text
Models/ElectrodePresetModels.cs
Services/ElectrodePresetRepository.cs
Services/ElectrodePresetMeshLoader.cs
Services/ElectrodePresetPackageService.cs
```

수정:

```text
Models/ElectrodeModels.cs
Services/ElectrodeFactory.cs
Services/ElectrodePlacementController.cs
Services/ElectrodeTransformBuilder.cs
Services/FemMetadataService.cs
Services/FemExportService.cs
ViewModels/ShellViewModel.cs
MainWindow.xaml
```

### Electrode Factory

신규 프로젝트:

```text
ElectrodeFactoryApp/
  Models/
  Services/
  ViewModels/
  Views/
  Export/
```

## 20. 예시 workflow

```text
1. Electrode Factory 실행
2. New Electrode Project
3. Ring electrode 생성
4. Hydrogel / Metal layer 추가
5. contact face 지정
6. material property 입력
7. validation 통과
8. Export custom-ring-electrode.egpreset
9. MeshGen 실행
10. Import Electrode Preset
11. preset dropdown에서 Custom Ring Electrode 선택
12. body surface 클릭
13. custom electrode preview/placement
14. Attach Electrodes
15. Export FEM Package
16. 외부 FEM 프로그램에서 import
```

## 21. 중요한 설계 원칙

1. Electrode Factory는 template을 만든다.
2. MeshGen은 placement ID를 기준으로 runtime region/boundary ID를 확정한다.
3. 전극 mesh에는 최소한 `LayerIndex`와 `ContactFace`가 있어야 한다.
4. material 값은 preset package에 포함되어야 한다.
5. MeshGen FEM export는 built-in 전극과 preset 전극을 동일한 방식으로 처리해야 한다.
6. `.vtu`는 검증/시각화용으로 유용하지만 solver import 계약은 `nodes/elements/boundary/materials`가 기준이다.

