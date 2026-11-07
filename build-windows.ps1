$ErrorActionPreference = "Stop"
dotnet restore .\NodeScope.sln
dotnet publish .\src\NodeScope\NodeScope.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\artifacts\win-x64
Write-Host "Built: .\artifacts\win-x64\NodeScope.exe"
