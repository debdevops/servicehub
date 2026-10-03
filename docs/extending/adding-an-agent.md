# Adding an agent

An agent is **one class and one registration line**. You write what a single cycle does; the host runs the loop, the schedule, the heartbeat,
the error handling, the pause and the authority check — so every agent behaves the same way when things go wrong. The Agents page on the
Advanced side lists exactly the agents registered in the build, with a name, purpose, health, history and a pause control, **without a line of UI
being written**. If you find you need UI, API, database or navigation changes to add an agent, the seam is wrong — raise that, don't work around it.

Adding a *cloud* is a different job: see [Adding a messaging provider](adding-a-provider.md).

## The two things you write

**1. The class** — implement `IAgent` (`services/api/src/ServiceHub.Core/Interfaces/IAgent.cs`) in `ServiceHub.Infrastructure/Agents/` (or beside the
feature it serves, like `Insights/`). It has two members:

```csharp
public AgentDescriptor Descriptor { get; }                                // who it is — constant for the process's lifetime
public Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct);    // do one unit of work
```

`BackupAgent` (`services/api/src/ServiceHub.Infrastructure/Agents/BackupAgent.cs`) is the smallest complete example:

```csharp
public sealed class BackupAgent : IAgent
{
    public BackupAgent(IServiceScopeFactory scopes, IOptions<BackupOptions> options)
    {
        // ...
        Descriptor = new AgentDescriptor(
            Id: "backup",
            Name: "Backup Keeper",
            Purpose: "Takes a checked backup of ServiceHub's database on a schedule, and keeps the newest ones.",
            Kind: AgentKind.Maintain,
            Authority: AgentAuthority.Observes,
            Cadence: TimeSpan.FromHours(hours),
            May: ["Write a backup into the data directory", "Delete backups older than the newest ones it keeps"],
            MayNot: ["Restore anything — only a person can, from Settings", "Touch any cloud or message"]);
    }

    public AgentDescriptor Descriptor { get; }

    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();   // agents are singletons: take scoped services per cycle
        var result = await scope.ServiceProvider.GetRequiredService<IBackupService>().CreateBackupAsync(ct);
        return result.IsFailure
            ? throw new InvalidOperationException("The scheduled backup did not complete.")   // throw = the cycle failed
            : new AgentCycleResult(1, 0, $"backup {result.Value.BackupId} taken");
    }
}
```

**2. The registration line** in `services/api/src/ServiceHub.Api/Program.cs`, with the others under `AddAgentPlatform()`:

```csharp
builder.Services.AddAgent<MyAgent>();
```

That is the whole change. (`BackupAgent` is wrapped in an `if` because it is off unless `Backup:ScheduledBackupIntervalHours` is above 0; yours
need not be.)

## Writing the descriptor

| Field | Rule |
|---|---|
| `Id` | Stable, kebab-case, **never renamed once shipped** — pause state, history and links are keyed on it |
| `Name` | What a person calls it. Title case, no "Worker" or "Service" suffix |
| `Purpose` | **One hand-written sentence** about what it does *for the reader*. If it reads like the class name with spaces in it, it isn't finished |
| `Kind` | `Watch`, `Decide`, `Act` or `Maintain` |
| `Authority` | What it may do alone — see below |
| `Cadence` | How often a cycle runs |
| `May` / `MayNot` | One plain line each. Say the limits; they are the safety story the page shows |
| `Notes` | Where it behaves differently per cloud, say so in plain words |
| `LedgerActor` | The actor name it records ledger entries under, so its timeline can come from the ledger rather than a new table |
| `Needs` | `None`, `WatchedCloud` or `VerifiableCloud` — see below |

### Authority is a contract, not a comment

`AgentAuthority` is `Observes`, `Proposes`, `ActsWithApproval` or `ActsAutonomously`. The host **enforces** it: a cycle that reports
`Changed > 0` from an agent that only `Observes` or `Proposes` is treated as a defect, surfaced loudly, never counted as a statistic. Choose the
lowest authority that does the job. Anything that can change something outside ServiceHub (`ActsWithApproval` or `ActsAutonomously`) is shown first
on the Agents page, and there should be very few. A recovery action never skips the one gated route: it goes through the eligibility gate, which
fails closed.

### Don't run an agent that has nothing to do — `Needs`

If no connected cloud gives the agent anything to do, the host does not run it and the Agents page does not list it; connecting a cloud that does
starts it again. It reads **capabilities**, never a cloud's name:

- `WatchedCloud` — needs a connected cloud with `SupportsRepeatablePeek` (a watcher is pointless where nothing may be polled).
- `VerifiableCloud` — needs a connected cloud with `CanProveDlqAbsence` (a verifier is pointless where nothing can be proved).

## What a cycle must do

- **Return normally to report; throw to fail.** The host records the failure and keeps going; one agent failing never stops another.
- **Report honestly.** `new AgentCycleResult(examined, changed, summary, degraded)`. The summary is one short line for a person ("scanned 12 queues,
  3 new dead letters"), never an exception message. Set `Degraded: true` when the cycle finished but something was skipped, and say what in the
  summary. Zero examined is a normal, healthy answer — use `AgentCycleResult.Idle()`.
- **Honour the cancellation token promptly.** Shutdown waits for it.
- **Be safe after an abrupt restart.** Agents are independent loops over durable state; that is why a restart is safe. Keep state in the database,
  not in fields.
- **Use scoped services per cycle** (`IServiceScopeFactory`), because agents are singletons.
- **Never branch on a provider's name.** Ask `ProviderCapabilities` (`CapabilityHonestyTests` enforces it).

## Testing it

Mirror `tests/backend/ServiceHub.UnitTests/Agents/AgentHostTests.cs`: build your agent, run it through an `AgentHost` with a `FakeTimeProvider`
and an `AgentRegistry`, and assert the registry shows it running, a failure is isolated and reported, a pause stops it acting, and a
change from a non-acting agent is rejected. `AgentApplicabilityTests` shows how `Needs` is tested. Then run `./runtest.sh --backend`; the
Agents screen needs no test of its own because it renders the registry.
