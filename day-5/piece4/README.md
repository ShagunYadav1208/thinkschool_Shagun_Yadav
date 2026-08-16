# Day 5 - Deploy via azd CLI

This piece reuses [Day 5 Piece 2](../piece2)'s `QuotesApi` (copied in) as the app azd deploys.

## Status: real, live, and fully working

An earlier draft of this README stopped at "no Azure subscription available, nothing was
provisioned." That's no longer true — a working **Azure for Students** subscription became
available (the same one used for real in [Day 5 Piece 3](../piece3)), so this was re-run for real
against it: `azd up`, three real bugs found by actually deploying (not by inspection), all three
fixed, and a genuinely live, working Container App at the end. Nothing below is illustrative.

## Installing azd and `azd init` — unchanged from the first draft

```
> winget install microsoft.azd --accept-package-agreements --accept-source-agreements
Successfully installed
> azd version
azd version 1.31.1 (commit 38c0e3235ee7a27a942a95431b0d0a8a530ae6b0) (stable)

> azd init --from-code -e thinkschool-quotes-api
Detected services:
  .NET
  Detected in: QuotesApi
  (✓) Done: Generating ./azure.yaml
  (✓) Done: Generating ./next-steps.md
SUCCESS: Your app is ready for the cloud!
```

`azure.yaml` needed no manual edit — `azd init`'s detection already produced the right thing:

```yaml
name: piece4
services:
    quotes-api:
        project: QuotesApi
        host: containerapp
        language: dotnet
resources:
    quotes-api:
        type: host.containerapp
        port: 8080
```

Two corrections to the exercise text, both still accurate:
- `azd init` (1.31.1) only writes `azure.yaml` to disk; the Bicep is generated in memory until you
  run `azd infra gen` explicitly (see `infra/` for what that produced — Azure Verified Modules for
  monitoring, Container Registry, the managed environment, a user-assigned identity, and the
  container app itself).
- `brew install azd` is the macOS instruction; `winget install microsoft.azd` is the Windows
  equivalent used here.

## `azd up` — real output, real bugs, real fixes

```
> azd provision --no-state --no-prompt
  (✓) Done: Resource group: rg-thinkschool-quotes-api (2.596s)
  (✓) Done: Log Analytics workspace: log-kjoiqlfbl4bpk (25.95s)
  (✓) Done: Container Registry: crkjoiqlfbl4bpk (24.059s)
  (✓) Done: Application Insights: appi-kjoiqlfbl4bpk (8.225s)
  (✓) Done: Portal dashboard: dash-kjoiqlfbl4bpk (2.205s)
  (✓) Done: Container Apps Environment: cae-kjoiqlfbl4bpk (2m34.247s)
  (✓) Done: Container App: quotes-api (35.626s)
SUCCESS: Your application was provisioned in Azure in 5 minutes 35 seconds.
```

The first `azd deploy` after that failed, and each failure was a real, separate bug — found by
actually deploying against live Azure, not by code review:

**Bug 1 — `ImagePullBackOff`.** `QuotesApi.csproj` (copied from piece2) hardcoded
`<ContainerImageName>quotes-api</ContainerImageName>`. `azd deploy` pushed the image under that
fixed repository name, but separately updated the Container App to reference azd's own computed
path (`piece4/quotes-api-<env-name>`) — a path nothing was ever pushed to. Confirmed directly:
`az containerapp show` and `az acr repository list` showed two different repository names.

**Bug 2 — `CrashLoopBackOff` after fixing bug 1.** Same csproj also hardcoded an Alpine
(`mcr.microsoft.com/dotnet/aspnet:10.0-alpine`, musl libc) base image with `ContainerFamily=alpine`
— piece2's own choice, for piece2's own manual `dotnet publish --os linux-musl --arch x64`
workflow. `azd deploy`'s build path resolves its own RID with no exposed way to pin it to
`linux-musl-x64`, so it restored the glibc-linked `linux-x64` SQLitePCLRaw native library into the
musl base image anyway. Confirmed live in the container logs:
```
Error loading shared library libe_sqlite3: No such file or directory
```
Fix: dropped `ContainerImageName`, `ContainerImageTag`, `ContainerBaseImage`, and
`ContainerFamily` from `QuotesApi.csproj` entirely. This exercise never asked for Alpine — that
was piece2's requirement, copied over along with the rest of the project without being needed
here. With no base image pinned, the SDK's default base image tracks whatever RID actually gets
published, so libc always matches.

