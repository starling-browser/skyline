#!/bin/sh
# Verify formatting and code style against .editorconfig: braces, SPDX headers,
# and layout. Fails if any file in the solution would change. To auto-fix, run
#   dotnet format Skyline.slnx
# Scope matches the coverage gate (Skyline.slnx), so Skyline.Apple, which sits
# outside the solution, is checked on Apple hardware rather than here.
set -e
cd "$(dirname "$0")/.."
dotnet format Skyline.slnx --verify-no-changes
