# Day 5 - Azure Container Apps fundamentals

## Status: real. This actually ran.

Earlier attempts in this session hit "no subscription found" on every account tried (see git
history / prior notes if you want the archaeology). That changed: logged in fresh via `az login
--use-device-code` with an account that has an active **Azure for Students** subscription, and ran
[`scripts/create-environment.sh`](scripts/create-environment.sh) for real against it. Nothing
below is illustrative — every command actually executed, every JSON block is the real response.

```
$ az login --use-device-code
To sign in, use a web browser to open the page https://login.microsoft.com/device and enter the
code HF29XD9CC to authenticate.
[
  {
    "cloudName": "AzureCloud",
    "homeTenantId": "8d46a076-d093-416d-a57b-8692cde13bf8",
    "id": "30e5e569-cb36-491a-91c9-2d880095bdcb",
    "isDefault": true,
    "name": "Azure for Students",
    "state": "Enabled",
    "tenantDisplayName": "Amity University",
    "user": { "name": "shagun.yadav3@s.amity.edu", "type": "user" }
  }
]
```

## The commands, run for real

```bash
az group create \
  --name thinkschool-rg \
  --location centralindia

az containerapp env create \
  --name thinkschool-env \
  --resource-group thinkschool-rg \
  --location centralindia

az containerapp env show \
  --name thinkschool-env \
  --resource-group thinkschool-rg \
  --output json
```

`az containerapp env create` also needs the `Microsoft.App` and `Microsoft.OperationalInsights`
resource providers registered on the subscription first (a one-time step per subscription, not per
environment) — included in [`scripts/create-environment.sh`](scripts/create-environment.sh) as:

```bash
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait
```

### Real output: resource group

```json
{
  "id": "/subscriptions/30e5e569-cb36-491a-91c9-2d880095bdcb/resourceGroups/thinkschool-rg",
  "location": "centralindia",
  "managedBy": null,
  "name": "thinkschool-rg",
  "properties": { "provisioningState": "Succeeded" },
  "tags": null,
  "type": "Microsoft.Resources/resourceGroups"
}
```

### Real output: `az containerapp env show`

```
WARNING: No Log Analytics workspace provided.
WARNING: Generating a Log Analytics workspace with name "workspace-thinkschoolrgsHAV"
Container Apps environment created.
```

```json
{
  "id": "/subscriptions/30e5e569-cb36-491a-91c9-2d880095bdcb/resourceGroups/thinkschool-rg/providers/Microsoft.App/managedEnvironments/thinkschool-env",
  "location": "Central India",
  "name": "thinkschool-env",
  "properties": {
    "appInsightsConfiguration": null,
    "appLogsConfiguration": {
      "destination": "log-analytics",
      "logAnalyticsConfiguration": {
        "customerId": "569c17fe-0f8e-47d6-aacf-c12b1e09bd02",
        "sharedKey": null
      }
    },
    "customDomainConfiguration": {
      "certificateKeyVaultProperties": null,
      "certificatePassword": null,
      "certificateValue": null,
      "customDomainVerificationId": "ECAF2534FF9ABC9A11C951BEF2CE3B7EA8E76AB326915AB2768E533FD7BB3CEF",
      "dnsSuffix": null,
      "expirationDate": null,
      "subjectName": null,
      "thumbprint": null
    },
    "daprAIConnectionString": null,
    "daprAIInstrumentationKey": null,
    "daprConfiguration": { "version": "1.16.4-msft.11" },
    "defaultDomain": "lemongrass-cabde085.centralindia.azurecontainerapps.io",
    "eventStreamEndpoint": "https://centralindia.azurecontainerapps.dev/subscriptions/30e5e569-cb36-491a-91c9-2d880095bdcb/resourceGroups/thinkschool-rg/managedEnvironments/thinkschool-env/eventstream",
    "infrastructureResourceGroup": null,
    "ingressConfiguration": null,
    "kedaConfiguration": { "version": "2.18.1" },
    "openTelemetryConfiguration": null,
    "peerAuthentication": { "mtls": { "enabled": false } },
    "peerTrafficConfiguration": { "encryption": { "enabled": false } },
    "provisioningState": "Succeeded",
    "publicNetworkAccess": "Enabled",
    "staticIp": "20.204.210.161",
    "vnetConfiguration": null,
    "workloadProfiles": [
      { "enableFips": false, "name": "Consumption", "workloadProfileType": "Consumption" }
    ],
    "zoneRedundant": false
  },
  "resourceGroup": "thinkschool-rg",
  "systemData": {
    "createdAt": "2026-08-14T08:51:22.7660506",
    "createdBy": "shagun.yadav3@s.amity.edu",
    "createdByType": "User"
  },
  "type": "Microsoft.App/managedEnvironments"
}
```

