# Day 4 - Wire CI with GitHub Actions

This piece reuses `QuotesIntegrationApi` and `Quotes.Tests.Integration` from Day 3 Piece 5 unchanged
(same SQLite-in-memory `WebApplicationFactory` integration tests, 13 tests) so the exercise can focus
entirely on the CI wiring rather than on new application code. The only change from the Day 3 Piece 5
copy is adding `coverlet.collector` to the test project, which is what actually produces a Cobertura
coverage file for `dotnet test --collect:"XPlat Code Coverage"` to write.

## Run locally

```bash
dotnet restore day-4/piece1
dotnet build day-4/piece1 --no-restore --configuration Release
dotnet test day-4/piece1 --no-build --configuration Release \
  --logger "trx;LogFileName=test-results.trx" \
  --collect:"XPlat Code Coverage" \
  --results-directory day-4/piece1/TestResults
```

All 13 tests pass; line coverage on this project is ~97%, well above the 70% gate.

## Where the workflow file actually lives

GitHub Actions only discovers workflows under the **repository root's** `.github/workflows/`, not
inside a subfolder. So although this exercise is scoped to `day-4/piece1`, the working file is at:

```
thinkschool_Shagun_Yadav/.github/workflows/ci.yml
```

It is scoped back to this folder with a `paths:` filter, so it only triggers on changes here (and on
changes to itself).

## What `ci.yml` does

- **Triggers**: `push` on any branch, and `pull_request` targeting `main` — both filtered to
  `day-4/piece1/**`.
- **Steps**: checkout → `actions/setup-dotnet@v4` (`10.0.x`) → `dotnet restore` → `dotnet build --no-restore`
  → `dotnet test --no-build` with a `trx` logger and `--collect:"XPlat Code Coverage"`.
- **Artifacts**: the `.trx` file and the `coverage.cobertura.xml` are uploaded as two separate
  `actions/upload-artifact@v4` artifacts (`test-results`, `coverage-report`), each with
  `if-no-files-found: error` so a silently-empty artifact fails loudly instead of hiding a broken step.
- **Coverage gate**: `dotnet test`'s `--collect:"XPlat Code Coverage"` has no built-in pass/fail
  threshold — it just emits a Cobertura XML. The workflow installs `dotnet-reportgenerator-globaltool`,
  turns that XML into a `TextSummary`, greps the `Line coverage: NN%` line out of it, and fails the job
  (`exit 1`) if that number is below `COVERAGE_THRESHOLD` (70).
- **Failure modes that fail the job**: any failing test (the `dotnet test` step itself returns non-zero,
  so the job stops there — the coverage step never even needs to run), or passing tests with coverage
  under 70%.

## Branch protection ("refuses to merge red CI")

I don't have `gh` CLI or a token in this environment, so I could not set this via API — it needs to be
done once, by hand, in the GitHub UI (repo **Settings → Branches → Add branch protection rule**):

1. Branch name pattern: `main`.
2. Enable **Require status checks to pass before merging**.
3. Search for and select **`build-and-test`** (the job name in `ci.yml` — it only appears in the list
   after the workflow has run at least once on the branch).
4. Optionally enable **Require branches to be up to date before merging**.

Once that's saved, a PR into `main` with a red `build-and-test` check is blocked from merging, which is
the actual enforcement mechanism behind "refuses to merge red CI" — the workflow failing is necessary
but not sufficient on its own without this setting.

## GitHub link

The exercise asks to push to the `thinkbridge-thinkschool` org; the current `origin` remote for this
repo is `ShagunYadav1208/thinkschool_Shagun_Yadav`, not that org. Push there (or add it as a second
remote) before sharing the link, then paste the folder/PR/run link here:

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-4/piece1

## Notes for mentor

Reused the Day 3 Piece 5 project as-is (no Docker/Testcontainers dependency) so the CI exercise tests
the pipeline mechanics — restore/build/test/artifact/coverage-gate — rather than fighting container
startup time in Actions. The Day 3 Piece 3 and Piece 6 workflow files have the same
"only-works-if-moved-to-repo-root" caveat noted in their own READMEs; this one is placed correctly from
the start.

## What did you learn this session?

_(fill in after pushing and watching the run go green)_

## What would break this?

A test project with `coverlet.collector` missing (or a project with zero coverable lines) produces no
`coverage.cobertura.xml` at all — `if-no-files-found: error` on the artifact upload catches the missing
file, but the `grep` in the coverage-gate step would also just fail to match and the `set -euo pipefail`
would abort the step, so the job still goes red rather than silently reporting 0% or skipping the gate.
