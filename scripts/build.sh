#!/bin/bash
set -e

echo "=== Building Sqlzibar ==="

dotnet restore src/Sqlzibar/Sqlzibar.csproj
dotnet restore tests/Sqlzibar.Tests/Sqlzibar.Tests.csproj
dotnet restore tests/Sqlzibar.IntegrationTests/Sqlzibar.IntegrationTests.csproj
dotnet restore tests/Sqlzibar.IntegrationTests.AppHost/Sqlzibar.IntegrationTests.AppHost.csproj
dotnet restore examples/Sqlzibar.Example.Api/Sqlzibar.Example.Api.csproj
dotnet restore examples/Sqlzibar.Example.Tests/Sqlzibar.Example.Tests.csproj
dotnet restore examples/Sqlzibar.Example.IntegrationTests/Sqlzibar.Example.IntegrationTests.csproj

dotnet build src/Sqlzibar/Sqlzibar.csproj --configuration Release --no-restore
dotnet build tests/Sqlzibar.Tests/Sqlzibar.Tests.csproj --configuration Release --no-restore
dotnet build tests/Sqlzibar.IntegrationTests/Sqlzibar.IntegrationTests.csproj --configuration Release --no-restore
dotnet build tests/Sqlzibar.IntegrationTests.AppHost/Sqlzibar.IntegrationTests.AppHost.csproj --configuration Release --no-restore
dotnet build examples/Sqlzibar.Example.Api/Sqlzibar.Example.Api.csproj --configuration Release --no-restore
dotnet build examples/Sqlzibar.Example.Tests/Sqlzibar.Example.Tests.csproj --configuration Release --no-restore
dotnet build examples/Sqlzibar.Example.IntegrationTests/Sqlzibar.Example.IntegrationTests.csproj --configuration Release --no-restore

echo "=== Build Complete ==="
