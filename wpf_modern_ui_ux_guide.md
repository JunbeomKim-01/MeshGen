# Modern WPF UI/UX Design Guide for MeshGen

본 문서는 MeshGen 프로젝트의 WPF 전환 시, 사용자에게 "프리미엄 전문가용 툴"의 느낌을 줄 수 있는 현대적인 UX/UI 디자인 가이드라인입니다.

## 1. 핵심 디자인 컨셉
- **Dark Elegance**: 기본 테마를 Deep Dark로 설정하여 3D 메쉬의 색상과 가독성을 극대화합니다.
- **Glassmorphism (Mica/Acrylic)**: 배경에 반투명 효과를 적용하여 윈도우의 깊이감을 부여합니다.
- **Fluent Design System**: Windows 11의 디자인 언어를 따르며, 부드러운 모서리(Rounded Corners)와 세련된 그림자를 사용합니다.

## 2. 레이아웃 구조 (Layout Strategy)

### A. 사이드바 내비게이션 (Workflow-based)
- **방식**: 좌측 슬림 사이드바에 파이프라인 단계를 아이콘으로 배치.
- **UX**: 현재 진행 중인 단계를 하이라이트하고, 완료된 단계는 체크 표시로 시각화하여 전체 워크플로우 진행 상태를 한눈에 파악.

### B. 메인 워크스페이스 (3D Viewport Focus)
- **방식**: 중앙의 가장 큰 영역을 VTK 뷰포트가 차지하게 함.
- **UI**: 뷰포트는 "Borderless" 느낌으로 배치하고, UI 컨트롤들은 뷰포트 위에 떠 있는(Floating) 카드 형태로 배치하여 몰입감을 높임.

### C. 속성 패널 (Contextual Side Panel)
- **방식**: 우측에 현재 단계(예: 전극 스펙 설정)에 필요한 파라미터 조절 창 배치.
- **UI**: 슬라이더, 토글 스위치 등을 사용하여 직관적인 조작 지원.

## 3. 추천 디자인 라이브러리
- **WPF-UI**: Windows 11의 기본 컨트롤들을 WPF에서 완벽하게 구현한 라이브러리. (강력 추천)
- **MaterialDesignInXaml**: 구글의 머티리얼 디자인 시스템을 WPF에 이식.
- **MahApps.Metro**: 현대적인 윈도우 스타일과 다양한 컨트롤 모음.

## 4. 시각적 디테일 (Visual Excellence)

### A. 컬러 팔레트 (Professional Palette)
- **Background**: `#0F0F0F` (Main), `#1A1A1A` (Panel)
- **Accent**: `#0078D4` (Microsoft Blue) 또는 `#60CDFF` (Cyan)
- **Success/Warning**: `#22C55E` (Emerald), `#EAB308` (Amber)

### B. 타이포그래피 (Modern Fonts)
- **Primary**: `Inter`, `Segoe UI Variable`
- **Header**: `Outfit` 또는 `Montserrat` (전문적인 느낌 강조)

### C. 마이크로 인터렉션 (Micro-animations)
- **Hover**: 버튼이나 카드 위로 마우스를 올릴 때 미세한 크기 변화나 글로우(Glow) 효과.
- **Loading**: 파이프라인 연산 중(Step 6, 7)에는 Lottie 애니메이션을 사용하여 "지루하지 않은 기다림" 제공.

## 5. 3D 뷰포트 통합 가이드
- **Seamless Integration**: XAML 경계선이 느껴지지 않도록 뷰포트 주변에 미세한 Gradient Inner Shadow를 적용.
- **HUD (Heads-Up Display)**: 뷰포트 내 상단에 "현재 메쉬 정보 (Vertices, Triangles 수)"를 작고 투명하게 표시.
- **Gizmo**: 카메라 방향을 나타내는 3D 좌표축(Gizmo)을 우측 하단에 세련된 디자인으로 배치.

## 6. 에이전트 개발 시 고려사항 (UI Implementation)
- **Responsive**: 창 크기 조절 시 3D 뷰포트가 실시간으로 좌표를 추적하여 빈틈없이 채워지도록 구현.
- **MVVM Pattern**: UI 디자인과 비즈니스 로직을 완벽히 분리하여 스타일 변경이 로직에 영향을 주지 않게 설계.