Also saved as [`scripts/env-show-output.json`](scripts/env-show-output.json), exactly as written
by the script — not re-typed or cleaned up.

Fields that matter for this exercise's framing:

- **`defaultDomain`** (`lemongrass-cabde085.centralindia.azurecontainerapps.io`) — the shared DNS
  suffix every app deployed into this environment gets its public FQDN under. This is the concrete
  form of "the environment is a logical boundary": apps in the same environment share this domain
  and the Log Analytics destination below; apps in a different environment don't.
- **`appLogsConfiguration.logAnalyticsConfiguration.customerId`** — every container in every app in
  this environment ships stdout/stderr here by default. This is the "built-in observability" the
  exercise mentions — no OpenTelemetry/Jaeger wiring required to get basic log aggregation, unlike
  the app-level tracing in [Day 5 Piece 1](../piece1), which is a separate, complementary concern.
- **`appInsightsConfiguration: null`** — creating the environment this way (bare `az containerapp
  env create`, no flags) does *not* wire up Application Insights. [Day 5 Piece 4](../piece4)'s
  `azd`-generated Bicep does, via a separate `monitoring` module — worth knowing these are two
  different paths to "observability," not the same thing.
- **`daprConfiguration`/`kedaConfiguration`** — Dapr and KEDA versions the environment runs
  under the hood, present in the real response even though I didn't predict either field before
  running this for real (see "what did I learn," below).

## One correction to the exercise text

The exercise says "`--scale-rule` for autoscale triggers" — that flag doesn't exist.
`az containerapp create --help` shows the real, current flags are `--scale-rule-name`,
`--scale-rule-type` (defaults to `http`), and trigger-specific ones like
`--scale-rule-http-concurrency`. [`scripts/deploy-quotes-api.sh`](scripts/deploy-quotes-api.sh) has
a from-scratch `az containerapp create` using the correct flags, targeting the real environment
created above — an optional next step past this exercise's actual deliverable (resource group +
environment + `env show`), not yet run because it needs [Day 5 Piece 2](../piece2)'s image pushed
to a registry first.

## What's next, now that this is real

This subscription unblocks the rest of the week's Azure-dependent pieces that were previously
documented-only:

- [Day 5 Piece 4](../piece4) (`azd up`) can now actually provision and deploy against this same
  subscription.
- [Day 5 Piece 5](../piece5) (App Insights KQL) can run for real once Piece 4 deploys something.

Not done in this piece since it wasn't what was asked here — flagging it because the blocker that
justified skipping them is gone.

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-5/piece3

(Not yet pushed — I don't commit or push without being asked. Ready for you to review, stage, and
push yourself.)

## Notes for mentor

This piece was rewritten after initially being fully blocked (no subscription on any available
account) — the first draft's "what's real" framing is gone because the situation underneath it
changed: a working `Azure for Students` subscription became available mid-week, so I re-ran
everything for real rather than leaving the illustrative version in place. The resource group
(`thinkschool-rg`) and environment (`thinkschool-env`) are live in `centralindia` right now under
that subscription — worth deciding whether to keep them running for Piece 4/5 or tear them down
once this is reviewed, since Azure for Students credit is finite.

## What did I learn this session?

My own "illustrative example" JSON from before this ran for real was structurally close but
incomplete — I hadn't included `daprConfiguration`, `kedaConfiguration`, `customDomainConfiguration`,
or `appInsightsConfiguration` at all, because I was working from a general sense of the
`managedEnvironments` schema rather than a response I'd actually seen. The gap between "plausible
guess" and "what the API actually returns" was bigger than expected even for a resource shape I
was fairly confident about — a concrete reminder that "documented and reasoned-through" and
"verified" are different confidence levels, not interchangeable ones.

## What would break this?

`az containerapp env create` silently generates a Log Analytics workspace with an unpredictable,
auto-generated name (`workspace-thinkschoolrgsHAV` here) if `--logs-workspace` isn't passed
explicitly. Verified independently with `az resource list --resource-group thinkschool-rg`: it
landed in the same resource group as the environment, so `az group delete -n thinkschool-rg` does
clean up both — no orphaned resource to hunt down separately. But the name itself is still
unpredictable, which matters the moment anything else (an IaC template, a KQL query, an alert
rule) needs to reference that specific workspace by name rather than discovering it at
runtime — that reference has to be resolved dynamically, not hardcoded, or it breaks the next time
someone recreates the environment and gets a different generated name.
