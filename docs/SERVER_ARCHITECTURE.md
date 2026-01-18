# 서버형 백엔드 설계안 (ASP.NET Core 기반)

## 1. 목표
- 로컬 CLI 전용 구조를 **서버형 백엔드**로 확장한다.
- 콘솔/웹 클라이언트가 동일한 API로 접속하도록 한다.
- 기존 핵심 로직(`Agent.Core`, `Agent.Tools`, `Agent.Workspace`, `Agent.Logging`)을 재사용한다.
- 스트리밍 응답, 세션 재개, 정책 승인(Approval)을 서버 환경에 맞게 제공한다.

## 2. 비목표
- 멀티테넌트 SaaS 운영 수준의 인증/과금/오토스케일은 범위에서 제외.
- 웹 UI 고도화(디자인/복잡한 상태 관리)는 별도 작업으로 분리.

## 3. 전체 구성
```
Client (CLI/Web)
  └─ HTTPS/SSE
       └─ Agent.Server (ASP.NET Core)
            ├─ Session API
            ├─ Stream API (SSE)
            ├─ Tool Execution Host
            └─ Persistence (JSONL/DB)
```

## 4. 핵심 모듈 설계
### 4.1 신규 프로젝트
- `src/Agent.Server` (ASP.NET Core, Minimal API)
- `src/Agent.Contracts` (옵션): DTO, API 스키마, 공용 모델

### 4.2 재사용/확장
- `Agent.Core`: Orchestrator, 이벤트 모델 그대로 사용
- `Agent.Tools`: 서버에서 툴 실행 (정책/승인 연동)
- `Agent.Workspace`: 워크스페이스 루트 관리 (세션 단위로 분리 가능)
- `Agent.Logging`: JSONL 로그 유지 + 서버 세션 저장소 연동

## 5. 실행 흐름
1) 클라이언트가 세션 생성 요청
2) 서버가 세션 상태/로그 초기화
3) 클라이언트가 프롬프트 전송
4) Orchestrator 실행
5) TextDelta는 SSE로 스트리밍
6) ToolCallReady 시 정책 평가
7) 승인 필요 시 클라이언트에 승인 이벤트 전송
8) 승인 후 Tool 실행 및 결과 스트리밍
9) 완료 이벤트 전송 및 세션 저장

## 6. API 설계 (초안)
### 6.1 세션
- `POST /api/sessions`
  - 요청: `{ "workspacePath": "...", "provider": "openai", "model": "..." }`
  - 응답: `{ "sessionId": "..." }`

- `GET /api/sessions/{id}`
  - 응답: 세션 요약, 최근 상태

- `POST /api/sessions/{id}/resume`
  - 기존 JSONL 또는 저장소로부터 메시지 복원

### 6.2 채팅/스트리밍
- `POST /api/sessions/{id}/chat`
  - 요청: `{ "message": "..." }`
  - 응답: 202 + SSE 채널 안내 또는 바로 SSE 스트림

- `GET /api/sessions/{id}/stream`
  - `text/event-stream`
  - 이벤트 타입: `text_delta`, `tool_call`, `approval_required`, `tool_result`, `completed`, `trace`

### 6.3 승인
- `POST /api/sessions/{id}/approvals/{callId}`
  - 요청: `{ "approved": true }`

## 7. SSE 이벤트 스키마 (예시)
```
event: text_delta
data: { "text": "..." }

event: tool_call
data: { "callId": "...", "tool": "ReadFile", "args": { ... } }

event: approval_required
data: { "callId": "...", "tool": "RunCommand", "reason": "RequiresApproval" }

event: tool_result
data: { "callId": "...", "ok": true, "data": { ... } }

event: completed
data: { "stopReason": "complete" }
```

## 8. 세션 상태 관리
### 8.1 저장 전략
- 기본: JSONL 파일 (기존 `Agent.Logging` 재사용)
- 옵션: SQLite/파일 기반 KV 저장소

### 8.2 상태 구성
- `SessionId`, `WorkspaceRoot`, `Provider`, `Model`
- `ChatMessage` 목록 (재개용)
- `PendingApprovals` (대기 중 툴콜)

## 9. 워크스페이스 격리
- 세션마다 `WorkspaceRoot`를 명시적으로 바인딩
- `Agent.Workspace`의 안전성 검사를 재사용
- 서버 프로세스 계정 권한 범위 내에서만 접근 가능

## 10. 보안/승인 정책
- `PolicyEngine`는 서버에서도 동일하게 동작
- 승인 이벤트는 반드시 클라이언트에서 확인 후 진행
- 토큰/키는 서버 환경 변수로 관리
- 로그에는 민감정보 마스킹 적용 고려

## 11. 배포/운영
- 단일 프로세스 모드: 로컬 개발/개인 사용
- 서비스 모드: Windows Service 또는 systemd
- Kestrel HTTPS 바인딩, 리버스 프록시(Nginx/IIS) 선택

## 12. 마이그레이션 단계
1) `Agent.Server` 프로젝트 추가 및 DI 구성
2) 최소 API + SSE 스트리밍 엔드포인트 구현
3) 승인 흐름(approval_required) 이벤트 도입
4) 세션 저장소(JSONL) 연동
5) CLI/Web 클라이언트에서 서버 접속 옵션 추가

## 13. 리스크 및 보완
- 스트리밍 연결 끊김 시 재접속/재시작 처리 필요
- 승인 대기 중 서버 타임아웃 관리
- 세션 저장소 커질 때 로테이션/압축 필요

