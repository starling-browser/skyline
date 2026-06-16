# HelloClear on iOS

Skyline.Gpu running as an iPhone app. wgpu draws an animated clear color
into a `CAMetalLayer`, and the app is compiled ahead of time with
NativeAOT.

`Skyline.Gpu` runs unchanged. The pieces around it swap out:

- The window host (`src/Skyline`) is GLFW, which does not exist on iOS.
  A `UIView` backed by a `CAMetalLayer` plays the window.
- `FrameLoop` stays behind with it. A `CADisplayLink` paces the frames
  instead.
- The app hands `GpuContext.Create` its own `WebGPU` object and a
  surface factory for the metal layer. That overload exists for exactly
  this case.

Two iOS wrinkles, both handled by `../wgpu-ios/build-wgpu.sh` (shared
by every iOS sample):

- Silk.NET ships no iOS wgpu binary, and no wgpu-native release matches
  the commit its bindings were generated from. The script builds that
  exact commit from source (it needs a Rust toolchain from rustup.rs).
  The build links the result into the app executable.
- The iOS link only exports symbols it is told to keep. The script
  writes `WgpuSymbols.items` so the app can resolve the wgpu functions
  at runtime.

## Run on the simulator

One-time setup. The workload install needs admin rights:

```sh
sudo dotnet workload install ios
../wgpu-ios/build-wgpu.sh
```

Then build and run. NativeAOT normally engages only under
`dotnet publish`, but the iOS SDK refuses to publish for a simulator.
`_IsPublishing=true` flips the same switch from `dotnet build`:

```sh
dotnet build -t:Run -p:_IsPublishing=true -p:ValidateXcodeVersion=false \
  -p:_DeviceName=:v2:udid=<simulator-udid>
```

Get a simulator UDID from `xcrun simctl list devices available`.
`ValidateXcodeVersion=false` is needed when your Xcode is newer than
the one the workload pins.

## Run on a device

Pass `-r ios-arm64` and a signing identity. A free Apple ID "Personal
Team" profile works. Not wired up in this sample yet.

This project is not in `Skyline.slnx` on purpose. It needs the iOS
workload, and the solution must build on machines without it.
