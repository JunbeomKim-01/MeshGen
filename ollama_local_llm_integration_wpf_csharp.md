# WPF/C# 프로젝트용 로컬 LLM(Ollama) 적용 설계서

## 1. 목적

이 문서는 **WPF + C# 기반 의료 3D 애플리케이션**에 **로컬 LLM(Ollama)** 을 통합하기 위한 실무 지침서이다.

대상 기능은 다음과 같다.

- 로컬 LLM 설치 안내
- 설치 여부 확인
- 서버 실행 여부 확인
- 모델 설치 여부 확인
- 모델 연결/응답 확인
- 현재 프로젝트의 DICOM / NIfTI / Mesh / Electrode 상태를 바탕으로 답변하는 채팅 기능
- 향후 tool-calling 기반의 의료 객체 질의 확장

---

## 2. 왜 이런 구조가 좋은가

의료 포맷(DICOM, RTSS, NIfTI)은 형식이 엄격하고 좌표계/메타데이터가 중요하다. 따라서 LLM에게 원본 파일을 직접 해석하게 하기보다, **애플리케이션이 먼저 의료 데이터를 구조화**하고, LLM은 그 구조화된 상태를 기반으로 설명/요약/질의응답을 수행하도록 만드는 것이 좋다.

권장 구조:

1. **의료 데이터 해석 계층**
   - DICOM, RTSS, NIfTI 읽기
   - Mesh, Electrode, Contact, Distance, Bounds 등 계산
2. **도메인 상태 요약 계층**
   - `SceneState`, `StudyState`, `SelectedObjectState`, `ElectrodeState` 같은 DTO 구성
3. **LLM 오케스트레이션 계층**
   - Ollama API 호출
   - Tool 호출 루프 처리
4. **WPF UI 계층**
   - 채팅창
   - 설치 상태 표시
   - 연결 테스트
   - 모델 선택

즉, **LLM은 파일 파서가 아니라 설명자 + 질의 라우터**로 사용한다.

---

## 3. 권장 전체 아키텍처

```text
[WPF UI]
  ├─ ChatView
  ├─ SettingsView
  ├─ OllamaStatusView
  └─ ViewModels
          ↓
[Application Services]
  ├─ OllamaClient
  ├─ OllamaStatusService
  ├─ ChatOrchestrator
  ├─ ToolRegistry
  └─ SceneContextBuilder
          ↓
[Domain Services]
  ├─ DicomReader
  ├─ NiftiReader
  ├─ MeshService
  ├─ ElectrodeService
  └─ ContactAnalysisService
          ↓
[Ollama Local Server]
  └─ http://localhost:11434/api
```

---

## 4. Ollama 도입 범위

### 4.1 Ollama가 담당할 것

- 사용자 질문 해석
- 현재 상태 요약 기반 설명
- Mesh / Electrode / 구조물 정보 질의응답
- Tool-calling 기반 함수 선택
- 사용자 친화적 응답 생성

### 4.2 Ollama가 직접 담당하면 안 되는 것

- DICOM / RTSS / NIfTI 원본 해석의 최종 판단
- 좌표계/spacing/origin/direction 계산
- Mesh 면적/체적/거리 계산
- 전극 접촉 판정
- 임상적 사실의 추측

이 값들은 반드시 **애플리케이션 코드가 계산한 결과**를 사용한다.

---

## 5. Windows 로컬 설치 가이드

### 5.1 기본 전제

- 대상 OS: Windows 10 22H2 이상
- Ollama는 Windows에서 백그라운드로 실행되며 기본 API 주소는 `http://localhost:11434/api`
- 설치 후 터미널에서 `ollama` 명령을 사용할 수 있어야 한다

### 5.2 설치 절차

1. 공식 사이트에서 Windows용 Ollama 설치 프로그램 다운로드
2. 설치 진행
3. 설치 후 PowerShell 또는 CMD 실행
4. 아래 명령으로 CLI 동작 확인

```powershell
ollama --version
```

5. 첫 모델 다운로드 예시

```powershell
ollama pull gemma3
```

6. 모델 실행 테스트

```powershell
ollama run gemma3
```

또는 API 호출 테스트:

```powershell
curl http://localhost:11434/api/version
```

### 5.3 프로젝트 초반 추천 운영 방식

초기 개발 단계에서는 모델 종류를 너무 많이 늘리지 말고 아래처럼 단순화하는 것이 좋다.

