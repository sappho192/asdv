# .NET 기반 로컬 코딩 에이전트(Claude Code 스타일) 구현 계획서

## 1. 목표와 범위

### 목표
- 로컬 리포지토리를 대상으로 동작하는 **콘솔 기반 코딩 에이전트**를 .NET 8/9로 구현한다.
- OpenAI(Responses API 스타일) / Anthropic(Claude Messages API 스타일) **공급자 독립 구조**를 갖춘다.
- 에이전트의 핵심 루프는 “읽기 → 계획 → 패치 생성 → 적용 → 빌드/테스트 → 반복”을 수행한다.
- 위험 작업(명령 실행/대량 변경/네트워크 등)은 **정책 기반 승인(Approval)** 을 거친다.
- 모든 상호작용(모델 스트리밍/툴 호출/결과)을 **세션 로그(JSONL)** 로 저장하고 재현 가능하게 한다.

### 비목표(초기)
- VSCode 확장/웹 UI(daemon)는 초기 범위에서 제외(추후 확장 가능).
- 완전한 JSON Schema 검증(드래프트 전체 지원)은 인터페이스만 마련 후 라이브러리 도입으로 확장.
- 에이전트 멀티프로세스/분산 실행은 제외.

---

## 2. 핵심 설계 원칙

1) **공급자 독립(Provider-agnostic)**  
- Orchestrator는 공급자 API를 직접 알지 않고, 내부 표준 이벤트 스트림(`AgentEvent`)만 소비한다.  
- OpenAI/Claude 차이는 각 `IModelProvider` 어댑터에서만 흡수한다.

2) **상태 변화는 Tool 결과만 신뢰**  
- 모델의 텍스트(계획/설명)는 참고용.  
- 실제 파일 변경, 테스트 결과 등은 Tool 실행 결과로만 AgentState에 반영.

3) **패치 기반 변경(ApplyPatch) 중심**  
- 파일 전체 덮어쓰기보다 unified diff 패치 적용이 안정적이고 리뷰 가능.
- 적용 실패 시 “실패 hunk/컨텍스트”를 구조적으로 모델에 반환해 반복을 끊는다.

4) **안전(Policy)과 승인(Approval)을 1급 구성요소로**  
- `RunCommand`, 네트워크 접근, 파일 삭제/대량 변경 등은 기본 승인 필요.
- repo root 탈출(path traversal / symlink) 방지, stdout/stderr 제한, timeout 필수.

5) **재현성(Logging/Replay)**  
- 모델 스트리밍 이벤트, tool call 인자/결과, patch 적용, 실행 명령 등을 JSONL로 남긴다.
- 이후 동일 로그로 리플레이/디버깅 가능하게 한다.

---

## 3. 아키텍처 개요

### 3.1 모듈 구성(권장)

```
Agent.sln
src/
  Agent.Cli/ # 엔트리포인트, 옵션 파싱, UI(콘솔)
  Agent.Core/ # Orchestrator, AgentEvent, ModelRequest, Policy
  Agent.Llm.OpenAI/ # OpenAI Responses Provider
  Agent.Llm.Anthropic/ # Claude Messages Provider
  Agent.Tools/ # ReadFile/SearchText/ApplyPatch/RunCommand/Git/RunTests 등
  Agent.Workspace/ # repo root 제한, 파일 접근 안전성, 검색/인덱싱
  Agent.Logging/ # JSONL 세션 로그, 리플레이
tests/
  Agent.Core.Tests/
```


### 3.2 실행 흐름(한 Iteration)
1. (선택) GitStatus/GitDiff/작업 컨텍스트 갱신
2. `IModelProvider.StreamAsync(ModelRequest)` 호출 → 스트리밍 `AgentEvent` 수신
3. TextDelta는 즉시 콘솔 출력
4. ToolCallReady 수신 시:
   - 정책 엔진 확인 → 승인 필요하면 사용자 승인 요청
   - Tool 실행 → ToolResultEvent 생성
   - Tool 결과를 대화 메시지로 반영(공급자 중립)
5. 다음 iteration에서 모델 재호출
6. 종료 조건 판단(완료/테스트 통과/반복 감지/max iterations)

---

## 4. 내부 표준 이벤트 모델(AgentEvent)

### 목표
OpenAI(typed SSE events) / Claude(content blocks + deltas) 모두를 **하나의 표준 이벤트 스트림**으로 통일.

### 표준 이벤트 타입(최소)
- `TextDelta(text)`
- `ToolCallStarted(callId, toolName)`
- `ToolCallArgsDelta(callId, jsonFragment)`
- `ToolCallReady(callId, toolName, argsJson)`  
  - Orchestrator는 여기에서만 JSON 파싱/검증/실행
