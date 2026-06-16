# StarlingMock on iOS

A mock Starling browser as an iPhone app. The chrome — address bar,
keyboard, gestures — is native UIKit. The page below it is Skyline.Gpu,
built the way the Starling plan describes: content rendered to offscreen
tiles, tiles composited to the screen, pixels never leaving the GPU.

What it does:

- The page is a column of strip textures (`CreateColorTarget`). A
  generative shader draws a page wireframe into each strip — headings,
  text lines, image blocks — seeded by the URL.
- A composite pipeline samples the strips onto the swapchain at the
  scroll offset. Only visible strips render.
- Type a URL and hit Go to "navigate". A new seed, a new page. Same
  address always gives the same page.
- Pan to scroll, with a fling. The strips are GPU textures, so scrolling
  is just compositing at a new offset.

The wgpu side shows both halves of Skyline.Gpu's design: helpers where
they save boilerplate (`CreateShaderModuleWgsl`, `CreatePipeline`,
`CreateBindGroup`, `CreateColorTarget`) and raw wgpu where the app owns
the decisions (bind group layouts, render passes, uniform writes).

## Run on the simulator

Same setup as `HelloClear.Ios` — see its README for the one-time steps
(`sudo dotnet workload install ios`, then `../wgpu-ios/build-wgpu.sh`).
Then:

```sh
dotnet build -t:Run -p:_IsPublishing=true -p:ValidateXcodeVersion=false \
  -p:_DeviceName=:v2:udid=<simulator-udid>
```

To launch at a specific page from the command line:

```sh
SIMCTL_CHILD_STARLING_URL="starling://docs/frame-pacing" \
  xcrun simctl launch <simulator-udid> dev.starling.mockbrowser
```

Launch arguments do not reach `Environment.GetCommandLineArgs()` in a
.NET iOS app, so the start URL rides an environment variable instead.