- 기본 대화 모델 1개
- 필요 시 tool-calling 실험용 모델 1개
- 필요 시 vision 모델 1개

초기에는 예시 모델 하나만 기준으로 시작하고, 앱 내부 설정에서 모델 이름을 바꾸는 구조가 가장 좋다.

---

## 6. 앱 안에서 필요한 상태 판정

앱은 Ollama 상태를 단순히 "된다/안 된다"로 보지 말고 아래처럼 나누는 것이 좋다.

```text
NotInstalled
InstalledButUnknown
CliAvailable
ServerReachable
NoModelInstalled
ModelInstalled
ModelLoaded
Ready
Error
```

### 6.1 상태 정의

#### NotInstalled
- `ollama` 명령을 찾을 수 없음
- API 포트도 응답 없음

#### CliAvailable
- `ollama --version` 성공
- 하지만 서버는 아직 확인 안 됨

#### ServerReachable
- `GET /api/version` 성공
- 로컬 서버 응답 가능

#### NoModelInstalled
- `/api/tags` 성공
- 설치된 모델 리스트가 비어 있음

#### ModelInstalled
- `/api/tags` 에 하나 이상 모델 존재

#### ModelLoaded
- `/api/ps` 에 현재 메모리에 올라간 모델 존재

#### Ready
- 짧은 `POST /api/chat` 또는 `POST /api/generate` 테스트 성공

---

## 7. 설치 확인 / 실행 확인 / 연결 확인 규칙

### 7.1 설치 확인

#### 방법 A: CLI 확인

```powershell
ollama --version
```

성공하면 최소한 설치 및 PATH 등록 가능성이 높다.

#### 방법 B: 프로세스/포트 확인

- `localhost:11434` 응답 여부 확인
- 단, CLI가 없어도 서버만 떠 있는 경우가 있을 수 있으므로 보조 수단으로 사용

### 7.2 서버 상태 확인

```http
GET http://localhost:11434/api/version
```

성공 시 예시:

```json
{
  "version": "0.x.x"
}
```

### 7.3 설치된 모델 확인

```http
GET http://localhost:11434/api/tags
```

성공 시 예시:

```json
{
  "models": [
    {
      "name": "gemma3"
    }
  ]
}
```

### 7.4 현재 메모리에 올라간 모델 확인

```http
GET http://localhost:11434/api/ps
```

### 7.5 실제 연결/응답 확인

```http
POST http://localhost:11434/api/chat
Content-Type: application/json

{
  "model": "gemma3",
  "stream": false,
  "messages": [
    { "role": "user", "content": "Say OK" }
  ]
}
```

이 요청이 성공하고 응답 메시지가 반환되면, 앱 입장에서 **실제 채팅 연결 확인 성공**으로 본다.

---

## 8. WPF에서 필요한 화면 구성

### 8.1 설정 화면

필수 항목:

- Ollama 설치 안내 버튼
- 설치 확인 버튼
- 서버 상태 확인 버튼
- 모델 목록 새로고침 버튼
- 기본 모델 선택 ComboBox
- 연결 테스트 버튼
- 최근 오류 로그 표시 영역

### 8.2 상태 표시 예시

- 설치 상태: 설치됨 / 미설치
- 서버 상태: 연결 가능 / 연결 불가
- 모델 상태: 없음 / 설치됨 / 로드됨
- 마지막 테스트: 성공 / 실패
- 응답 시간: `xxx ms`

### 8.3 추천 UX

- 빨강: 서버 연결 실패
- 노랑: 서버는 됨, 모델 없음
- 초록: 모델 연결 테스트 성공
- 회색: 아직 검사 안 함

---

## 9. WPF/C# 코드 구조 제안

### 9.1 핵심 클래스

```text
Services/
  OllamaClient.cs
  OllamaStatusService.cs
  OllamaInstallGuideService.cs
  ChatOrchestrator.cs
  ToolRegistry.cs

Models/
  OllamaStatus.cs
  OllamaModelInfo.cs
  OllamaHealthReport.cs
  ChatMessage.cs
  ChatRequestContext.cs

ViewModels/
  SettingsViewModel.cs
  ChatViewModel.cs
  OllamaStatusViewModel.cs
```

---

## 10. 상태 모델 예시

