import readline from "node:readline";

function getArg(name, fallback) {
  const idx = process.argv.indexOf(name);
  if (idx === -1 || idx === process.argv.length - 1) return fallback;
  return process.argv[idx + 1];
}

const baseUrl = getArg("--base", "http://localhost:5000");
const workspacePath = getArg("--workspace", process.cwd());
const provider = getArg("--provider", "openai");
const model = getArg("--model", "gpt-5-mini");
const resumeId = getArg("--session", "");

async function postJson(url, body) {
  const resp = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const text = await resp.text();
  if (!resp.ok) {
    throw new Error(`HTTP ${resp.status}: ${text}`);
  }
  return text ? JSON.parse(text) : null;
}

async function createOrResumeSession() {
  if (resumeId) {
    const url = `${baseUrl}/api/sessions/${resumeId}/resume`;
    await postJson(url, { workspacePath, provider, model });
    return resumeId;
  }

  const data = await postJson(`${baseUrl}/api/sessions`, {
    workspacePath,
    provider,
    model,
  });
  return data.sessionId;
}

function parseSseLines(text) {
  const lines = text.split(/\r?\n/);
  const events = [];
  let current = { event: "", data: "" };

  for (const line of lines) {
    if (line.startsWith("event:")) {
      current.event = line.slice("event:".length).trim();
    } else if (line.startsWith("data:")) {
      current.data += line.slice("data:".length).trim();
    } else if (line === "") {
      if (current.data) {
        events.push(current);
      }
      current = { event: "", data: "" };
    }
  }

  return events;
}

async function streamEvents(sessionId, onEvent) {
  const resp = await fetch(`${baseUrl}/api/sessions/${sessionId}/stream`);
  if (!resp.ok || !resp.body) {
    throw new Error(`Stream failed: HTTP ${resp.status}`);
  }

  const reader = resp.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });
    const parts = buffer.split(/\n\n/);
    buffer = parts.pop() || "";
    for (const part of parts) {
      for (const evt of parseSseLines(part)) {
        onEvent(evt);
      }
    }
  }
}

const rl = readline.createInterface({
  input: process.stdin,
  output: process.stdout,
  terminal: true,
});

let pendingApproval = null;

function printStatus(message) {
  process.stdout.write(`\n${message}\n> `);
}

function handleEvent(evt) {
  const payload = evt.data ? JSON.parse(evt.data) : {};
  switch (evt.event) {
    case "text_delta":
      process.stdout.write(payload.text || "");
      break;
    case "approval_required":
      pendingApproval = {
        callId: payload.callId,
        tool: payload.tool,
      };
      printStatus(
        `[Approval Required] ${payload.tool} callId=${payload.callId}\nApprove? (y/N)`
      );
      break;
    case "tool_call":
      printStatus(`[Tool] ${payload.tool}`);
      break;
    case "tool_result":
      printStatus(`[Tool Result] ${payload.tool} ok=${payload.result?.ok}`);
      break;
    case "completed":
      printStatus(`[Completed] stopReason=${payload.stopReason ?? "unknown"}`);
      break;
    case "trace":
      printStatus(`[Trace] ${payload.kind}: ${payload.data}`);
      break;
    case "error":
      printStatus(`[Error] ${payload.message}`);
      break;
    default:
      printStatus(`[Event] ${evt.event}`);
      break;
  }
}

async function sendChat(sessionId, message) {
  await postJson(`${baseUrl}/api/sessions/${sessionId}/chat`, { message });
}

async function sendApproval(sessionId, callId, approved) {
  await postJson(`${baseUrl}/api/sessions/${sessionId}/approvals/${callId}`, {
    approved,
  });
}

async function main() {
  const sessionId = await createOrResumeSession();
  console.log(`Session: ${sessionId}`);
  console.log("Type a prompt and press Enter. Use /exit to quit.");
  process.stdout.write("> ");

  streamEvents(sessionId, handleEvent).catch((err) => {
    printStatus(`Stream error: ${err.message}`);
  });

  rl.on("line", async (line) => {
    const trimmed = line.trim();
    if (!trimmed) {
      process.stdout.write("> ");
      return;
    }

    if (trimmed === "/exit" || trimmed === "/quit") {
      rl.close();
      process.exit(0);
    }

    if (pendingApproval) {
      if (trimmed.toLowerCase() === "y") {
        await sendApproval(sessionId, pendingApproval.callId, true);
        pendingApproval = null;
      } else {
        await sendApproval(sessionId, pendingApproval.callId, false);
        pendingApproval = null;
      }

      process.stdout.write("> ");
      return;
    }

    try {
      await sendChat(sessionId, trimmed);
    } catch (err) {
      printStatus(`Chat error: ${err.message}`);
    }
    process.stdout.write("> ");
  });
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
