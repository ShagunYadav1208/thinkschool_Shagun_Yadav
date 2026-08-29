// Static Web App, Free tier, default *.azurestaticapps.net hostname - no
// custom domain (piece1's brief answer, see README). Deployment content comes
// from GitHub Actions (.github/workflows/azure-static-web-apps.yml) using the
// deployment token this resource issues, not from a linked-repo config here -
// keeps this template free of any GitHub PAT/App installation details.

param location string
param staticWebAppName string

resource swa 'Microsoft.Web/staticSites@2024-04-01' = {
  name: staticWebAppName
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

output defaultHostname string = swa.properties.defaultHostname