```csharp
public enum OllamaConnectionState
{
    Unknown,
    NotInstalled,
    CliAvailable,
    ServerReachable,
    NoModelInstalled,
    ModelInstalled,
    ModelLoaded,
    Ready,
    Error
}

public sealed class OllamaHealthReport
{
    public OllamaConnectionState State { get; set; }
    public bool IsCliAvailable { get; set; }
    public bool IsServerReachable { get; set; }
    public bool HasInstalledModels { get; set; }
    public bool HasLoadedModels { get; set; }
    public bool CanGenerateResponse { get; set; }
    public string? Version { get; set; }
    public string? SelectedModel { get; set; }
    public string? Message { get; set; }
    public TimeSpan? Latency { get; set; }
    public List<string> InstalledModels { get; set; } = new();
    public List<string> LoadedModels { get; set; } = new();
}
```

---

## 11. HttpClient 기반 OllamaClient 예시

```csharp
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class OllamaClient
{
    private readonly HttpClient _http;

    public OllamaClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("http://localhost:11434/api/");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("version", ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.GetProperty("version").GetString();
    }

    public async Task<List<string>> GetInstalledModelsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("tags", ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<string>();

        if (doc.RootElement.TryGetProperty("models", out var models))
        {
            foreach (var model in models.EnumerateArray())
            {
                if (model.TryGetProperty("name", out var name))
                    result.Add(name.GetString() ?? string.Empty);
            }
        }

        return result.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    public async Task<List<string>> GetLoadedModelsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("ps", ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<string>();

        if (doc.RootElement.TryGetProperty("models", out var models))
        {
            foreach (var model in models.EnumerateArray())
            {
                if (model.TryGetProperty("name", out var name))
                    result.Add(name.GetString() ?? string.Empty);
            }
        }

        return result.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    public async Task<(bool success, string? responseText, TimeSpan latency, string? error)> TestChatAsync(
        string model,
        CancellationToken ct = default)
    {
        var body = new
        {
            model,
            stream = false,
            messages = new[]
            {
                new { role = "user", content = "Say OK" }
            }
        };

        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _http.PostAsJsonAsync("chat", body, ct);
            sw.Stop();
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            string? text = null;
            if (doc.RootElement.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                text = content.GetString();
            }

            return (true, text, sw.Elapsed, null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, null, sw.Elapsed, ex.Message);
        }
    }
}
```

---

## 12. 설치 확인용 CLI 실행 예시

CLI 존재 여부는 `ollama --version`으로 확인할 수 있다.

```csharp
using System.Diagnostics;

public static class OllamaCliProbe
{
    public static async Task<(bool success, string output)> CheckAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            return (process.ExitCode == 0, output.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
```

---

## 13. 종합 상태 점검 서비스 예시

```csharp
public sealed class OllamaStatusService
{
    private readonly OllamaClient _client;

    public OllamaStatusService(OllamaClient client)
    {
        _client = client;
    }

    public async Task<OllamaHealthReport> CheckAsync(string? preferredModel = null, CancellationToken ct = default)
    {
        var report = new OllamaHealthReport();

        var cli = await OllamaCliProbe.CheckAsync();
        report.IsCliAvailable = cli.success;

        if (!cli.success)
        {
            report.State = OllamaConnectionState.NotInstalled;
            report.Message = cli.output;
        }
        else
        {
            report.State = OllamaConnectionState.CliAvailable;
        }

        try
        {
            report.Version = await _client.GetVersionAsync(ct);
            report.IsServerReachable = true;
            report.State = OllamaConnectionState.ServerReachable;
        }
        catch (Exception ex)
        {
            report.IsServerReachable = false;
            report.Message = ex.Message;
            report.State = report.IsCliAvailable
                ? OllamaConnectionState.CliAvailable
                : OllamaConnectionState.NotInstalled;
            return report;
        }

        report.InstalledModels = await _client.GetInstalledModelsAsync(ct);
        report.HasInstalledModels = report.InstalledModels.Count > 0;

        if (!report.HasInstalledModels)
        {
            report.State = OllamaConnectionState.NoModelInstalled;
            report.Message = "서버는 연결되지만 설치된 모델이 없습니다.";
            return report;
        }

        report.State = OllamaConnectionState.ModelInstalled;

        report.LoadedModels = await _client.GetLoadedModelsAsync(ct);
        report.HasLoadedModels = report.LoadedModels.Count > 0;

        if (report.HasLoadedModels)
            report.State = OllamaConnectionState.ModelLoaded;

        var testModel = !string.IsNullOrWhiteSpace(preferredModel)
            ? preferredModel
            : report.InstalledModels.First();

        report.SelectedModel = testModel;

        var test = await _client.TestChatAsync(testModel, ct);
        report.CanGenerateResponse = test.success;
        report.Latency = test.latency;

        if (test.success)
        {
            report.State = OllamaConnectionState.Ready;
            report.Message = $"연결 성공: {test.responseText}";
        }
        else
        {
            report.State = OllamaConnectionState.Error;
            report.Message = test.error;
        }

        return report;
    }
}
```

