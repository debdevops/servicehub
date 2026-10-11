# ServiceHub for AI assistants (MCP)

> **In this folder:** a small server that lets an AI coding assistant *ask* your ServiceHub about dead letters — from your
> editor, in plain words. **It is read-only.** It cannot replay, purge, approve or change anything.

With it connected you can ask things like:

- *"Why did the message with order 4417 fail?"*
- *"What is stuck on the payments queue, and has replaying that kind of failure ever worked?"*
- *"What is ServiceHub waiting for a person to decide?"*

## Set it up

You need **Node 20 or newer** and a running ServiceHub. There is nothing to install: it is one file with no dependencies.

In your assistant's MCP settings, add:

```json
{
  "mcpServers": {
    "servicehub": {
      "command": "node",
      "args": ["/path/to/servicehub/tools/mcp/servicehub-mcp.mjs"],
      "env": { "SERVICEHUB_URL": "http://localhost:8080" }
    }
  }
}
```

With Claude Code: `claude mcp add servicehub -e SERVICEHUB_URL=http://localhost:8080 -- node /path/to/servicehub/tools/mcp/servicehub-mcp.mjs`

| Setting | |
|---|---|
| `SERVICEHUB_URL` | Where your ServiceHub answers. Default `http://localhost:8080` |
| `SERVICEHUB_API_KEY` | Optional. An API key ServiceHub knows; it is sent as `X-API-KEY`. Give it the **Viewer** role — this server never needs more |

## What it can ask

| Tool | Answers |
|---|---|
| `list_clouds` | Which clouds are connected, and what each can and cannot do |
| `list_dead_letters` | What is stuck: where, why, how many per reason |
| `get_dead_letter` | One message: the start of its body, its properties, its error |
| `explain_replay` | What *would* happen if it were replayed — the safety checks. Replays nothing |
| `list_failure_kinds` | Kinds of failure, whether each is growing, whether replay has helped |
| `failure_trust` | Whether ServiceHub may replay one kind of failure on its own, and why |
| `list_replays` | What was replayed, and how each ended |
| `waiting_for_a_person` | What ServiceHub stopped and asked about |
| `recovery_summary` | Outcome counts over a period |
| `list_agents` | The agents, what each may do, and what it last did |

## What it cannot do — by construction

- It only ever sends a **GET**, and only to the ten addresses above. There is no way to give it another address.
- There is no tool for replaying, purging, approving, pausing or changing a rule. Those stay with a person, or with
  ServiceHub's own safety checks.

`get_dead_letter` returns the start of a message's body, which can hold business data. Your assistant only sees it when
it asks for that one message.

## Check it

```bash
node --test tools/mcp/
```
