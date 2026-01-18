# Server Client Examples (SSE + Approvals)

This doc shows a minimal curl-based flow to create a session, stream events, send prompts, and approve tool calls.

## 0) Node.js Interactive Client
Requires Node.js 18+.
```bash
node scripts/server-client.mjs --base http://localhost:5000 --workspace C:\\REPO\\asdv --provider openai --model gpt-5-mini
```

OpenAI-compatible example (reads `asdv.yaml` from workspace if you omit model):
```bash
node scripts/server-client.mjs --base http://localhost:5000 --workspace C:\\REPO\\asdv --provider openai-compatible
```

Resume an existing session:
```bash
node scripts/server-client.mjs --base http://localhost:5000 --workspace C:\\REPO\\asdv --provider openai --model gpt-5-mini --session <id>
```

## 1) Create Session
```bash
curl -s -X POST http://localhost:5000/api/sessions \
  -H "Content-Type: application/json" \
  -d "{\"workspacePath\":\"C:\\\\REPO\\\\asdv\",\"provider\":\"openai\",\"model\":\"gpt-5-mini\"}"
```

Response:
```json
{"sessionId":"<id>"}
```

## 2) Open SSE Stream (listen in a separate terminal)
```bash
curl -N http://localhost:5000/api/sessions/<id>/stream
```

## 3) Send a Chat Prompt
```bash
curl -s -X POST http://localhost:5000/api/sessions/<id>/chat \
  -H "Content-Type: application/json" \
  -d "{\"message\":\"List the root files.\"}"
```

## 4) Approve a Tool Call (when approval_required arrives)
Example SSE event:
```
event: approval_required
data: {"type":"approval_required","callId":"<callId>","tool":"RunCommand","argsJson":"{...}","reason":"RequiresApproval"}
```

Approve:
```bash
curl -s -X POST http://localhost:5000/api/sessions/<id>/approvals/<callId> \
  -H "Content-Type: application/json" \
  -d "{\"approved\":true}"
```

## 5) Resume Session
```bash
curl -s -X POST http://localhost:5000/api/sessions/<id>/resume \
  -H "Content-Type: application/json" \
  -d "{\"workspacePath\":\"C:\\\\REPO\\\\asdv\",\"provider\":\"openai\",\"model\":\"gpt-5-mini\"}"
```