- `ToolResultEvent(callId, toolName, ToolResult)`
- `ResponseCompleted(stopReason, usage, providerMetadata)`
- `TraceEvent(kind, data)` (unknown 이벤트/디버깅)

---

## 5. 공급자(Provider) 어댑터 설계

### 5.1 공통 인터페이스
- `IModelProvider.StreamAsync(ModelRequest)` → `IAsyncEnumerable<AgentEvent>`
- Provider는 다음 책임을 가진다:
  - 공급자별 스트리밍(SSE) 파싱
  - 텍스트 델타 → `TextDelta`로 변환
  - 툴콜 입력(JSON) 누적/완성 신호 처리 → `ToolCallReady` 발생
  - unknown 이벤트는 `TraceEvent`로 남기고 죽지 않기

### 5.2 OpenAI (Responses API 스타일) 어댑터
- `POST /v1/responses` with `stream:true`
- SSE 이벤트 `type` 기반 라우팅:
  - 텍스트: `response.output_text.delta` → TextDelta
  - 툴 인자: `response.function_call_arguments.delta/done`
    - done에서 최종 `name + arguments`로 ToolCallReady
  - 완료: `response.completed` → ResponseCompleted
- 현재 단계:
  - 최소 동작: 내부 메시지들을 `instructions` + `input`으로 flatten 가능
  - 다음 개선: Responses “items” 기반으로 tool result를 function_call_output 형태로 정확 매핑

### 5.3 Claude (Messages API 스타일) 어댑터
- `POST /v1/messages` with `stream:true`
- SSE 흐름:
  - `message_start` → usage/model/id 확보
  - `content_block_start`에서 `tool_use` 감지 → ToolCallStarted
  - `content_block_delta`에서 `input_json_delta.partial_json` 누적 → ToolCallArgsDelta
  - `content_block_stop`에서 누적 JSON 파싱 → ToolCallReady
  - `message_stop`에서 stop_reason에 따라 ResponseCompleted
- Tool 결과 전달:
  - 다음 턴 요청에서 **user 메시지 content에 tool_result 블록**으로 전달
  - `tool_use_id`는 callId를 그대로 사용
- parallel tool use 대비:
  - 하나의 응답에서 여러 tool_use가 나올 수 있으므로, Orchestrator는 “다중 ToolCallReady 수집 후 실행”으로 확장 권장

---

## 6. Tool 시스템(실행기) 설계

### 6.1 Tool 계약
- `ITool`:
  - `Name`, `Description`
  - `InputSchema`(JSON Schema)
  - `Policy`(RequiresApproval/ReadOnly/Risk)
  - `ExecuteAsync(JsonElement args, ToolContext ctx) -> ToolResult`

### 6.2 ToolResult 표준
- `Ok` (bool)
- `Stdout`, `Stderr` (옵션)
- `Data` (구조화 JSON; 모델에게 주로 전달)
- `Diagnostics` (Code/Message/Details)

### 6.3 MVP Tool 목록
**읽기/탐색**
- `ListFiles(glob, maxDepth)`
- `ReadFile(path, startLine, endLine)`  
- `SearchText(query, includeGlobs, excludeGlobs, maxResults)` (초기엔 rg 호출도 실용적)

**쓰기**
- `ApplyPatch(unifiedDiff)` (핵심)
- (옵션/제한적) `WriteFile(path, contents)`

**실행**
- `RunCommand(exe, args[], cwd, timeoutSec)`
- `GitStatus()`, `GitDiff()` (초기엔 git CLI로)
- `RunTests()` (repo별 테스트 커맨드 프로파일)

---

## 7. 정책(Policy)과 승인(Approval)

### 7.1 정책 엔진의 목적
- Tool 실행 전 “허용/차단/승인 필요”를 판정
- 공급자에 독립적이어야 함

### 7.2 승인 필요(권장 기본값)
- `RunCommand` 중 위험 커맨드(쉘 실행, 삭제, 시스템 디렉토리 접근 등)
- 네트워크 관련(예: curl/wget/git push)
- 대량 변경(ApplyPatch가 N 파일 이상 또는 diff 라인 수 임계치 초과)
- 파일 삭제/생성(초기에는 금지 또는 승인 필요)

### 7.3 안전 가드레일
- **repo root 탈출 방지**
  - path normalization 후 prefix 검사
  - symlink 통한 탈출 방지(추가 체크)
- `RunCommand`:
  - shell 직접 실행은 승인 필요
  - timeout 필수
  - stdout/stderr 크기 제한(로그 폭발 방지)
  - env allowlist(토큰/키 유출 방지)

---

## 8. 컨텍스트 관리(토큰/메모리 폭발 방지)

### 8.1 원칙
- “전체 파일/전체 로그”를 모델에 직접 넣지 않는다.
- 필요한 구간만 line range로 읽고, 검색 결과 중심으로 컨텍스트를 구성한다.
- 오래된 tool 결과/로그는 요약 후 원문은 artifact로 분리한다.

