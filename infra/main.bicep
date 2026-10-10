// Nerdy API on Azure: Linux App Service (.NET 10) + Azure SQL serverless (Entra-only auth, managed identity)
// + Key Vault (RBAC) for secrets referenced from app settings + Application Insights.
// Deploy into an existing resource group; see infra/README.md.

targetScope = 'resourceGroup'

@description('Short lowercase prefix for resource names.')
@minLength(3)
@maxLength(12)
param appName string = 'nerdy'

param location string = resourceGroup().location

@description('App Service plan SKU (Linux). B1 is the cheapest tier with Always On.')
@allowed(['B1', 'B2', 'B3', 'S1', 'P0v3', 'P1v3'])
param appServiceSku string = 'B1'

@description('Microsoft Entra admin of the SQL server: a user principal name or group display name.')
param sqlEntraAdminLogin string

@description('Object id of that user or group (az ad signed-in-user show --query id -o tsv).')
param sqlEntraAdminObjectId string

@allowed(['User', 'Group', 'Application'])
param sqlEntraAdminPrincipalType string = 'User'

@description('Minutes without connections before the serverless database pauses (compute is then free).')
@minValue(15)
param sqlAutoPauseDelayMinutes int = 60

@description('Maximum vCores of the serverless database.')
@allowed([1, 2, 4])
param sqlMaxVCores int = 1

@description('Use the Azure SQL free offer (one database per subscription; pauses when the monthly free amount is used up).')
param sqlUseFreeOffer bool = false

@description('JWT signing key (>= 32 bytes). Required on the first deployment; leave empty later to keep the stored secret.')
@secure()
param jwtSigningKey string = ''

@description('Google OAuth client id; empty disables Google sign-in.')
param googleClientId string = ''

@description('Google OAuth client secret. Empty keeps the stored secret.')
@secure()
param googleClientSecret string = ''

@description('Microsoft (Entra app registration) client id; empty disables Microsoft sign-in.')
param microsoftClientId string = ''

@description('Microsoft client secret. Empty keeps the stored secret.')
@secure()
param microsoftClientSecret string = ''

@description('Sender address on a domain verified in Azure Communication Services; empty saves emails to App_Data instead of sending them.')
param emailFrom string = ''

@description('Azure Communication Services connection string. Empty keeps the stored secret.')
@secure()
param acsConnectionString string = ''

@description('Public origin used in email links, e.g. a custom domain. Defaults to the web app\'s azurewebsites.net URL.')
param publicBaseUrl string = ''

@description('Turns on the AI review of non-emergency Ultra-Focus exits (needs the Anthropic API key below). Off: every exit is allowed.')
param enableExitReview bool = false

@description('Anthropic API key for the exit review. Empty keeps the stored secret.')
@secure()
param anthropicApiKey string = ''

var suffix = uniqueString(resourceGroup().id)
var webAppName = '${appName}-api-${suffix}'
var keyVaultName = take('${appName}kv${suffix}', 24)
var databaseName = 'nerdy'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// Secret names in Key Vault, referenced from app settings below.
var jwtSecretName = 'jwt-signing-key'
var googleSecretName = 'google-client-secret'
var microsoftSecretName = 'microsoft-client-secret'
var acsSecretName = 'acs-connection-string'
var anthropicSecretName = 'anthropic-api-key'

// ---------------------------------------------------------------- Monitoring

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-insights-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------- Key Vault

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
  }
}

// Secrets are written only when a value is passed, so later deployments don't need them again.
resource jwtSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(jwtSigningKey)) {
  parent: keyVault
  name: jwtSecretName
  properties: { value: jwtSigningKey }
}

resource googleSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(googleClientSecret)) {
  parent: keyVault
  name: googleSecretName
  properties: { value: googleClientSecret }
}

resource microsoftSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(microsoftClientSecret)) {
  parent: keyVault
  name: microsoftSecretName
  properties: { value: microsoftClientSecret }
}

