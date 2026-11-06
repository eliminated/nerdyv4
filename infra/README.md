# Nerdy on Azure

`main.bicep` deploys the API into one resource group:

| Resource | Notes |
|---|---|
| App Service plan (Linux, **B1** by default) + Web App | .NET 10, HTTPS only, Always On, HTTP/2, FTP disabled, system-assigned managed identity |
| Azure SQL logical server + **serverless** database `nerdy` | General Purpose Gen5, 0.5–1 vCore, auto-pause after 60 min, 32 GB. **Microsoft Entra-only authentication** (no SQL logins); the web app connects with its managed identity |
| Key Vault (RBAC) | Holds the JWT signing key and the Google / Microsoft / ACS / Anthropic secrets. App settings reference them (`@Microsoft.KeyVault(...)`); the web app identity gets *Key Vault Secrets User*, the SQL Entra admin (you) *Key Vault Secrets Officer* so you can set secrets yourself |
| Application Insights + Log Analytics | The API sends traces, metrics and logs via OpenTelemetry when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set |
| Email Communication Service + Communication Service (with `enableManagedEmail`) | Azure-managed sender domain (`DoNotReply@….azurecomm.net`); its connection string goes straight into Key Vault |

App settings written by the template: `ASPNETCORE_ENVIRONMENT=Production`, `Database__Provider=SqlServer`,
`Jwt__SigningKey` (Key Vault), `App__PublicBaseUrl`, `APPLICATIONINSIGHTS_CONNECTION_STRING`, and — only when you
pass the matching parameter — `Authentication__Google__*`, `Authentication__Microsoft__*`, `Email__*`. The connection
string `Nerdy` (type SQLAzure) uses `Authentication=Active Directory Managed Identity`.

> Nothing here has been deployed for you. The commands below are for you to run.

## Prerequisites

- Azure CLI (`az version`), signed in with `az login`, and **Owner** (or Contributor + User Access Administrator)
  on the resource group — the template creates a role assignment.
- .NET 10 SDK.

## 1. Deploy the infrastructure

```powershell
az group create --name rg-nerdy --location southeastasia

Copy-Item infra/main.parameters.example.json infra/main.parameters.json   # then edit it
# Your own object id for sqlEntraAdminObjectId (and your UPN for sqlEntraAdminLogin):
az ad signed-in-user show --query "{login:userPrincipalName, id:id}" -o table

# A new random JWT signing key (64 random bytes, base64):
$jwtKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))

az deployment group create `
  --resource-group rg-nerdy `
  --template-file infra/main.bicep `
  --parameters infra/main.parameters.json `
  --parameters jwtSigningKey=$jwtKey
```

(bash: `jwtKey=$(openssl rand -base64 64 | tr -d '\n')`.)

Pass `jwtSigningKey` on the **first** deployment. Later deployments can omit it: secrets are only written when a
value is passed, so the stored ones are kept. Rotating the key signs everyone out for at most 15 minutes (access
tokens); refresh tokens are stored in the database and are unaffected.

Note the outputs (`webAppName`, `webAppUrl`, `sqlServerName`, `sqlServerFqdn`, `keyVaultName`):

```powershell
az deployment group show -g rg-nerdy -n main --query properties.outputs
```

Optional parameters: `appServiceSku`, `sqlAutoPauseDelayMinutes` (min 15), `sqlMaxVCores`, `sqlUseFreeOffer`
(Azure SQL free offer: a monthly amount of free compute and storage; when it's used up the database pauses until the
next month), `enableManagedEmail` (see 6), `publicBaseUrl` (custom domain for email links).

