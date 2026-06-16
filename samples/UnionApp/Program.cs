using Skyline;
using Skyline.Render;
using UnionApp;

// A Skyline app written rust-style. App.Window(...).Run() hands back a union:
// Ok(frames) on a clean exit, or Err(exception) if a draw call threw. We
// branch on it the way Rust branches on Result — the same shape as a Tauri
// main that ends in `if let Err(error) = result { ... }`.
//
// By default the window draws a few calm frames and exits Ok. Pass
// --throw-on N to throw while drawing frame N, which comes back as Err and
// exits non-zero. Escape quits early.

var throwOn = 0;
var argIdx = Array.IndexOf(args, "--throw-on");
if (argIdx >= 0 && argIdx + 1 < args.Length)
{
    _ = int.TryParse(args[argIdx + 1], out throwOn);
}

var drawn = 0;

var result = App
    .Window(new AppWindowOptions { Title = "Skyline — union app", Width = 640, Height = 400 })
    .Clear(0.10, 0.12, 0.16)
    .ForFrames(30)
    .OnRender((in Frame _) =>
    {
        drawn++;
        if (throwOn > 0 && drawn >= throwOn)
        {
            throw new InvalidOperationException($"deliberate failure while drawing frame {drawn}");
        }
    })
    .Run();

return result switch
{
    Ok ok => Done(ok),
    Err error => Fail(error),
    _ => Fail(new Err(new InvalidOperationException("run produced no result"))),
};

static int Done(Ok ok)
{
    Console.WriteLine($"UNION APP OK: presented {ok.Frames} frames, then closed clean");
    return 0;
}

static int Fail(Err error)
{
    Console.Error.WriteLine($"Skyline app failed: {error.Error.Message}");
    return 1;
}
