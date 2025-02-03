cd Templates
rm -f bin/Release/*.nupkg
dotnet new uninstall JuegoFramework.Templates 
dotnet pack
dotnet new install bin/Release/*.nupkg 
# dotnet nuget push "bin/Release/*.nupkg" --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json