Secrets can be passed as `--parameters <name>=<value>` or, so they never pass through the deployment, written to
Key Vault directly from your own terminal (you're a *Key Vault Secrets Officer* on it):

```powershell
az keyvault secret set --vault-name <keyVaultName> --name <secret-name> --value "<value>"
az webapp restart -g rg-nerdy -n <webAppName>   # picks up the new value
```

Secret names: `google-client-secret`, `microsoft-client-secret`, `anthropic-api-key`, `acs-connection-string`.

## 2. One-time: let the web app into the database

With Entra-only authentication the managed identity must be created as a database user by an Entra admin
(the `sqlEntraAdmin*` you deployed with). Allow your IP through the firewall, then connect to the **nerdy** database
with Entra authentication — Azure portal → the database → *Query editor*, or `sqlcmd -S <sqlServerFqdn> -d nerdy -G`,
or SSMS / VS Code mssql with "Microsoft Entra ID – Universal with MFA":

```powershell
$myIp = (Invoke-RestMethod https://api.ipify.org)
az sql server firewall-rule create -g rg-nerdy -s <sqlServerName> -n my-ip --start-ip-address $myIp --end-ip-address $myIp
```

```sql
CREATE USER [<webAppName>] FROM EXTERNAL PROVIDER;   -- the web app's name is its identity's name
ALTER ROLE db_datareader ADD MEMBER [<webAppName>];
ALTER ROLE db_datawriter ADD MEMBER [<webAppName>];
ALTER ROLE db_ddladmin  ADD MEMBER [<webAppName>];   -- the API applies EF migrations on startup
```

Remove your firewall rule afterwards if you like (`az sql server firewall-rule delete ... -n my-ip`).

## 3. Build and deploy the API

```powershell
dotnet publish src/Nerdy.Api -c Release -o artifacts/api
Compress-Archive -Path artifacts/api/* -DestinationPath artifacts/api.zip -Force
az webapp deploy --resource-group rg-nerdy --name <webAppName> --src-path artifacts/api.zip --type zip
```

On startup the API runs `Database.Migrate()` (creating the schema on the first run) and then serves
`https://<webAppName>.azurewebsites.net`. Check `https://<host>/health` → `Healthy`, and
`az webapp log tail -g rg-nerdy -n <webAppName>` if not. A serverless database that was paused takes up to a minute
to resume; the API retries transient SQL errors, so the first request after a pause is just slower.

To create a migration after model changes: `dotnet ef migrations add <Name> --project src/Nerdy.Api --output-dir Data/Migrations`
(the tool is in `.config/dotnet-tools.json`; run `dotnet tool restore` once).

## 4. Microsoft sign-in (personal + work/school accounts)

1. [Microsoft Entra admin center](https://entra.microsoft.com) → *App registrations* → *New registration*.
2. Name `Nerdy`; **Supported account types**: *Accounts in any organizational directory (any Microsoft Entra ID
   tenant – multitenant) and personal Microsoft accounts (e.g. Skype, Xbox)*.
3. **Redirect URI**: platform *Web*, `https://<host>/signin-microsoft`. Add `http://localhost:5080/signin-microsoft`
   too if you want to test locally.
4. *Certificates & secrets* → *New client secret* → copy the **Value** (shown once).
5. *API permissions*: the default delegated *Microsoft Graph → User.Read* is all the API needs.
6. Redeploy with `microsoftClientId=<Application (client) ID>` in the parameters file and
   `--parameters microsoftClientSecret=<value>`.

The API uses the `common` endpoint. It identifies the account by its user principal name, not Graph's editable
`mail` attribute, before linking it to an existing Nerdy account with the same email.

## 5. Google sign-in

1. [Google Cloud console](https://console.cloud.google.com) → *APIs & Services* → *OAuth consent screen*: user type
   *External*, scopes `openid`, `email`, `profile`; publish the app when ready (test mode only allows listed users).
2. *Credentials* → *Create credentials* → *OAuth client ID* → application type **Web application**.
3. **Authorized redirect URIs**: `https://<host>/signin-google` (and `http://localhost:5080/signin-google` for local).
4. Redeploy with `googleClientId=<client id>` and `--parameters googleClientSecret=<secret>`.

The providers only ever redirect to the API. The desktop app's own loopback redirect
(`http://127.0.0.1:{port}/…`) is validated by the API and is never registered with Microsoft or Google.

For local testing, keep the ids and secrets out of files with user secrets:

```powershell
dotnet user-secrets set "Authentication:Google:ClientId" "<id>" --project src/Nerdy.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<secret>" --project src/Nerdy.Api
```

## 6. Email (optional)

Without email configuration the API saves messages to `App_Data/emails` on the server and logs a warning. To send
confirmation and password-reset emails, deploy with `enableManagedEmail=true`: the template creates an *Email
Communication Service* with an Azure-managed `*.azurecomm.net` domain, links it to a *Communication Service*, stores
its connection string in Key Vault and sets the sender to `DoNotReply@<that domain>` (output `emailSender`).
Azure-managed domains have low sending limits, which suit a beta; for more, add your own domain to the email service.

To use your own Communication Service instead, deploy with `emailFrom=DoNotReply@<your-domain>` and
`--parameters acsConnectionString="<Communication Service connection string>"`.

## 7. AI review of Ultra-Focus exits (optional)

When a student leaves Ultra-Focus early for an "urgent school or work matter" or "something else", the API can ask
Claude (Anthropic) whether it can wait until the session ends. Medical and family emergencies are never reviewed,
and every failure (no key, timeout, rate limit, refusal) allows the exit. Without a key every exit is allowed.

**Recommended: workload identity federation (no stored key).** The web app's managed identity gets a Microsoft
Entra token and Anthropic exchanges it for a short-lived access token (see Anthropic's
[Use WIF with Microsoft Entra ID](https://platform.claude.com/docs/en/manage-claude/wif-providers/azure)).

1. Register the token audience once per tenant (an app registration with v2.0 tokens and a service principal):

   ```powershell
   $appId = az ad app create --display-name "Nerdy Claude federation" --query appId -o tsv
   $objId = az ad app show --id $appId --query id -o tsv
   '{"identifierUris":["api://' + $appId + '"],"api":{"requestedAccessTokenVersion":2}}' | Set-Content audience.json
   az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$objId" --headers "Content-Type=application/json" --body "@audience.json"
   az ad sp create --id $appId
   ```

2. Claude Console → *Settings → Workload identity*:
   - **Issuer**: issuer URL `https://login.microsoftonline.com/<tenant id>/v2.0` (the v2.0 issuer, not
     `sts.windows.net`), single-use tokens **off** (managed identity tokens carry no `jti`), and maximum token lifetime
     **90000** seconds. Managed identity tokens are valid for 24 hours and Entra backdates their `iat` by 5 minutes,
     so `exp − iat` is 86700 s; a limit of 86400 or less rejects them with a bare `401 Authentication failed`.
   - **Rule** (pattern match): *Expected audience* = `$appId`, the bare GUID (the token's `aud`, no `api://`);
     *Subject pattern* = the web app's `webAppPrincipalId` output (a managed identity's `sub` is its object id; replace
     the `system:serviceaccount:…` example); additional claim `tid` = your tenant id; target = a service account in a
     single workspace.
   - When an exchange is rejected, *Workload identity → Authentication events* names the failed check, and the API
     logs the presented token's `aud`, `iss`, `oid`, `tid` and lifetime (never the token) next to the error.
3. Redeploy with `enableExitReview=true`, `claudeFederationAudience=api://<appId>`, and the wizard's
   `claudeFederationRuleId` (`fdrl_…`), `claudeOrganizationId`, `claudeServiceAccountId` (`svac_…`) and
   `claudeWorkspaceId` (`wrkspc_…`). None of these are secrets.

**Alternatively, an API key:** create one in the Claude Console scoped to a single workspace (keys for several
workspaces need a workspace header the API doesn't send), then redeploy with `enableExitReview=true` and store the
key as the `anthropic-api-key` secret. Later deployments keep the stored key when `anthropicApiKey` is omitted.
Optional app settings: `ExitReview__Model` (default `claude-haiku-5-5`), `ExitReview__TimeoutSeconds` (8) and
`ExitReview__PerUserPerHour` (20 reviews per person per hour; beyond that exits are allowed unreviewed).

For local testing:

```bash
dotnet user-secrets set "ExitReview:ApiKey" "<key>" --project src/Nerdy.Api
```

## Troubleshooting

- **Key Vault references not resolved** (portal → web app → *Environment variables* shows a red status): the role
  assignment can take a few minutes to apply. Restart the web app. The API refuses to start if `Jwt__SigningKey`
  is still the literal reference text.
- **Login failed for user '<token-identified principal>'**: step 2 has not been run against the `nerdy` database.
- **Costs**: B1 plan runs continuously; the database only bills compute while active (storage always). App Service
  health checks are deliberately not enabled because they would keep the database from pausing.
- **Network**: the SQL server allows Azure services (`0.0.0.0` rule). For stricter isolation, add VNet integration
  and a private endpoint, then disable public network access.
