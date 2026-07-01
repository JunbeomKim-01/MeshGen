# Electrode Factory Technical Specification

## 1. 목적

이 문서는 별도 프로그램 **Electrode Factory**와 MeshGen의 **Electrode Preset Import** 기능을 구현하기 위한 기술 명세서다.

Electrode Factory는 사용자가 커스텀 전극 geometry, layer, material, contact face, local coordinate system을 정의하고, MeshGen이 읽을 수 있는 preset package를 출력한다.

MeshGen은 해당 preset을 import하여 body surface에 배치하고, attachment/deformation 및 FEM export까지 동일한 metadata 계약으로 처리한다.

## 2. 시스템 구성

```text
ElectrodeFactoryApp
  -> electrode preset package (.egpreset)

MeshGenLite
  -> preset import
  -> placement/attachment
  -> FEM export

External FEM App
  -> MeshGen FEM package import
  -> electric field FEM
  -> thermal FEM
```

## 3. 프로젝트 구성

권장 solution 구조:

```text
MeshGenLite.sln
  MeshGenLite/
  MeshGenLite.Tests/
  ElectrodeFactoryApp/
  ElectrodeFactory.Core/
  ElectrodeFactory.Tests/
```

### ElectrodeFactory.Core

UI와 분리된 전극 생성/검증/패키징 로직.

```text
ElectrodeFactory.Core/
  Models/
  Geometry/
  Materials/
  Validation/
  Export/
  Import/
```

### ElectrodeFactoryApp

WPF 또는 WinUI 기반 데스크탑 UI.

```text
ElectrodeFactoryApp/
  Views/
  ViewModels/
  Services/
  Resources/
```

### MeshGenLite

기존 앱에 preset import 및 placement 기능 추가.

```text
MeshGenLite/
  Models/ElectrodePresetModels.cs
  Services/ElectrodePresetRepository.cs
  Services/ElectrodePresetMeshLoader.cs
  Services/ElectrodePresetPackageService.cs
```

## 4. Package Format

Electrode Factory 출력물은 `.egpreset` package다.

`.egpreset`는 zip 기반 컨테이너다.

내부 구조:

```text
custom-electrode.egpreset
  preset.json
  mesh.vtp
  mesh.stl
  thumbnail.png
  validation.json
```

### 필수 파일

| 파일 | 필수 | 설명 |
|---|---:|---|
| `preset.json` | yes | preset metadata |
| `mesh.vtp` | yes | cell data 포함 가능한 primary mesh |
| `mesh.stl` | no | fallback geometry-only mesh |
| `thumbnail.png` | no | UI preview |
| `validation.json` | yes | export 당시 validation result |

MeshGen은 `mesh.vtp`를 우선 사용한다. `mesh.vtp`가 없으면 `.stl` fallback을 사용할 수 있으나, 이 경우 `LayerIndex`와 `ContactFace`를 별도 방식으로 복원해야 하므로 권장하지 않는다.

## 5. preset.json Schema

### Root

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
  "localCoordinateSystem": {},
  "placement": {},
  "layers": [],
  "cellData": {},
  "validation": {}
}
```

### localCoordinateSystem

```json
{
  "origin": [0.0, 0.0, 0.0],
  "upAxis": [0.0, 0.0, 1.0],
  "contactPlaneNormal": [0.0, 0.0, -1.0]
}
```

규칙:

- `origin`: MeshGen placement 기준점
- `upAxis`: 전극이 body surface에서 바깥쪽으로 올라가는 방향
- `contactPlaneNormal`: contact face의 외부 방향. 보통 `-upAxis`

### placement

```json
{
  "defaultClearanceMm": 0.5,
  "footprintRadiusMm": 10.0,
  "allowRoll": true,
  "allowScale": false,
  "defaultRollDegrees": 0.0
}
```

### layers

```json
{
  "layerId": 0,
  "name": "Hydrogel",
  "regionRole": "ContactMedium",
  "conductivity": 0.1,
  "permittivity": 10000.0,
  "thermalConductivity": 0.5,
  "density": 1000.0,
  "specificHeat": 3600.0
}
```

필수 물성:

- `conductivity`
- `permittivity`
- `thermalConductivity`
- `density`
- `specificHeat`

### cellData

```json
{
  "layerArray": "LayerIndex",
  "contactArray": "ContactFace",
  "boundaryArray": "BoundaryMarker"
}
```

## 6. Mesh Cell Data Contract

Electrode Factory가 출력하는 `mesh.vtp`는 cell data를 포함해야 한다.

### Required Cell Arrays

| 이름 | 타입 | 필수 | 설명 |
|---|---|---:|---|
| `LayerIndex` | int | yes | cell이 속한 layer |
| `ContactFace` | int | yes | contact face 여부. 1이면 contact |

### Optional Cell Arrays

| 이름 | 타입 | 설명 |
|---|---|---|
| `BoundaryMarker` | int | factory 단계 boundary role |
| `PartId` | int | multi-part electrode 구분 |
| `FaceRole` | int | side/top/contact/interface role |

MeshGen import 후 runtime cell arrays:

| 이름 | 설명 |
|---|---|
| `RegionId` | placement ID + layer index 기반 |
| `MaterialId` | material table lookup ID |
| `BoundaryMarker` | placement ID 기반 contact/outer marker |

## 7. ID 규칙

MeshGen runtime ID 규칙은 기존 FEM export와 동일해야 한다.

```csharp
ElectrodeLayerRegion(placementId, layerIndex)
  = 100000 + placementId * 100 + layerIndex

