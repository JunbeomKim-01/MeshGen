# Electrode Factory UX/UI Design

## 1. UX 목표

Electrode Factory는 사용자가 FEM-ready 커스텀 전극을 만들 수 있게 하는 데스크탑 도구다.

사용자는 복잡한 mesh/FEM metadata를 직접 이해하지 않아도 다음 작업을 자연스럽게 수행할 수 있어야 한다.

```text
전극 형상 만들기
 -> layer/material 지정
 -> contact face 확인
 -> local axis 확인
 -> validation 통과
 -> MeshGen preset으로 export
```

UX 핵심:

- 전극의 실제 3D 형상을 항상 중심에 둔다.
- layer/material/contact 상태를 색으로 즉시 확인한다.
- FEM export에 필요한 metadata 누락을 화면에서 바로 알려준다.
- MeshGen에서 어떻게 배치될지 미리 볼 수 있어야 한다.

## 2. 대상 사용자

### Primary User

전극 설계자 또는 연구자.

필요:

- 전극 형상 커스텀
- 전극 물성 지정
- 전극 접촉면 지정
- MeshGen에서 반복 사용 가능한 preset 생성

### Secondary User

FEM 해석 담당자.

필요:

- region/material/boundary marker가 정확한지 확인
- 전기장/온열 FEM에 필요한 물성 검증
- export package의 신뢰성 확인

## 3. 전체 UX Flow

```text
Start
 -> New Electrode / Open Project
 -> Geometry 생성 또는 Import
 -> Layer 지정
 -> Material 지정
 -> Contact Face 지정
 -> Local Coordinate 확인
 -> Validate
 -> Preview in MeshGen Pose
 -> Export .egpreset
```

## 4. App Shell Layout

권장 레이아웃:

```text
┌────────────────────────────────────────────────────────────┐
│ Top Command Bar                                             │
├───────────────┬───────────────────────────────┬────────────┤
│ Left Panel    │ 3D Viewport                   │ Right Panel │
│ Project Tree  │ Electrode Mesh                │ Properties  │
│ Layers        │ Contact/Layer Overlay         │ Materials   │
│ Parts         │ Axis Gizmo                    │ Validation  │
├───────────────┴───────────────────────────────┴────────────┤
│ Bottom Status / Validation / Export Log                     │
└────────────────────────────────────────────────────────────┘
```

## 5. Top Command Bar

### 주요 버튼

| 버튼 | 설명 |
|---|---|
| New | 새 전극 프로젝트 |
| Open | 기존 project 열기 |
| Import Mesh | STL/OBJ/PLY/VTP import |
| Save | project 저장 |
| Validate | FEM preset validation |
| Export Preset | `.egpreset` 출력 |

### 상태 표시

우측에 validation 상태 badge 표시:

```text
Ready
Needs Contact Face
Material Missing
Invalid Mesh
Exportable
```

색상:

- green: export 가능
- yellow: warning 있음
- red: error 있음
- gray: 아직 검사 전

## 6. Left Panel

### 6.1 Project Tree

```text
Custom Ring Electrode
  Geometry
    Hydrogel Base
    Metal Ring
  Layers
    Hydrogel
    Metal
  Markers
    Contact Face
    Outer Surface
```

사용자는 tree에서 part/layer/marker를 선택한다.

### 6.2 Layer List

각 layer item:

```text
[color swatch] Hydrogel
Conductivity: 0.1 S/m
Thickness: 1.0 mm
Cells: 1,024
```

기능:

- add layer
- duplicate layer
- delete layer
- reorder
- assign selected faces to layer

### 6.3 Part List

Geometry part 단위 관리:

```text
Hydrogel Base
Metal Ring
Insulator Cap
```

각 part는 visibility toggle을 가진다.

## 7. 3D Viewport

3D viewport는 앱의 핵심 화면이다.

### 표시 요소

- electrode mesh
- layer color overlay
- selected face highlight
- contact face highlight
- local origin marker
- local +Z arrow
- contact plane
- bounding box
- scale grid