resource acsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(acsConnectionString)) {
  parent: keyVault
  name: acsSecretName
  properties: { value: acsConnectionString }
}

resource anthropicSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(anthropicApiKey)) {
  parent: keyVault
  name: anthropicSecretName
  properties: { value: anthropicApiKey }
}

// ---------------------------------------------------------------- Azure SQL

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: '${appName}-sql-${suffix}'
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // Microsoft Entra only: no SQL logins or passwords exist. The web app connects with its managed identity.
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlEntraAdminLogin
      sid: sqlEntraAdminObjectId
      principalType: sqlEntraAdminPrincipalType
      tenantId: tenant().tenantId
    }
  }
}

// 0.0.0.0 is the special rule "allow Azure services", which lets App Service reach the server.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

var databaseProperties = {
  autoPauseDelay: sqlAutoPauseDelayMinutes
  minCapacity: json('0.5')
  maxSizeBytes: 34359738368 // 32 GB
  zoneRedundant: false
  requestedBackupStorageRedundancy: 'Local'
}

var freeOfferProperties = sqlUseFreeOffer ? {
  useFreeLimit: true
  freeLimitExhaustionBehavior: 'AutoPause'
} : {}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5' // Serverless
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: sqlMaxVCores
  }
  properties: union(databaseProperties, freeOfferProperties)
}

// ---------------------------------------------------------------- App Service

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${appName}-plan-${suffix}'
  location: location
  kind: 'linux'
  sku: { name: appServiceSku }
  properties: {
    reserved: true // Linux
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    keyVaultReferenceIdentity: 'SystemAssigned'
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet Nerdy.Api.dll'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      // No healthCheckPath on purpose: probing /health every minute would keep the serverless database awake.
    }
  }
}

// The web app's identity may read secrets (needed to resolve the Key Vault references below).
resource webAppSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, webApp.id, keyVaultSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

var keyVaultReference = '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName='

var baseSettings = {
  ASPNETCORE_ENVIRONMENT: 'Production'
  SCM_DO_BUILD_DURING_DEPLOYMENT: 'false' // We deploy the output of dotnet publish.
  APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
  Database__Provider: 'SqlServer'
  Jwt__SigningKey: '${keyVaultReference}${jwtSecretName})'
  App__PublicBaseUrl: empty(publicBaseUrl) ? 'https://${webApp.properties.defaultHostName}' : publicBaseUrl
}

// Each optional feature is configured only when enabled, so the API never sees a reference to a missing secret.
var googleSettings = empty(googleClientId) ? {} : {
  Authentication__Google__ClientId: googleClientId
  Authentication__Google__ClientSecret: '${keyVaultReference}${googleSecretName})'
}

var microsoftSettings = empty(microsoftClientId) ? {} : {
  Authentication__Microsoft__ClientId: microsoftClientId
  Authentication__Microsoft__ClientSecret: '${keyVaultReference}${microsoftSecretName})'
}

var emailSettings = empty(emailFrom) ? {} : {
  Email__From: emailFrom
  Email__AcsConnectionString: '${keyVaultReference}${acsSecretName})'
}

var exitReviewSettings = !enableExitReview ? {} : {
  ExitReview__ApiKey: '${keyVaultReference}${anthropicSecretName})'
}

resource appSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: webApp
  name: 'appsettings'
  properties: union(baseSettings, googleSettings, microsoftSettings, emailSettings, exitReviewSettings)
  dependsOn: [
    webAppSecretsUser
    jwtSecret
    googleSecret
    microsoftSecret
    acsSecret
    anthropicSecret
  ]
}

// Becomes the SQLAZURECONNSTR_Nerdy environment variable, which ASP.NET Core maps to ConnectionStrings:Nerdy.
resource connectionStrings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: webApp
  name: 'connectionstrings'
  properties: {
    Nerdy: {
      type: 'SQLAzure'
      value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${database.name};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
    }
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output webAppPrincipalId string = webApp.identity.principalId
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
output keyVaultName string = keyVault.name
