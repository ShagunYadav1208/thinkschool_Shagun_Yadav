# Deploy runbook (not yet run - see README "Current status")

All of this is written and validated (`az bicep build` compiles cleanly) but
**not executed**: no resource group exists yet, nothing has been pushed to
GitHub. This is the exact sequence to run once given the go-ahead.

## 0. Prerequisites

```bash
az login
az account show   # confirm the "Azure for Students" subscription is active
```

## 1. Provision the resources

```bash
MY_OBJECT_ID=$(az ad signed-in-user show --query id -o tsv)
MY_NAME=$(az ad signed-in-user show --query userPrincipalName -o tsv)

az deployment sub create \
  --location centralindia \
  --template-file infra/main.bicep \
  --parameters namePrefix=syquotes17 \
               sqlAadAdminObjectId="$MY_OBJECT_ID" \
               sqlAadAdminName="$MY_NAME"
```

Outputs `apiHostname`, `apiPrincipalId`, `sqlServerFqdn`, `staticWebAppHostname`
- capture these, steps below use them.

## 2. Fill in the real hostnames

Two files currently carry `REPLACE-AT-DEPLOY` placeholders on purpose (so
nothing that looks like a real secret or endpoint sits in the repo before
it's real):

- `QuotesApi/appsettings.Production.json` - `ConnectionStrings:DefaultConnection`
  (the SQL server FQDN) and `Cors:AllowedOrigin` (the SWA hostname).
- `quotes-list-detail/src/environments/environment.prod.ts` - `apiBaseUrl`
  (the App Service hostname).

Neither of these is a secret - they're hostnames and an auth *mode*
(`Authentication=Active Directory Managed Identity`), not a credential - so
committing the real values afterward is safe.

## 3. Grant the App Service's managed identity access to the database

This is the one step Bicep/ARM genuinely can't express - it's a T-SQL
statement that must run as the AAD admin set in step 1, against the live
database:

```bash
sqlcmd -S <sqlServerFqdn> -d quotesdb -G -N -C <<'EOF'
CREATE USER [syquotes17-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [syquotes17-api];
ALTER ROLE db_datawriter ADD MEMBER [syquotes17-api];
EOF
```

(`-G` = use AAD auth for this sqlcmd session itself, via the currently
`az login`'d identity - which must be the same principal as the AAD admin
from step 1. `[syquotes17-api]` is the App Service's name, which is also its
display name as a database principal once granted.)

## 4. Deploy the code

Frontend, via GitHub Actions (`.github/workflows/azure-static-web-apps.yml`):
add the deployment token as a repo secret first -
```bash
az staticwebapp secrets list --name syquotes17-swa \
  --query properties.apiKey -o tsv
# -> GitHub repo secret AZURE_STATIC_WEB_APPS_API_TOKEN
```

Backend, via GitHub Actions (`.github/workflows/deploy-api.yml`) using OIDC -
no publish-profile secret. One-time setup:

```bash
APP_ID=$(az ad app create --display-name syquotes17-deploy --query appId -o tsv)
az ad sp create --id "$APP_ID"
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "gh-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:thinkbridge-thinkschool/thinkschool-Shagun_Yadav:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
az role assignment create --assignee "$APP_ID" --role "Website Contributor" \
  --scope "$(az webapp show -n syquotes17-api -g syquotes17-rg --query id -o tsv)"
```
Then set repo secrets `AZURE_CLIENT_ID` (=`$APP_ID`), `AZURE_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID` - all non-secret identifiers, no password/certificate
involved; the trust is the federated-credential subject match above.

## 5. Verify (this is the part the exercise actually grades)

- Live SWA URL loads: `curl -I https://<staticWebAppHostname>`
- Lighthouse against the **live** URL (not a local approximation):
  `npx lighthouse https://<staticWebAppHostname> --only-categories=performance,accessibility,best-practices,seo`
- API call carries an MI token, zero secret anywhere:
  `az webapp config appsettings list -n syquotes17-api -g syquotes17-rg` should
  show no connection-string password in any setting; the Kudu/App Service
  logs should show a successful AAD token acquisition on first DB query.
- A deliberately-broken case: temporarily revoke the MI's DB role
  (`ALTER ROLE db_datareader DROP MEMBER [syquotes17-api]`) and confirm the API
  returns a real 5xx (not a silent empty list) - re-grant afterward.
