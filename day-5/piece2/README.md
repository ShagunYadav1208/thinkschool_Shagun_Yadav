# Day 5 - Container image from `dotnet publish` (no Dockerfile)

This piece reuses [Day 2 Piece 1](../../day-2/piece1)'s `QuotesApi` (EF Core + Sqlite CRUD, no
auth, no external dependencies) as the app to containerize — a good fit for this exercise since it
has no moving parts beyond the app itself. Everything below is real: I actually ran
`dotnet publish .../t:PublishContainer` against a local Docker daemon, ran the resulting image, and
curled it.

## `QuotesApi.csproj` container properties

```xml
<!-- .NET SDK container support (docs.microsoft.com/dotnet/core/docker/publish-as-container).
     No Dockerfile: "dotnet publish" targeting linux x64 with target PublishContainer reads
     these properties and builds and tags the image straight into the local Docker daemon.
     Alpine base for a smaller image than the default Debian-based aspnet image. -->
<PropertyGroup>
  <ContainerImageName>quotes-api</ContainerImageName>
  <ContainerImageTag>0.1.0</ContainerImageTag>
  <ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:10.0-alpine</ContainerBaseImage>
  <!-- Alpine's base image is musl libc, not glibc. Without this, a plain linux/x64 publish
       restores the glibc-linked linux-x64 SQLitePCLRaw native library, which throws
       DllNotFoundException at startup on Alpine (see "what would break this" below). This
       property makes the container tooling restore/publish the linux-musl-x64 RID instead,
       matching the base image's libc. -->
  <ContainerFamily>alpine</ContainerFamily>
</PropertyGroup>
```

One deviation from the exercise text, found by actually building it: the SDK now warns
`ContainerImageName` is obsolete in favor of `ContainerRepository` (renamed after this exercise was
written). It still works — the warning below is the only effect — so I kept the name the exercise
asked for and just noted the rename here rather than silently swapping it.

## Building the image — real output

```
> dotnet publish --os linux-musl --arch x64 /t:PublishContainer

  Determining projects to restore...
  Restored C:\...\day-5\piece2\QuotesApi\QuotesApi.csproj (in 1.32 sec).
  QuotesApi -> C:\...\QuotesApi\bin\Release\net10.0\linux-musl-x64\QuotesApi.dll
  QuotesApi -> C:\...\QuotesApi\bin\Release\net10.0\linux-musl-x64\publish\
C:\Program Files\dotnet\sdk\10.0.302\Containers\build\Microsoft.NET.Build.Containers.targets(85,5):
warning CONTAINER003: The property 'ContainerImageName' was set but is obsolete - please use
'ContainerRepository' instead.
  Building image 'quotes-api' with tags '0.1.0' on top of base image 'mcr.microsoft.com/dotnet/aspnet:10.0-alpine'.
  Pushed image 'quotes-api:0.1.0' to local registry via 'docker'.
```

```
> docker images quotes-api

IMAGE              ID             DISK USAGE   CONTENT SIZE   EXTRA
quotes-api:0.1.0   1c1c838fe0d4        187MB           57MB
```

Note the command actually run was `--os linux-musl --arch x64`, not the exercise's literal
`--os linux --arch x64` — see "what would break this" for why that one-word difference matters.

## Running it — real output

```
> docker run -d --name quotes-api-day5 -p 8080:8080 \
    -e ConnectionStrings__DefaultConnection="Data Source=/tmp/quotes.db" \
    quotes-api:0.1.0

a1353c0b1f459112a9aec390346ca5ef0cdda3f2646c7cd2c53aeb6aa2d5878e

> docker ps --filter name=quotes-api-day5

CONTAINER ID   IMAGE              COMMAND                  CREATED          STATUS          PORTS                                         NAMES
a1353c0b1f45   quotes-api:0.1.0   "dotnet /app/QuotesA…"   19 seconds ago   Up 17 seconds   0.0.0.0:8080->8080/tcp, [::]:8080->8080/tcp   quotes-api-day5

> docker logs quotes-api-day5   (tail)

info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20260810085054_InitialCreate'.
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20260811045314_AddQuoteCreatedAt'.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://[::]:8080
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Production
info: Microsoft.Hosting.Lifetime[0]
      Content root path: /app
```

(The `-e ConnectionStrings__DefaultConnection=...` override is explained below — it's the fix for
the second real bug this exercise surfaced.)

## Confirming it's the real app — real output

```
> curl -s -w "\nHTTP %{http_code}\n" http://localhost:8080/health
Healthy
HTTP 200

> curl -s -w "\nHTTP %{http_code}\n" http://localhost:8080/api/quotes
[]
HTTP 200

> curl -s -w "\nHTTP %{http_code}\n" -X POST http://localhost:8080/api/quotes \
    -H "Content-Type: application/json" \
    -d '{"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal."}'
{"id":1,"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal.","createdAt":"2026-08-14T04:22:40.0221321+00:00"}
HTTP 201

> curl -s -w "\nHTTP %{http_code}\n" http://localhost:8080/api/quotes
[{"id":1,"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal.","createdAt":"2026-08-14T04:22:40.0221321+00:00"}]
HTTP 200
```

