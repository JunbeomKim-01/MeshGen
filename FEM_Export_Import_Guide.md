# FEM Export Import Guide

이 문서는 MeshGenLite에서 출력한 FEM package를 외부 C# 데스크탑 응용 프로그램이 읽어서 전기장 FEM 및 온열 FEM 계산으로 연결하는 방법을 설명한다.

## 1. Export 파일 구성

`Export FEM Package` 실행 시 다음 파일들이 생성된다.

```text
case_name.vtu
case_name.nodes.csv
case_name.elements.csv
case_name.boundary_faces.csv
case_name.materials.json
case_name.manifest.json
```

각 파일의 역할은 다음과 같다.

| 파일 | 역할 |
|---|---|
| `.vtu` | VTK Unstructured Grid. ParaView/VTK 검증용 또는 VTK 기반 import용 |
| `.nodes.csv` | node 좌표 |
| `.elements.csv` | tetrahedron connectivity + element region/material label |
| `.boundary_faces.csv` | 외부 표면 및 전극 표면 boundary face marker |
| `.materials.json` | region/material/boundary marker 의미와 물성값 |
| `.manifest.json` | package 파일 목록과 count metadata |

외부 FEM 계산 프로그램에서는 `.vtu`만 읽어도 되지만, C# solver 구현에는 CSV/JSON 세트를 읽는 방식이 더 단순하다.

권장 import 순서:

```text
manifest.json
 -> nodes.csv
 -> elements.csv
 -> boundary_faces.csv
 -> materials.json
 -> internal FEM mesh 구성
 -> electric field FEM / thermal FEM assembly
```

## 2. 기본 개념

MeshGenLite FEM package는 다음 네 가지를 전달한다.

```text
Geometry Mesh
+ Element Region Label
+ Boundary Face Marker
+ Region Material Table
```

### Geometry Mesh

node 좌표와 tetrahedron 연결 정보다.

### Element Region Label

각 tetrahedron이 어느 region에 속하는지 나타낸다.

예:

```text
region_id = 1       body/external tissue
region_id = 100100  electrode 1, layer 0
region_id = 100101  electrode 1, layer 1
```

### Boundary Face Marker

경계 조건을 걸어야 하는 triangle face의 marker다.

예:

```text
boundary_marker = 1       external outer surface
boundary_marker = 200001  electrode 1 outer surface
boundary_marker = 300001  electrode 1 contact surface
```

### Region Material Table

`region_id` 또는 `material_id`가 어떤 전기/열 물성을 갖는지 정의한다.

전기장 FEM:

- conductivity
- permittivity

온열 FEM:

- thermalConductivity
- density
- specificHeat

## 3. nodes.csv 읽기

형식:

```csv
node_id,x,y,z
0,12.3,45.6,78.9
1,12.5,45.8,79.0
```

C# 내부 구조 예:

```csharp
public readonly record struct FemNode(
    int Id,
    double X,
    double Y,
    double Z);
```

간단한 reader 예:

```csharp
static List<FemNode> ReadNodes(string path)
{
    return File.ReadLines(path)
        .Skip(1)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line =>
        {
            var p = line.Split(',');
            return new FemNode(
                int.Parse(p[0], CultureInfo.InvariantCulture),
                double.Parse(p[1], CultureInfo.InvariantCulture),
                double.Parse(p[2], CultureInfo.InvariantCulture),
                double.Parse(p[3], CultureInfo.InvariantCulture));
        })
        .ToList();
}
```

## 4. elements.csv 읽기

형식:

```csv
element_id,node0,node1,node2,node3,region_id,material_id
0,10,12,15,18,1,1
1,20,21,22,23,100100,100100
```

C# 내부 구조 예:

```csharp
public readonly record struct TetElement(
    int Id,
    int Node0,
    int Node1,
    int Node2,
    int Node3,
    int RegionId,
    int MaterialId);
```