### 8.2 Claude에서 특히 중요
- Messages API는 stateless 기본이므로 “매번 히스토리 전송” 비용이 큼.
- tool_result 블록과 최근 작업 파일만 유지하고, 나머지는 요약/압축.

---

## 9. 로그/리플레이(Observability)

### 9.1 JSONL 세션 로그 항목(최소)
- user prompt
- 모델 스트리밍 이벤트(원문 + 표준 AgentEvent)
- tool call(이름, callId, argsJson)
- tool result(ok, diagnostics, data 요약)
- patch 적용 결과(성공/실패, 실패 hunk 정보)
- run command(명령, cwd, exit code, duration, stdout/stderr 요약)

### 9.2 리플레이
- 저장된 이벤트/툴 결과를 재생하여 “모델 호출 없이” 디버깅 가능.
- 실패 케이스 재현, 정책/프롬프트 개선에 활용.

---

## 10. CLI UX(Claude Code 스타일)

### 기능(초기)
- 스트리밍 텍스트 출력
- tool call 표시: `[tool] ReadFile path=...`
- 승인 프롬프트: `Approve? (y/N)`
- diff 미리보기(ApplyPatch는 승인 시 preview 제공)
- 옵션:
  - `--repo <path>`
  - `--provider openai|anthropic`
  - `--model <name>`
  - `--yes`(자동 승인; 로컬 실험용)
  - `--session <file.jsonl>`
  - `--dry-run`(실행 없이 계획/패치 제안만)

---

## 11. 단계별 구현 로드맵

### Phase 1: 코어 뼈대(1~2일)
- `Agent.Core`:
  - AgentEvent, ChatMessage, ModelRequest, ToolDefinition
  - ToolRegistry, ApprovalService(콘솔)
  - AgentOrchestrator(단일 tool call 처리 버전)
- `Agent.Tools`:
  - ReadFile, SearchText(간단), RunCommand(안전 최소)
- `Agent.Cli`:
  - 옵션 파싱, repo 설정, 실행

### Phase 2: OpenAI Provider (b)
- Responses API 스트리밍 SSE 파서 구현
- 텍스트 델타/툴콜 args done 처리 → ToolCallReady
- unknown 이벤트 Trace 로깅

### Phase 3: Claude Provider (c)
- Messages API 스트리밍 SSE 파서 구현
- tool_use + input_json_delta 누적 → ToolCallReady
- tool_result 블록 생성 로직 정착

### Phase 4: 멀티 툴콜/병렬(필수 안정화)
- 한 응답에서 여러 ToolCallReady 수집 → 전부 실행 → tool_result를 묶어서 다음 턴에 전달
- 반복 감지/종료 조건 강화

### Phase 5: ApplyPatch + Git 통합(품질 핵심)
- git apply 기반 patch 적용
- 실패 시 diagnostics(실패 hunk/context) 구조화
- GitStatus/GitDiff 도구 추가
- RunTests 도구 추가(언어별 프로파일)

### Phase 6: 컨텍스트 압축/요약 + JSON Schema 검증 강화
- Tool args 스키마 검증(라이브러리 도입)
- 대화/로그 요약 전략 도입
- 큰 로그는 artifact 파일로 저장 + 요약만 모델에 전달

---

## 12. 리스크 & 주의사항 체크리스트

### 기술 리스크
- 스트리밍 중 tool args JSON 조각 처리(부분 JSON 파싱 금지, 완성 시점에서만)
- 공급자 이벤트 타입 변화(unknown 이벤트 내성)
- Claude tool_result 매핑 오류(바로 다음 턴에 tool_result 필요)

### 운영/보안 리스크
- path traversal/symlink 탈출
- 위험 명령 실행(쉘, 삭제, 네트워크)
- secrets 노출(.env, credential 파일)
- 로그에 민감 정보 저장(마스킹/필터링 필요)

### 품질 리스크
- ApplyPatch 실패 반복(실패 이유 구조화 필수)
- 컨텍스트 폭발(라인 제한/검색 중심/요약)
- 무한 루프(반복 감지/Max iteration/질문 전환)

---

## 13. 완료 기준(Definition of Done)

- OpenAI/Claude 두 공급자 중 하나를 선택해 실행 가능
- 다음 작업을 end-to-end 수행:
  1) 관련 코드 검색/읽기
  2) unified diff 패치 생성
  3) ApplyPatch로 적용
  4) dotnet test(또는 지정 커맨드) 실행
  5) 결과에 따라 반복 또는 종료
- 위험 작업은 승인 프롬프트를 거치고, 거부 시 안전하게 중단
- 세션 로그(JSONL)가 남고, 최소한 “어떤 툴을 어떤 args로 실행했는지” 재현 가능

---
