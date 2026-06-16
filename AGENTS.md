# Agents — rules of engagement

Read this first when working in Skyline. Skyline is a WebGPU-first window and
GPU library for .NET. For what the library does, see [README.md](README.md). For
how it is put together and the rules each project owns, see
[ARCHITECTURE.md](ARCHITECTURE.md).

## Build + test (must be green before merge)

```sh
dotnet build
./tools/format-check.sh   # check formatting and style against .editorconfig
./tools/cover.sh          # runs all three test vehicles and merges the report
```

Run `dotnet format Skyline.slnx` to auto-fix the style nits the check finds
(braces, missing SPDX headers, layout).

The test vehicles:

- `tests/Skyline.Tests` and `tests/Skyline.Gpu.Tests` — MSTest, headless. The
  GPU tests run against a real device with no window.
- `tests/Skyline.WindowedTests` — a console harness for checks that need a real
  window. GLFW wants the main thread on macOS, so these run as a plain program
  with an exit code, not under a test runner.

Both libraries are at 100% line coverage. The only excluded lines are the
native-failure guards in `src/Skyline.Gpu/Guard.cs`, documented there.

`src/Skyline.Apple` is the native macOS windowing backend (AppKit). It
targets `net*-macos`, needs the `macos` workload, and builds only on a
Mac, so it is **not** in `Skyline.slnx` or the coverage gate — the same
way the iOS samples sit outside the core build. It is checked on Apple
hardware. Its testable logic (chrome tables, backend choice, surface
dispatch) lives in the covered core and is reachable through an
injectable seam, so adding it did not lower the gate.

## Coding standards

Skyline targets WebGPU and mirrors its surface: helpers fill WebGPU's own
descriptors with overridable defaults and take and return raw handles. Keep that
shape — do not wrap wgpu in a renderer abstraction. See ARCHITECTURE.md.

Target modern .NET and prefer simple, allocation-conscious code. The render loop
and `FramePacer` are hot — one native call per frame, zero allocation. Keep new
per-frame code to that bar.

**Comments and doc comments:** Use them sparingly. A comment should explain a
decision local to the scope of the code when that decision isn't clear from the
code itself. Skip comments that restate what the code already says.

**Keep comments local.** A comment explains the code right next to it. Do not
send the reader three layers up or into another system. Skip lines like "Refer
to the LayerComponentCompositor to see how it uses this." If a fact belongs to
that other place, write it there.

**Do not narrate a problem you hit.** A comment is not a changelog. Skip lines
like "Fixes the issue where you wanted layers to be unique." Say what is true now
and why it matters here. "Each layer needs a unique id" is fine. The story of the
bug is not.

**Braces:** Always use braces for conditionals, loops, and other block
statements, even when the body is a single line.

Add `// SPDX-License-Identifier: Apache-2.0` to new owned C# files.
