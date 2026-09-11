# ParkFlow infra — data tier behind a private endpoint (Day 27)

Day 22's ParkFlow scaffold runs entirely on EF Core's InMemory provider — there is no Azure
deployment for the capstone yet. This `infra/` is new for Day 27: the *target* architecture, with
the specific hardening this piece asks for (see [../THREAT-MODEL.md](../THREAT-MODEL.md), section
3.2) already built in from the start rather than retrofitted.

## Layout

```
main.bicep                  - resource group + wires the three modules together
modules/
  network.bicep              - VNet, two subnets (App Service integration, private endpoints),
                                private DNS zone for privatelink.database.windows.net
  sql.bicep                  - one logical SQL server, five databases (one per module),
                                publicNetworkAccess: 'Disabled', private endpoint + DNS zone group
  api.bicep                  - App Service (Standard/S1+, required for VNet Integration),
                                regional VNet Integration into the network module's subnet
```

## What "private endpoint" actually buys here

Before this piece, the *only* deployed thing for ParkFlow was nothing — this is greenfield infra.
So the comparison isn't "was public, now private"; it's "built with the data tier unreachable
from the public internet from day one":

- `sqlServer.properties.publicNetworkAccess = 'Disabled'` — no public IP path to any of the five
  databases exists at all, full stop, independent of firewall rules or credentials.
- The only network path in is the private endpoint's NIC, which lives in
  `snet-private-endpoints` — itself only reachable from inside the VNet.
- The API reaches it by being VNet-integrated (`virtualNetworkSubnetId` on the Web App, in
  `snet-app-integration`) — its outbound SQL connections leave through the VNet, resolve the
  private DNS zone to the private IP, and never touch the public internet either.
- AAD-only authentication (`azureADOnlyAuthentication: true`, same pattern as day-23/24's
  `sql.bicep`) — there is no SQL-auth login to leak in the first place, network path aside.

## Why undeployed

Never run against real Azure — `az bicep build` compiles clean (0 warnings after fixing one
cloud-portability lint, see `network.bicep`'s DNS zone name), but this student's Azure for
Students subscription (`30e5e569-...`) has been in a `Disabled` state (spending limit) since
before Day 25 — see the `project-azure-subscription-disabled` memory. The same block that's held
up Day 25's Key Vault and Day 26's Application Insights deployment applies here. When the
subscription is usable again, `az deployment sub validate` / `azd provision` against this template
is the natural next step, same as the standing instructions already recorded for Day 25/26.

## What is not built here

- **A Key Vault for ParkFlow's `Security:ApiKey`.** `api.bicep` takes `extraAppSettings` so a
  `@Microsoft.KeyVault(SecretUri=...)` reference can be added the same way day-25's
  `keyvault.bicep` already does for a different app (`QuotesApi`) — building a second Key Vault
  module was out of scope for a pass focused on the data tier; see THREAT-MODEL.md section 5 for
  the reasoning on API-key auth generally.
- **Managed Identity from the API to SQL.** AAD-only auth is configured, but granting the Web
  App's system-assigned identity a database user (a T-SQL `CREATE USER ... FROM EXTERNAL
  PROVIDER` statement, same as day-23/24's deploy.md step 3) can only be run once the server is
  actually deployed.
- **A private endpoint / VNet Integration validation run.** Nothing here has been provisioned, so
  "the API can actually reach the database through the private endpoint" is a design claim backed
  by the standard Azure pattern (this is the documented, supported way to reach Azure SQL
  privately), not something exercised end-to-end yet.
