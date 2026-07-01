# LEGACY manual publish path for the JuegoFramework.Templates package.
# Requires a long-lived $NUGET_API_KEY (the pattern we moved away from for the
# core library). JuegoFramework itself now publishes keylessly via GitHub Actions
# trusted publishing (.github/workflows/publish.yml) — see README "Releasing".
# TODO: migrate Templates to its own trusted-publishing workflow + nuget.org policy
# (e.g. publish-templates.yml on a `templates-v*` tag) and delete this script.
cd JuegoFrameworkTemplates
rm -f bin/Release/*.nupkg
dotnet pack -c Release
dotnet nuget push "bin/Release/*.nupkg" --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json
# dotnet new uninstall JuegoFramework.Templates
# dotnet new install bin/Release/*.nupkg