### View Modes

| Mode | 설명 |
|---|---|
| Solid | 기본 mesh 표시 |
| Layer | layer별 색상 표시 |
| Contact | contact face만 강조 |
| Boundary | boundary role 표시 |
| Wireframe | mesh quality 확인 |
| Mesh Quality | degenerate/non-manifold 표시 |

### Interaction

| 조작 | 기능 |
|---|---|
| Left drag | orbit |
| Right drag | pan |
| Wheel | zoom |
| Click face | select face |
| Shift click | add selection |
| Ctrl click | remove selection |
| Double click part | select connected part |

## 8. Right Panel

Right panel은 선택한 항목에 따라 내용이 바뀐다.

### 8.1 Project Properties

```text
Name
Description
Unit
Author
Preset ID
```

Preset ID는 MeshGen library에서 unique해야 한다.

### 8.2 Geometry Properties

Parametric part 선택 시:

```text
Shape Type
Radius
Width
Depth
Thickness
Segments
Layer
```

Import mesh 선택 시:

```text
Scale
Transform
Repair Mesh
Recompute Normals
Triangulate
```

### 8.3 Material Properties

Layer 선택 시:

```text
Layer Name
Region Role
Conductivity
Permittivity
Thermal Conductivity
Density
Specific Heat
Color
```

입력값 validation:

- conductivity >= 0
- permittivity > 0
- thermal conductivity > 0
- density > 0
- specific heat > 0

### 8.4 Contact Face Panel

```text
Contact Face Status
Selected Contact Cells
Auto Detect Contact Face
Assign Selection as Contact
Clear Contact
Show Only Contact
```

Contact face는 매우 중요하므로 별도 섹션으로 둔다.

### 8.5 Coordinate System Panel

```text
Origin
Up Axis
Contact Plane Normal
Reset to Bounds Center
Pick Origin from Mesh
Set +Z from View
Preview MeshGen Placement
```

시각화:

- local origin: small sphere
- local +Z: arrow
- contact plane: translucent plane

## 9. Bottom Panel

### Validation Results

```text
Errors
  - ContactFace array missing
  - LayerIndex 3 has no material

Warnings
  - Mesh is not closed
  - 24 degenerate triangles removed

Info
  - 3 layers
  - 12,420 triangles
```

각 항목 클릭 시 viewport에서 관련 geometry를 highlight한다.

### Export Log

```text
Validation passed
mesh.vtp written
preset.json written
thumbnail.png written
custom-ring-electrode.egpreset exported
```

## 10. Main Workflows

### 10.1 Parametric Electrode 만들기

```text
New
 -> Choose Template: Ring Electrode
 -> Set dimensions
 -> Add layers
 -> Set material properties
 -> Auto Detect Contact Face
 -> Validate
 -> Export Preset
```

화면은 template wizard 형태로 시작하고, 완료 후 full editor로 전환한다.

### 10.2 외부 Mesh Import

```text
Import Mesh
 -> select STL/OBJ/PLY/VTP
 -> choose unit/scale
 -> orient mesh
 -> set local origin
 -> assign layers
 -> select contact face
 -> validate
 -> export preset
```

외부 mesh import 후에는 사용자가 반드시 contact face와 layer를 지정해야 한다.

### 10.3 Contact Face 지정

권장 UX:

1. Contact mode 진입
2. 하단면 자동 후보 표시
3. 사용자가 확인 또는 수정
4. contact face가 cyan/green 계열로 표시
5. validation status 갱신

자동 감지 기준:

```text
local Z minimum 근처 face
normal이 -upAxis 방향인 face
footprint 중심 근처 connected component
```

### 10.4 Local Coordinate 설정

목표:

MeshGen에서 전극이 surface normal에 맞게 자연스럽게 배치되도록 한다.

UX:

```text
Set Origin
  - Bounds Center
  - Contact Face Center
  - Pick Point

Set Up Axis
  - +X / +Y / +Z
  - From Selected Face Normal
  - Flip Axis
```

