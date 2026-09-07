# Deploy runbook

`what-if` (read-only, no resources touched) has been run against both parameter files - see
README "Current status" for the real output. **`create` has not been run** - applying this
against `prod.bicepparam` would modify the live `syquotes17-api` App Service and the live
`syquotes19sb` Service Bus namespace (see README "What the prod what-if actually found" for
exactly what it would change), which needs an explicit go-ahead first.

## 0. Prerequisites

```bash
az login
az account show   # confirm the "Azure for Students" subscription is active

export SQL_AAD_ADMIN_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
export SQL_AAD_ADMIN_NAME=$(az ad signed-in-user show --query userPrincipalName -o tsv)
```

Both `.bicepparam` files read the SQL AAD admin from these two environment variables
(`readEnvironmentVariable(...)`) rather than hardcoding an email/object ID into a committed
file.

## 1. Preview (what-if - safe, read-only)

```bash
cd infra

az deployment sub what-if \
  --location eastasia \
  --template-file main.bicep \
  --parameters params/prod.bicepparam

az deployment sub what-if \
  --location eastasia \
  --template-file main.bicep \
  --parameters params/dev.bicepparam
```

## 2. Deploy (not yet run)

```bash
az deployment sub create \
  --location eastasia \
  --template-file main.bicep \
  --parameters params/prod.bicepparam
```

Outputs `apiHostname`, `apiPrincipalId`, `sqlServerFqdn`, `serviceBusFullyQualifiedNamespace`,
`serviceBusTopicName`.

## 3. Grant the App Service's managed identity access to the database

Bicep/ARM can't express this - it's a T-SQL statement that must run as the AAD admin from
step 0, against the live database (unchanged from day-17's runbook; this template doesn't
touch database-level permissions):

```bash
sqlcmd -S <sqlServerFqdn> -d quotesdb -G -N -C <<'EOF'
CREATE USER [syquotes17-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [syquotes17-api];
ALTER ROLE db_datawriter ADD MEMBER [syquotes17-api];
EOF
```

Service Bus access does **not** need an equivalent manual step - `modules/servicebus.bicep`
grants the API's managed identity the **Azure Service Bus Data Owner** role directly via a
`Microsoft.Authorization/roleAssignments` resource, which Bicep/ARM can express (unlike the
SQL database-level grant above, which is data-plane T-SQL, not a control-plane role).

## 4. Verify

Same checks as day-17/deploy.md step 5: live API responds, no connection-string secret in
`az webapp config appsettings list`, and (new for this template) confirm the role assignment
landed:

```bash
az role assignment list --scope $(az servicebus namespace show -g syquotes19-rg -n syquotes19sb --query id -o tsv) -o table
```

should show the API's `principalId` with role `Azure Service Bus Data Owner`.