ElectrodeOuterBoundary(placementId)
  = 200000 + placementId

ElectrodeContactBoundary(placementId)
  = 300000 + placementId
```

예:

```text
placementId = 1
layer 0 region = 100100
layer 1 region = 100101
outer boundary = 200001
contact boundary = 300001
```

## 8. ElectrodeFactory.Core Models

### ElectrodeProject

```csharp
public sealed class ElectrodeProject
{
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "mm";
    public ElectrodeLocalCoordinateSystem CoordinateSystem { get; set; } = new();
    public List<ElectrodePart> Parts { get; set; } = new();
    public List<ElectrodeLayerDefinition> Layers { get; set; } = new();
}
```

### ElectrodePart

```csharp
public sealed class ElectrodePart
{
    public int PartId { get; set; }
    public string Name { get; set; } = "";
    public int LayerId { get; set; }
    public MeshGeometry Mesh { get; set; } = new();
}
```

### MeshGeometry

```csharp
public sealed class MeshGeometry
{
    public List<Vector3> Points { get; } = new();
    public List<TriangleCell> Triangles { get; } = new();
    public Dictionary<string, List<int>> CellIntData { get; } = new();
}
```

### TriangleCell

```csharp
public readonly record struct TriangleCell(int A, int B, int C);
```

## 9. Geometry Pipeline

### Parametric Builder

입력:

- shape type
- dimensions
- layer id

출력:

- triangulated `MeshGeometry`
- `LayerIndex`
- default `ContactFace`

지원 primitive:

```text
Cylinder
Box
Ring
Needle
ExtrudedPolygon
RoundedPad
```

### Mesh Import

지원 포맷:

```text
STL
OBJ
PLY
VTP
VTK
```

import 후 처리:

```text
triangulate
merge duplicate points
remove degenerate triangles
compute normals
assign layer/contact metadata
```

## 10. Validation

### Geometry Validation

```text
point count > 0
triangle count > 0
no degenerate triangles
no invalid indices
no duplicated triangles
normal orientation check
optional closed surface check
```

### Metadata Validation

```text
LayerIndex exists
every LayerIndex exists in layers[]
ContactFace exists
at least one ContactFace == 1
local origin is finite
upAxis is non-zero
material values are finite and valid
```

### FEM Validation

```text
at least one conductive layer
thermalConductivity > 0
density > 0
specificHeat > 0
conductivity >= 0
permittivity > 0
```

Validation result:

```json
{
  "isValid": true,
  "errors": [],
  "warnings": [],
  "meshStats": {
    "pointCount": 1200,
    "triangleCount": 2400
  }
}
```

## 11. Export Service

### IElectrodePresetExporter

```csharp
public interface IElectrodePresetExporter
{
    Task<ElectrodePresetExportResult> ExportAsync(
        ElectrodeProject project,
        string outputPath,
        CancellationToken cancellationToken);
}
```

Export steps:

```text
validate project
generate final mesh
write mesh.vtp
write fallback mesh.stl
write preset.json
write validation.json
generate thumbnail.png
zip to .egpreset
```

## 12. MeshGen Import Services

### ElectrodePresetRepository

```csharp
public interface IElectrodePresetRepository
{
    IReadOnlyList<ElectrodePreset> Presets { get; }
    Task LoadAsync(CancellationToken cancellationToken);
    Task<ElectrodePreset> ImportAsync(string packagePath, CancellationToken cancellationToken);
    Task DeleteAsync(string presetId, CancellationToken cancellationToken);
}
```

### ElectrodePresetMeshLoader

```csharp
public interface IElectrodePresetMeshLoader
{
    vtkPolyData LoadMesh(ElectrodePreset preset);
}
```

Responsibilities:

- load `mesh.vtp`
- fallback load `mesh.stl`
- triangulate
- validate required cell arrays
- normalize local coordinate system if needed

### ElectrodePresetPlacementService

```csharp
public interface IElectrodePresetPlacementService
{
    vtkPolyData BuildWorldMesh(
        ElectrodePreset preset,
        Point3D surfacePoint,
        Vector3D surfaceNormal,
        double clearance,
        double rollDegrees);
}
```

## 13. MeshGen UI Integration

ViewModel additions:

```csharp
public enum ElectrodeSourceMode
{
    BuiltIn,
    Preset
}