Preview:

가상의 body surface plane 위에 전극이 어떻게 놓이는지 보여준다.

## 11. MeshGen Preset UI

MeshGen의 기존 electrode panel에 preset 기능을 추가한다.

### Electrode Source

```text
Source
[ Built-in | Preset ]
```

Built-in 선택:

```text
Shape
Layer editor
```

Preset 선택:

```text
Preset dropdown
Thumbnail
Description
Layer summary
Import Preset
Reload
Open Folder
```

### Preset Card

```text
┌──────────────────────┐
│ thumbnail            │
│ Custom Ring Electrode│
│ 2 layers · 10 mm     │
│ Hydrogel, Metal      │
└──────────────────────┘
```

선택된 preset은 placement preview에 바로 반영된다.

## 12. MeshGen Placement UX

기존 placement mode를 유지한다.

```text
Place Electrode
 -> hover body surface
 -> preset preview follows cursor
 -> left click commit
```

Preset일 때 추가 controls:

```text
Roll
Clearance
Scale if allowed
Flip Normal
```

Roll은 surface normal 주변 회전이다.

## 13. Visual Language

### Color

| 의미 | 색 |
|---|---|
| Hydrogel/contact medium | light blue |
| Metal/conductor | yellow/gold |
| Ceramic/insulator | light gray |
| Contact face | cyan/green highlight |
| Error faces | red |
| Warning faces | orange |
| Selected faces | white outline |

### Icons

권장 아이콘:

- import
- export
- validate/check
- warning
- error
- layer
- material
- contact
- axis
- eye visibility

## 14. Validation UX

Validation은 export 직전뿐 아니라 편집 중에도 incremental로 실행한다.

### 상태 단계

```text
Not Checked
Checking
Valid
Valid With Warnings
Invalid
```

### Error 표시 원칙

나쁜 예:

```text
Invalid mesh.
```

좋은 예:

```text
Contact face is missing. Select bottom faces and click "Assign as Contact".
```

가능하면 fix action을 같이 제공한다.

```text
[Auto Detect Contact Face]
[Select Problem Faces]
```

## 15. Export UX

Export button은 validation이 error-free일 때만 primary enabled.

Warnings가 있을 경우:

```text
This preset has warnings:
- Mesh is not closed.
- 12 skinny triangles detected.

Export anyway?
```

Export 결과:

```text
Export complete
custom-ring-electrode.egpreset

[Open Folder]
[Import into MeshGen]
```

MeshGen이 같은 PC에 있으면 바로 preset library로 복사하는 기능도 제공 가능하다.

## 16. Empty / Error States

### 새 프로젝트 초기 상태

중앙 viewport:

```text
Choose a template or import a mesh
```

버튼:

```text
[Ring Electrode]
[Pad Electrode]
[Needle]
[Import Mesh]
```

### Mesh import 실패

```text
The mesh could not be loaded.
Supported formats: STL, OBJ, PLY, VTP, VTK.
```

### Validation 실패

Validation panel에 실패 원인을 보여주고, export는 disable한다.

## 17. Accessibility

- 색상만으로 layer/contact/error를 구분하지 않는다.
- tooltip과 label을 같이 제공한다.
- keyboard shortcut 제공:
  - Ctrl+N New
  - Ctrl+O Open
  - Ctrl+S Save
  - Ctrl+I Import Mesh
  - Ctrl+E Export
  - Ctrl+R Validate
- viewport focus 상태에서 delete는 selected face/part 삭제 전 confirm 필요

## 18. UX 구현 우선순위

### MVP

1. New/Open/Save
2. primitive builder
3. layer/material editor
4. contact face auto detect
5. validation panel
6. export `.egpreset`
7. MeshGen preset dropdown/import

### V2

1. external mesh import
2. manual face selection
3. local coordinate editor
4. thumbnail generation
5. validation issue highlighting

### V3

1. scripted electrode templates
2. advanced mesh repair
3. layer interface marker editing
4. direct send to MeshGen
5. preset marketplace/library