---

## 14. 모델이 없을 때의 UX

앱은 `NoModelInstalled` 상태에서 사용자에게 아래 가이드를 보여주는 것이 좋다.

### PowerShell 예시

```powershell
ollama pull gemma3
```

모델 설치 후 다시 검사:

```powershell
curl http://localhost:11434/api/tags
```

또는 앱의 **[모델 목록 새로고침]** 버튼으로 재검사한다.

---

## 15. 연결 확인 버튼의 권장 동작

"연결 확인" 버튼은 아래 순서로 동작하는 것이 좋다.

1. CLI 확인
2. `/api/version` 확인
3. `/api/tags` 확인
4. 기본 모델 선택 확인
5. `POST /api/chat` 테스트
6. 최종 상태 갱신

실패 시 어느 단계에서 실패했는지를 분명히 보여준다.

예시 메시지:

- 설치되지 않음
- 설치는 됨, 서버 미응답
- 서버 응답 가능, 모델 없음
- 모델 있음, 채팅 테스트 실패
- 모든 검사 성공

---

## 16. 채팅 기능 적용 방식

### 16.1 잘못된 방식

- DICOM 파일 전체를 프롬프트에 그대로 넣기
- NIfTI 바이너리를 직접 모델에 넣기
- Mesh 수치 계산을 모델에게 맡기기

### 16.2 권장 방식

앱이 현재 상태를 JSON으로 요약해서 LLM에 전달한다.

예시:

```json
{
  "study": {
    "patientId": "local-session",
    "modality": ["CT", "RTSS", "NIFTI_SEG"]
  },
  "scene": {
    "selectedObject": "liver_mesh",
    "visibleMeshes": ["body", "liver", "electrode_A"],
    "cameraMode": "3D"
  },
  "mesh": {
    "name": "liver_mesh",
    "vertexCount": 152344,
    "triangleCount": 304680,
    "bounds": [12.1, 144.4, -75.3, 98.1, 22.0, 186.9]
  },
  "electrode": {
    "name": "electrode_A",
    "distanceToLiverMeanMm": 1.8,
    "isContacting": false
  }
}
```

이렇게 하면 모델이 설명은 잘하되, 계산값은 앱이 책임질 수 있다.

---

## 17. Tool-calling 확장 설계

Ollama는 tool calling을 지원하므로, 이후 아래 함수들을 도구로 공개할 수 있다.

### 17.1 추천 도구 목록

- `GetCurrentSceneSummary()`
- `ListStructures()`
- `GetSelectedMeshInfo()`
- `GetMeshInfo(name)`
- `GetElectrodeInfo(name)`
- `GetElectrodeContactReport(electrodeName, organName)`
- `GetDistanceBetweenObjects(a, b)`
- `CaptureViewportSnapshot()`
- `SearchProjectDocs(query)`

### 17.2 도구 호출 루프 원칙

1. 사용자 질문 수신
2. 모델에 메시지 + 도구 스키마 전달
3. 모델이 도구 요청 시 C# 함수 실행
4. 함수 결과를 다시 모델에 전달
5. 최종 자연어 응답 생성

---

## 18. ChatOrchestrator 설계 제안

```csharp
public sealed class ChatOrchestrator
{
    private readonly OllamaClient _ollamaClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly SceneContextBuilder _sceneContextBuilder;

    public ChatOrchestrator(
        OllamaClient ollamaClient,
        ToolRegistry toolRegistry,
        SceneContextBuilder sceneContextBuilder)
    {
        _ollamaClient = ollamaClient;
        _toolRegistry = toolRegistry;
        _sceneContextBuilder = sceneContextBuilder;
    }

    public async Task<string> AskAsync(string userMessage, string model)
    {
        // 1. 현재 scene/state 요약 생성
        // 2. 메시지 구성
        // 3. 필요 시 tool schema 포함
        // 4. Ollama /api/chat 호출
        // 5. tool call이 있으면 앱 함수 실행
        // 6. 결과 반영 후 최종 응답 반환
        return "TODO";
    }
}
```

