#!/bin/sh
# Run every test vehicle and produce one merged coverage report.
# Output: coverage/report/Summary.txt (the gate is 100% line coverage).
set -e
cd "$(dirname "$0")/.."

rm -rf coverage
mkdir -p coverage

dotnet build Skyline.slnx
dotnet test Skyline.slnx --no-build --collect:"XPlat Code Coverage" --results-directory coverage/unit

# The windowed harness needs the main thread (GLFW on macOS), so it runs
# as an instrumented process instead of under a test runner.
dotnet coverlet tests/Skyline.WindowedTests/bin/Debug/net10.0/Skyline.WindowedTests.dll \
  --target dotnet \
  --targetargs "tests/Skyline.WindowedTests/bin/Debug/net10.0/Skyline.WindowedTests.dll" \
  --include "[Skyline]*" --include "[Skyline.Gpu]*" \
  --format cobertura --output coverage/windowed.cobertura.xml

dotnet reportgenerator \
  -reports:"coverage/unit/*/coverage.cobertura.xml;coverage/windowed.cobertura.xml" \
  -targetdir:coverage/report \
  "-reporttypes:TextSummary" \
  "-assemblyfilters:+Skyline;+Skyline.Gpu"

cat coverage/report/Summary.txt
