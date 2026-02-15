#!/bin/bash
set -e

echo "=== Running Integration Tests ==="

mkdir -p TestResults/Integration

dotnet test tests/Sqlzibar.IntegrationTests/Sqlzibar.IntegrationTests.csproj \
    --configuration Release \
    --no-build \
    --collect:"XPlat Code Coverage" \
    --settings tests/coverlet.runsettings \
    --results-directory TestResults/Integration \
    --logger "console;verbosity=normal" \
    --logger "trx;LogFileName=IntegrationTests.trx"

dotnet test examples/Sqlzibar.Example.IntegrationTests/Sqlzibar.Example.IntegrationTests.csproj \
    --configuration Release \
    --no-build \
    --collect:"XPlat Code Coverage" \
    --settings tests/coverlet.runsettings \
    --results-directory TestResults/Integration \
    --logger "console;verbosity=normal" \
    --logger "trx;LogFileName=ExampleIntegrationTests.trx"

echo "=== Integration Tests Complete ==="