---

## 19. 설정 저장 권장 항목

```json
{
  "ollama": {
    "baseUrl": "http://localhost:11434/api/",
    "defaultModel": "gemma3",
    "connectionTimeoutSeconds": 10,
    "enableToolCalling": true,
    "enableVision": false,
    "autoHealthCheckOnStartup": true
  }
}
```

---

## 20. 앱 시작 시 권장 동작

앱 시작 후:

1. 저장된 기본 모델 확인
2. 자동 상태 검사 실행
3. 상태 배지 갱신
4. 모델 없으면 설치 안내 패널 표시
5. 연결 테스트 성공 시 채팅 입력 활성화

---

## 21. 오류 처리 기준

### 21.1 설치 안 됨
- 사용자가 설치 안내 페이지를 열 수 있게 한다
- PATH 인식 문제 가능성도 함께 안내

### 21.2 서버 미실행
- Ollama 앱이 백그라운드에서 실행 중인지 확인하도록 안내
- 필요 시 사용자가 직접 `ollama` 명령 실행

### 21.3 모델 없음
- `ollama pull <model>` 안내
- 앱에서 원하는 모델명을 복사할 수 있게 제공

### 21.4 모델 응답 실패
- 모델명 오타
- 메모리 부족
- 초기 로딩 지연
- 타임아웃 초과

위 가능성을 메시지로 분리해서 보여주는 것이 좋다.

---

## 22. 보안/개인정보 기본 원칙

- 기본값은 **로컬 전용**
- 외부 API 전송 없음
- DICOM/NIfTI 원본 전체를 프롬프트에 보내지 않음
- 필요한 경우에만 구조화된 요약값 전달
- 로그에는 민감한 환자 식별자 저장 금지

---

## 23. 1차 구현 우선순위

### Phase 1
- Ollama 설치 안내 화면
- 설치/서버/모델/연결 확인 기능
- 단일 모델 채팅

### Phase 2
- SceneState 요약 포함 채팅
- 선택 객체 정보 기반 응답
- 모델 설정 저장

### Phase 3
- Tool calling
- Mesh/Electrode 질의 자동 함수 호출
- 문서 검색(RAG)

### Phase 4
- Vision 입력
- 뷰포트 캡처 기반 설명
- 고급 agent loop

---

## 24. 최소 완료 기준(MVP)

다음이 되면 1차 완료로 본다.

- 사용자가 Ollama 설치 여부를 앱에서 확인할 수 있다
- 로컬 서버 상태를 앱에서 확인할 수 있다
- 설치된 모델 목록을 앱에서 조회할 수 있다
- 기본 모델로 짧은 채팅 테스트를 할 수 있다
- 현재 mesh/electrode 상태 요약을 넣어 질문할 수 있다

---

## 25. 권장 체크리스트

### 개발 체크리스트

- [ ] `ollama --version` 확인 구현
- [ ] `/api/version` 확인 구현
- [ ] `/api/tags` 확인 구현
- [ ] `/api/ps` 확인 구현
- [ ] `/api/chat` 테스트 구현
- [ ] WPF 상태 표시 UI 구현
- [ ] 기본 모델 설정 저장
- [ ] 오류 메시지 분기 구현
- [ ] SceneState 요약기 구현
- [ ] ChatOrchestrator 구현

---

## 26. 결론

이 프로젝트에서 로컬 LLM은 **의료 파일 파서**가 아니라 **의료/3D 엔진의 설명 인터페이스**로 쓰는 것이 가장 적합하다.

초기 구현은 반드시 아래 순서로 진행한다.

1. Ollama 설치 안내
2. 설치/서버/모델/연결 상태 확인
3. 단일 모델 채팅 연결
4. SceneState 기반 질의응답
5. Tool-calling 확장

이 순서를 따르면 WPF/C# 애플리케이션 안에서 안정적으로 로컬 LLM 기능을 도입할 수 있다.

---

## 27. 공식 참고 링크

- Ollama Windows 설치 문서
- Ollama API Introduction
- Ollama Get Version API
- Ollama List Models API
- Ollama List Running Models API
- Ollama Chat API
- Ollama Tool Calling