public sealed class ElectrodePanelViewModel
{
    public ElectrodeSourceMode SourceMode { get; set; }
    public ObservableCollection<ElectrodePreset> Presets { get; }
    public ElectrodePreset? SelectedPreset { get; set; }
    public ICommand ImportPresetCommand { get; }
    public ICommand ReloadPresetsCommand { get; }
}
```

Placement logic:

```text
if SourceMode == BuiltIn:
    use existing ElectrodeFactory
else:
    use ElectrodePresetPlacementService
```

## 14. FEM Metadata Integration

`FemMetadataService.TagElectrodeSurface()` 확장:

```text
if ContactFace array exists:
    ContactFace == 1 -> ElectrodeContactBoundary(placementId)
else:
    fallback to geometry-based contact detection
```

Layer:

```text
LayerIndex -> ElectrodeLayerRegion(placementId, layerIndex)
```

Material:

```text
preset.layers[layerIndex] -> material table row
```

## 15. Backward Compatibility

기존 built-in 전극은 계속 지원한다.

```text
Built-in Circle/Rectangle/Triangle
Custom Preset
```

기존 `ElectrodeSpec`는 built-in 전극용으로 유지한다.

Preset 전극은 `ElectrodePreset` 기반으로 관리한다.

## 16. Error Handling

### Electrode Factory

Export 불가 조건:

- geometry 없음
- layer 없음
- `LayerIndex` 누락
- `ContactFace` 누락
- material invalid
- local coordinate invalid

### MeshGen

Import 불가 조건:

- unsupported schema
- package 손상
- mesh file 없음
- required cell array 없음
- layer/material 불일치

Placement 불가 조건:

- preset mesh load 실패
- local coordinate invalid
- body surface pick 실패

## 17. Testing

### Unit Tests

```text
preset.json parse
package unzip/import
LayerIndex validation
ContactFace validation
runtime RegionId assignment
runtime BoundaryMarker assignment
material table generation
```

### Integration Tests

```text
Electrode Factory sample preset export
MeshGen preset import
custom electrode placement
attachment
FEM package export
materials.json contains preset layers
elements.csv contains custom electrode region ids
boundary_faces.csv contains contact marker
```

## 18. Performance

Preset mesh load는 cache한다.

권장:

```text
preset mesh raw cache
transformed preview mesh cache
thumbnail cache
validation result cache
```

고해상도 preset의 경우:

- preview mesh는 decimated version 사용
- FEM mesh는 full resolution 사용

## 19. Security

`.egpreset`는 zip이므로 import 시 zip-slip 방어가 필요하다.

금지:

```text
../
absolute path
drive root path
symlink extraction
```

package extract는 항상 preset library 내부 sandbox 폴더에서 수행한다.

## 20. Implementation Priority

1. MeshGen preset data model
2. preset package import
3. `.vtp` mesh loader
4. preset dropdown UI
5. preset placement transform
6. runtime FEM metadata tagging
7. FEM export material integration
8. Electrode Factory primitive builder
9. Electrode Factory validation
10. `.egpreset` export