`/health` is a real `AddHealthChecks()` / `MapHealthChecks("/health")` endpoint added to
[`Program.cs`](QuotesApi/Program.cs) for this exercise. The POST-then-GET round trip proves this
isn't a static placeholder — it's the actual EF Core-backed API, migrating and querying a real
SQLite database inside the container.

## Two real bugs this exercise surfaced (not simulated)

Following the exercise's literal instructions produced two crashes, in order. Both are documented
here instead of quietly worked around, because they're the actual lesson of this piece.

**1. `--os linux --arch x64` + an Alpine base image is broken by default.**

Alpine uses musl libc, not glibc. `--os linux --arch x64` resolves to RID `linux-x64` (glibc), so
`dotnet publish` restores the glibc-linked SQLite native library and copies only that one into the
image. On container start:

```
Unhandled exception. System.TypeInitializationException: The type initializer for
'Microsoft.Data.Sqlite.SqliteConnection' threw an exception.
 ---> System.DllNotFoundException: Unable to load shared library 'e_sqlite3' or one of its
dependencies. ...
Error relocating /app/libe_sqlite3.so: fcntl64: symbol not found
```

Adding `<ContainerFamily>alpine</ContainerFamily>` to the csproj was necessary but *not* sufficient
by itself while `--os linux --arch x64` was still on the command line — that explicit flag pair
still forced RID `linux-x64`. The fix that actually worked was publishing with
`--os linux-musl --arch x64` instead, which resolves to RID `linux-musl-x64` and pulls in the
musl-linked native SQLite library. Confirmed by checking the publish output directory before and
after: `bin/Release/net10.0/linux-x64/publish/libe_sqlite3.so` (glibc, broken) versus
`bin/Release/net10.0/linux-musl-x64/publish/libe_sqlite3.so` (musl, works).

**2. The container runs as a non-root user, and `/app` isn't writable.**

Once SQLite could load, it still failed to open the database file:

```
Microsoft.Data.Sqlite.SqliteException (0x80004005): SQLite Error 14: 'unable to open database file'.
```

.NET's container images have run as a non-root user by default since .NET 8 (a deliberate security
hardening — checked with `docker run --rm --entrypoint sh quotes-api:0.1.0 -c "id"` →
`uid=1654(app) gid=1654(app)`). `/app` is owned by `root:root` at `755`, so `app` can't create
`quotes.db` there — the default connection string (`Data Source=quotes.db`, relative to `/app`)
can never work in this image. Rather than bake a path into the image or weaken the container's
security posture, I overrode the connection string at `docker run` time to point at `/tmp`, which
is world-writable (`drwxrwxrwt`) in this base image:

```
-e ConnectionStrings__DefaultConnection="Data Source=/tmp/quotes.db"
```

This is also the more correct pattern generally (12-factor config via environment, not baked into
the image) — a real deployment would instead mount a persistent volume at a writable path and point
the connection string there, since `/tmp` is wiped whenever the container is removed.

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-5/piece2

(Not yet pushed — I don't commit or push without being asked. This piece is ready for you to
review, stage, and push yourself.)

## Notes for mentor

The exercise's literal command (`dotnet publish --os linux --arch x64 /t:PublishContainer` against
an Alpine base image) does not work as written for any app that P/Invokes a native library shipped
per-RID — SQLite here, but the same issue hits `System.Drawing`, native crypto libs, etc. The
one-word fix (`linux-musl` instead of `linux`) isn't obvious from the error message alone; the
`DllNotFoundException` output lists several search paths that all look like path/packaging
problems, not a libc mismatch. Worth flagging if this exercise is meant to be followed literally by
students without also hitting this wall.

## What did I learn this session?

The RID you publish for and the base image's libc have to match, and "linux x64" as a phrase is
ambiguous between them — .NET treats `linux-x64` (glibc) and `linux-musl-x64` (musl) as genuinely
different platforms for native interop purposes, not just a naming detail. Also: `ContainerFamily`
alone doesn't override an explicit `--os`/`--arch` pair on the command line; the RID that ships in
the image is whatever the publish step actually produced, and it's worth checking directly (e.g.
which `libe_sqlite3.so` landed in the publish output) rather than trusting that a property was
"probably" respected.

## What would break this?

Restarting or removing the container loses all data — `/tmp/quotes.db` lives in the container's
writable layer, not a volume, so `docker rm` (or any host reboot that clears `/tmp` inside a fresh
container) wipes it. For anything beyond this exercise, that connection string needs to point at a
mounted volume (`docker run -v quotes-data:/data -e ConnectionStrings__DefaultConnection="Data
Source=/data/quotes.db"`), not `/tmp`.
