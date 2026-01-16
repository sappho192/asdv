# Local Coding Agent

A .NET 8 console-based coding agent that operates on local repositories, inspired by Claude Code. Supports both OpenAI and Anthropic as LLM providers with a provider-agnostic architecture.

## Features

- **Provider-agnostic**: Switch between OpenAI and Anthropic with a single flag
- **Streaming**: Real-time streaming of model responses via SSE
- **Tool system**: Extensible tools for file operations, search, git, and command execution
- **Policy-based approval**: Dangerous operations require user approval
- **Session logging**: JSONL logs for reproducibility and debugging
- **Path safety**: Prevents path traversal and symlink escape attacks

## Quick Start

### Prerequisites

- .NET 8.0 SDK or later
- An API key for OpenAI or Anthropic

### Installation

```bash
git clone <repository-url>
cd asdv
dotnet build
```

### Usage

```bash
# Set your API key
export ANTHROPIC_API_KEY=your_key_here
# or
export OPENAI_API_KEY=your_key_here

# Run with default settings (OpenAI)
dotnet run --project src/Agent.Cli -- "Describe the project structure"

# Run with OpenAI
dotnet run --project src/Agent.Cli -- -p openai "Fix the bug in Program.cs"

# Run with auto-approve (use with caution)
dotnet run --project src/Agent.Cli -- -y "Add unit tests for Calculator.cs"

# Specify repository path
dotnet run --project src/Agent.Cli -- -r /path/to/repo "Refactor the authentication module"
```

### CLI Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--repo` | `-r` | Repository root path | Current directory |
| `--provider` | `-p` | LLM provider (`openai` or `anthropic`) | `openai` |
| `--model` | `-m` | Model name | Provider-specific |
| `--yes` | `-y` | Auto-approve all tool calls | `false` |
| `--session` | `-s` | Session log file path | Auto-generated |
| `--max-iterations` | | Maximum agent iterations | `20` |

## Architecture

```
Agent.sln
src/
  Agent.Cli/           # Entry point, CLI parsing
  Agent.Core/          # Orchestrator, events, policies
  Agent.Llm.Anthropic/ # Claude Messages API provider
  Agent.Llm.OpenAI/    # OpenAI Chat Completions API provider
  Agent.Tools/         # Tool implementations
  Agent.Workspace/     # File system safety
  Agent.Logging/       # JSONL session logging
tests/
  Agent.Core.Tests/
  Agent.Tools.Tests/
```

### Available Tools

| Tool | Description | Requires Approval |
|------|-------------|-------------------|
| `ReadFile` | Read file contents with optional line range | No |
| `ListFiles` | List files matching glob patterns | No |
| `SearchText` | Search for text patterns (regex) | No |
| `GitStatus` | Get repository status | No |
| `GitDiff` | View staged/unstaged changes | No |
| `ApplyPatch` | Apply unified diff patches | Yes |
| `RunCommand` | Execute shell commands | Yes |

## Configuration

### Environment Variables

| Variable | Description |
|----------|-------------|
| `ANTHROPIC_API_KEY` | API key for Anthropic Claude |
| `OPENAI_API_KEY` | API key for OpenAI |
| `OPENAI_BASE_URL` | Custom base URL for OpenAI-compatible APIs |

### Default Models

- **Anthropic**: `claude-sonnet-4-20250514`
- **OpenAI**: `gpt-4o`

## Session Logs

Session logs are saved in JSONL format to `.agent/session_YYYYMMDD_HHMMSS.jsonl` by default. Each line contains:

- User prompts
- Model streaming events
- Tool calls and results
- Timestamps

Use session logs for debugging, auditing, or replaying agent sessions.

## Safety Features

### Path Safety
- All file operations are restricted to the repository root
- Path traversal attempts (`../`) are blocked
- Symlink escape attacks are prevented

### Policy-Based Approval
- Dangerous commands (shell, delete, network) require user approval
- Large file changes trigger approval prompts
- Auto-approve mode (`--yes`) bypasses prompts (use with caution)

### Environment Filtering
- Sensitive environment variables (API keys, tokens) are filtered from subprocess environments

## Development

### Building

```bash
dotnet build
```

### Testing

```bash
dotnet test
```

### Project Dependencies

```
Agent.Cli
  └── Agent.Core
  └── Agent.Tools
  └── Agent.Workspace
  └── Agent.Llm.Anthropic
  └── Agent.Llm.OpenAI
  └── Agent.Logging
```

## Documentation

- [DESIGN.md](docs/DESIGN.md) - Design principles and architecture
- [IMPLEMENTATION.md](docs/IMPLEMENTATION.md) - Implementation details
- [AGENTS.md](AGENTS.md) - Agent architecture and extension guide
- [CLAUDE.md](CLAUDE.md) - Instructions for AI assistants working on this codebase

## License

Apache 2.0
