#!/bin/bash
set -e

echo "=== Merging Coverage Reports ==="

# Install reportgenerator if not present
dotnet tool install -g dotnet-reportgenerator-globaltool 2>/dev/null || true
export PATH="$PATH:$HOME/.dotnet/tools"

# Find all coverage files
COVERAGE_FILES=$(find TestResults -name "coverage.cobertura.xml" 2>/dev/null | tr '\n' ';')

if [ -z "$COVERAGE_FILES" ]; then
    echo "No coverage files found"
    exit 0
fi

echo "Found coverage files: $COVERAGE_FILES"

# Generate merged report
reportgenerator \
    -reports:"$COVERAGE_FILES" \
    -targetdir:"TestResults/Coverage/Report" \
    -reporttypes:"Html;Cobertura;TextSummary"

# Copy merged cobertura for downstream consumption
cp TestResults/Coverage/Report/Cobertura.xml TestResults/Coverage/coverage.merged.cobertura.xml 2>/dev/null || true

echo "=== Coverage Merge Complete ==="

# Print summary if available
if [ -f "TestResults/Coverage/Report/Summary.txt" ]; then
    echo ""
    cat TestResults/Coverage/Report/Summary.txt
fi