reader 예:

```csharp
static List<TetElement> ReadElements(string path)
{
    return File.ReadLines(path)
        .Skip(1)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line =>
        {
            var p = line.Split(',');
            return new TetElement(
                int.Parse(p[0], CultureInfo.InvariantCulture),
                int.Parse(p[1], CultureInfo.InvariantCulture),
                int.Parse(p[2], CultureInfo.InvariantCulture),
                int.Parse(p[3], CultureInfo.InvariantCulture),
                int.Parse(p[4], CultureInfo.InvariantCulture),
                int.Parse(p[5], CultureInfo.InvariantCulture),
                int.Parse(p[6], CultureInfo.InvariantCulture));
        })
        .ToList();
}
```

FEM assembly에서는 각 tetrahedron의 `MaterialId` 또는 `RegionId`로 물성을 찾는다.

```csharp
var material = materialByRegion[element.RegionId];
```

## 5. boundary_faces.csv 읽기

형식:

```csv
face_id,node0,node1,node2,boundary_marker,owner_region_id
0,10,12,15,1,1
1,20,21,22,300001,100100
```

C# 내부 구조 예:

```csharp
public readonly record struct BoundaryFace(
    int Id,
    int Node0,
    int Node1,
    int Node2,
    int BoundaryMarker,
    int OwnerRegionId);
```

reader 예:

```csharp
static List<BoundaryFace> ReadBoundaryFaces(string path)
{
    return File.ReadLines(path)
        .Skip(1)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line =>
        {
            var p = line.Split(',');
            return new BoundaryFace(
                int.Parse(p[0], CultureInfo.InvariantCulture),
                int.Parse(p[1], CultureInfo.InvariantCulture),
                int.Parse(p[2], CultureInfo.InvariantCulture),
                int.Parse(p[3], CultureInfo.InvariantCulture),
                int.Parse(p[4], CultureInfo.InvariantCulture),
                int.Parse(p[5], CultureInfo.InvariantCulture));
        })
        .ToList();
}
```

boundary marker는 FEM boundary condition에 사용한다.

예:

```csharp
foreach (var face in boundaryFaces)
{
    if (face.BoundaryMarker == 300001)
    {
        // electrode 1 contact face
        // voltage, current injection, heat flux 등 적용
    }
}
```

## 6. materials.json 읽기

형식 예:

```json
{
  "units": {
    "length": "mm",
    "electricalConductivity": "S/m",
    "relativePermittivity": "dimensionless",
    "thermalConductivity": "W/(m*K)",
    "density": "kg/m^3",
    "specificHeat": "J/(kg*K)"
  },
  "regions": [
    {
      "regionId": 1,
      "materialId": 1,
      "name": "External",
      "sourceType": "ExternalBody",
      "conductivity": 0.2,
      "permittivity": 100000.0,
      "thermalConductivity": 0.5,
      "density": 1000.0,
      "specificHeat": 3600.0
    }
  ],
  "boundaries": [
    {
      "boundaryMarker": 1,
      "name": "External Outer Surface",
      "sourceType": "ExternalBody",
      "ownerRegionId": 1
    }
  ]
}
```

C# model 예:

```csharp
public sealed class MaterialTable
{
    public MaterialUnits Units { get; set; } = new();
    public List<RegionMaterial> Regions { get; set; } = new();
    public List<BoundaryDefinition> Boundaries { get; set; } = new();
}

public sealed class MaterialUnits
{
    public string Length { get; set; } = "";
    public string ElectricalConductivity { get; set; } = "";
    public string RelativePermittivity { get; set; } = "";
    public string ThermalConductivity { get; set; } = "";
    public string Density { get; set; } = "";
    public string SpecificHeat { get; set; } = "";
}

public sealed class RegionMaterial
{
    public int RegionId { get; set; }
    public int MaterialId { get; set; }
    public string Name { get; set; } = "";
    public string SourceType { get; set; } = "";
    public double Conductivity { get; set; }
    public double Permittivity { get; set; }
    public double ThermalConductivity { get; set; }
    public double Density { get; set; }
    public double SpecificHeat { get; set; }
}

public sealed class BoundaryDefinition
{
    public int BoundaryMarker { get; set; }
    public string Name { get; set; } = "";
    public string SourceType { get; set; } = "";
    public int OwnerRegionId { get; set; }
}
```