**Bug 3 — `SqliteException: unable to open database file`, after fixing bugs 1 and 2.** Exactly
the second bug piece2's own README documents: .NET's container images run as a non-root user since
.NET 8, `/app` isn't writable by it, and the default connection string
(`Data Source=quotes.db`, relative to `/app`) can never work. Confirmed live in the container logs:
```
SQLite Error 14: 'unable to open database file'
```
Fix: added a `ConnectionStrings__DefaultConnection` environment variable
(`Data Source=/tmp/quotes.db`) to the container app definition in `infra/resources.bicep` — the
infra-level equivalent of piece2's `docker run -e ConnectionStrings__DefaultConnection=...`
override, for the same reason: `/tmp` is world-writable, `/app` isn't, and baking a path into the
image is worse than configuring it at the infra layer. Same caveat as piece2: this is ephemeral,
wiped on every new revision — fine for this exercise, not for real data.

After all three fixes, `azd provision` (to apply the Bicep env var change) then `azd deploy`
produced a genuinely healthy, running replica:

```
> az containerapp replica list --name quotes-api --resource-group rg-thinkschool-quotes-api
quotes-api--0000002-6b59dff7b7-v6kgp   Running   True   restartCount: 0
```

## The live URL and real curls

```
https://quotes-api.wonderfulpebble-94a9da27.eastasia.azurecontainerapps.io/
```

```
> curl -s -w "\nHTTP %{http_code}\n" https://quotes-api.wonderfulpebble-94a9da27.eastasia.azurecontainerapps.io/health
Healthy
HTTP 200

> curl -s -w "\nHTTP %{http_code}\n" https://.../api/quotes
[]
HTTP 200

> curl -s -w "\nHTTP %{http_code}\n" -X POST https://.../api/quotes \
    -H "Content-Type: application/json" \
    -d '{"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal."}'
{"id":1,"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal.","createdAt":"2026-08-16T10:15:57.3981384+00:00"}
HTTP 201

> curl -s -w "\nHTTP %{http_code}\n" https://.../api/quotes
[{"id":1,"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal.","createdAt":"2026-08-16T10:15:57.3981384+00:00"}]
HTTP 200
```

Also confirmed live against this same deployment: the pagination and null-author validation fixes
(same bugs as piece2, fixed identically here) hold up — `?page=0&size=10` returns only the `page`
validation error, and `{"author": null, ...}` returns a clean 400 instead of a 500.

## What's real vs. what's provisioned right now

Everything above — every command, every log line, every curl response — is genuine output from
this session. As of writing, the resources are still live under
`rg-thinkschool-quotes-api` (eastasia): Container Registry, Log Analytics, Application Insights, a
Container Apps environment, and the running `quotes-api` container app. Since Azure for Students
credit is finite, whether to tear this down after review is worth deciding explicitly rather than
leaving it running indefinitely.

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-5/piece4

(Not yet pushed — I don't commit or push without being asked. Ready for you to review, stage, and
push yourself.)

## Notes for mentor

Three real, independent deployment bugs surfaced here, all only discoverable by actually running
`azd deploy` against live Azure rather than by reading the Bicep or the csproj: a repository-path
mismatch between what azd pushes and what it references, a libc mismatch between an Alpine base
image and azd's own RID resolution (same underlying class of bug as piece2, reached through a
different pipeline), and the non-root/`$HOME` writability issue piece2 already documented. Worth
flagging that "copy an app that works standalone into an azd project" is not guaranteed to deploy
cleanly without re-verifying its container-specific assumptions against azd's own build path
specifically.

## What did I learn this session?

A project's container properties (`ContainerImageName`, `ContainerBaseImage`, `ContainerFamily`)
that are correct for one build path (a manual `dotnet publish /t:PublishContainer` + `docker run`)
are not automatically correct for another (`azd deploy`, which owns image naming and RID
resolution itself). Copying a working app's `.csproj` unmodified into a new deployment context
doesn't carry its container assumptions safely — each needs re-verifying against how *that*
specific pipeline actually builds and references images, not assumed compatible by analogy.

## What would break this?

The `/tmp` connection string means every new revision (redeploy, scale event, or restart) starts
with an empty database — there is no persistent volume. For anything beyond this exercise, that
needs a real persistent store (Azure Files mount, or swapping SQLite for a networked database)
rather than a path inside the container's writable layer.