reader 예:

```csharp
static MaterialTable ReadMaterials(string path)
{
    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<MaterialTable>(
        json,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new MaterialTable();
}
```

lookup 구성:

```csharp
var materialByRegion = materialTable.Regions
    .ToDictionary(region => region.RegionId);

var boundaryByMarker = materialTable.Boundaries
    .ToDictionary(boundary => boundary.BoundaryMarker);
```

## 7. manifest.json 읽기

`manifest.json`은 package의 진입점으로 사용할 수 있다.

형식 예:

```json
{
  "format": "MeshGenLite FEM package",
  "version": 1,
  "files": {
    "geometryMesh": "case.vtu",
    "nodes": "case.nodes.csv",
    "elements": "case.elements.csv",
    "boundaryFaces": "case.boundary_faces.csv",
    "materialTable": "case.materials.json"
  }
}
```

권장 방식:

```csharp
var manifest = ReadManifest("case.manifest.json");
var baseDir = Path.GetDirectoryName(manifestPath)!;

var nodes = ReadNodes(Path.Combine(baseDir, manifest.Files.Nodes));
var elements = ReadElements(Path.Combine(baseDir, manifest.Files.Elements));
var boundaryFaces = ReadBoundaryFaces(Path.Combine(baseDir, manifest.Files.BoundaryFaces));
var materials = ReadMaterials(Path.Combine(baseDir, manifest.Files.MaterialTable));
```

## 8. 내부 FEM Mesh 구성

권장 내부 구조:

```csharp
public sealed class FemMesh
{
    public List<FemNode> Nodes { get; init; } = new();
    public List<TetElement> Elements { get; init; } = new();
    public List<BoundaryFace> BoundaryFaces { get; init; } = new();
    public MaterialTable Materials { get; init; } = new();
}
```

import 후 반드시 검증한다.

```csharp
static void ValidateFemMesh(FemMesh mesh)
{
    var nodeCount = mesh.Nodes.Count;
    var regionIds = mesh.Materials.Regions.Select(r => r.RegionId).ToHashSet();
    var boundaryMarkers = mesh.Materials.Boundaries.Select(b => b.BoundaryMarker).ToHashSet();

    foreach (var element in mesh.Elements)
    {
        if (element.Node0 < 0 || element.Node0 >= nodeCount ||
            element.Node1 < 0 || element.Node1 >= nodeCount ||
            element.Node2 < 0 || element.Node2 >= nodeCount ||
            element.Node3 < 0 || element.Node3 >= nodeCount)
        {
            throw new InvalidOperationException($"Element {element.Id} has invalid node index.");
        }

        if (!regionIds.Contains(element.RegionId))
        {
            throw new InvalidOperationException($"Element {element.Id} has unknown RegionId {element.RegionId}.");
        }
    }

    foreach (var face in mesh.BoundaryFaces)
    {
        if (!boundaryMarkers.Contains(face.BoundaryMarker))
        {
            throw new InvalidOperationException($"Boundary face {face.Id} has unknown marker {face.BoundaryMarker}.");
        }
    }
}
```

## 9. Electric Field FEM 연결

전기장 FEM의 일반적인 scalar potential 문제:

```text
div(sigma * grad(V)) = 0
```

element별 물성:

```csharp
foreach (var element in mesh.Elements)
{
    var material = materialByRegion[element.RegionId];
    var sigma = material.Conductivity;

    // element stiffness Ke = integral(B^T * sigma * B dV)
}
```

boundary condition:

```csharp
foreach (var face in mesh.BoundaryFaces)
{
    var boundary = boundaryByMarker[face.BoundaryMarker];

    if (boundary.SourceType == "ElectrodeContact")
    {
        // Dirichlet voltage, Neumann current, 또는 Robin contact impedance 적용
    }
}
```

전극 marker 예:

```text
200001 = electrode 1 outer surface
300001 = electrode 1 contact surface
```

## 10. Thermal FEM 연결

온열 FEM의 일반적인 transient heat equation:

```text
rho * c * dT/dt - div(k * grad(T)) = Q
```

element별 물성:

```csharp
foreach (var element in mesh.Elements)
{
    var material = materialByRegion[element.RegionId];

    var k = material.ThermalConductivity;
    var rho = material.Density;
    var cp = material.SpecificHeat;

    // thermal stiffness, mass matrix assembly
}
```

전기장 FEM 결과와 coupling:

```text
Q = sigma * |E|^2
```

C# 처리 개념:

```csharp
var sigma = material.Conductivity;
var jouleHeat = sigma * electricFieldNormSquared;
```

boundary face에는 convection, fixed temperature, heat flux 등을 적용할 수 있다.

```csharp
if (boundary.SourceType == "ExternalBody")
{
    // convection to air
}
else if (boundary.SourceType == "ElectrodeContact")
{
    // electrode heating/contact flux
}
```

## 11. .vtu를 직접 읽는 경우

`.vtu`는 VTK XML UnstructuredGrid 포맷이다.

C#에서 읽는 방법:

1. Kitware.VTK 사용
2. 직접 XML 파싱
3. ActiViz/VTK wrapper 사용

VTK wrapper 사용 예:

```csharp
var reader = vtkXMLUnstructuredGridReader.New();
reader.SetFileName("case.vtu");
reader.Update();

var grid = reader.GetOutput();
var regionArray = grid.GetCellData().GetArray("RegionId");
var materialArray = grid.GetCellData().GetArray("MaterialId");
```

하지만 solver input으로는 CSV/JSON 방식이 더 명확하다.

## 12. Import 시 주의사항

### 단위

Mesh 좌표는 mm 기준이다.

많은 FEM solver는 SI 단위 m를 사용하므로 필요하면 좌표를 변환해야 한다.

```csharp
var xMeters = node.X * 1e-3;
```

물성값은 `materials.json`의 units를 기준으로 해석한다.

### Region ID와 Material ID

현재 구조에서는 대체로 `RegionId`와 `MaterialId`가 같을 수 있다. 그러나 항상 같다고 가정하지 말고 material table을 lookup해야 한다.

권장:

```csharp
var material = materialByRegion[element.RegionId];
```

### Boundary Marker

boundary marker는 boundary condition을 찾는 key다.

```csharp
var boundary = boundaryByMarker[face.BoundaryMarker];
```

### .vtu와 CSV 중복

`.vtu`와 CSV/JSON은 같은 정보를 다른 방식으로 담는다.

- 시각화/검증: `.vtu`
- solver import: `.nodes.csv`, `.elements.csv`, `.boundary_faces.csv`, `.materials.json`

## 13. 최소 Import Flow

외부 C# FEM 프로그램에서 최소한 다음 순서를 따르면 된다.

```text
1. manifest.json 읽기
2. nodes.csv 읽기
3. elements.csv 읽기
4. boundary_faces.csv 읽기
5. materials.json 읽기
6. node index / region id / boundary marker 검증
7. element별 material property lookup
8. boundary marker별 boundary condition 지정
9. electric FEM assembly/solve
10. Joule heating 계산
11. thermal FEM assembly/solve
```

이 구조를 따르면 MeshGenLite 출력물을 외부 C# 데스크탑 FEM 프로그램에서 안정적으로 import할 수 있다.

