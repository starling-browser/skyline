using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;
using Skyline.Interaction;
using Skyline.Interaction.Ui;
using Skyline.Render;

// The Skyline interaction tier's approvals overlay, composited on top of an
// app frame. Every prompt says who is asking, what they want, and how long you
// have to answer: a kind-colored badge and name, the capability in plain words,
// the request detail, and a countdown that runs green to red as the deadline
// nears. Allow / Allow once / Deny, answered with the mouse. Each answer flashes
// a toast — ALLOWED, ALLOWED ONCE, DENIED.
//
//   A  an AI wants to type            (Edit, cyan)
//   K  an automation wants the keyboard (LowLevelInput, amber)
//   C  you request control            (Administer, green)
//   Esc quit
//
// Pass --frames N to render continuously and auto-answer the first prompt,
// then close after N presented frames (smoke test).

var maxFrames = 0;
var argIdx = Array.IndexOf(args, "--frames");
if (argIdx >= 0 && argIdx + 1 < args.Length)
{
    _ = int.TryParse(args[argIdx + 1], out maxFrames);
}
var auto = maxFrames > 0;

using var win = new AppWindow(new AppWindowOptions { Title = "Skyline - approvals", Width = 720, Height = 520 });

// BeginClearPass = false: this app owns the encoder, clears the background
// itself, and lets the overlay draw on top through a LoadOp.Load pass.
using var loop = FrameLoop.Attach(win, new FrameLoopOptions
{
    BeginClearPass = false,
    // Render every frame so the countdown, toast slides, and the live-grant
    // pulse animate. The overlay only builds geometry while it has something
    // to show, so an idle window just re-clears its background.
    Continuous = true,
});

using var overlay = new ApprovalsOverlay(loop.Gpu, loop.Surface.Format, requestRedraw: loop.RequestRedraw);

// The decision model: policy + the overlay as the approvals UI.
var shell = new InProcessApprovalShell(overlay);

// The desktop input bridge: it mints the local person and routes pointer-downs
// to the overlay, and wraps the window clipboard behind the transfer seam.
var source = new AppWindowInteractionSource(overlay, new AppWindowClipboard(win));
var handler = new CallbackAppWindowHandler { PointerInput = (_, e) => source.OnPointerEvent(e) };
loop.Handler = handler;

var planner = new Actor("planner", "Planner", ActorKind.Ai, ActorLocality.Local);
var formFiller = new Actor("form-filler", "Form Filler", ActorKind.Automation, ActorLocality.Local);

async Task Authorize(Actor actor, InteractionCapability capability, string prompt)
{
    var grant = await shell.AuthorizeAsync(actor, capability, prompt);
    Console.WriteLine(grant is null
        ? $"  DENIED  {actor.DisplayName} / {capability}"
        : $"  GRANTED {actor.DisplayName} / {capability}");
}

handler.KeyInput = async (_, e) =>
{
    if (!e.IsDown)
    {
        return;
    }
    switch (e.Key)
    {
        case Key.A:
            await Authorize(planner, InteractionCapability.Edit, "Fill the address bar");
            break;
        case Key.K:
            await Authorize(formFiller, InteractionCapability.LowLevelInput, "Capture every keystroke");
            break;
        case Key.C:
            await Authorize(source.LocalHuman, InteractionCapability.Administer, "Take control of this window");
            break;
        case Key.Escape:
            win.RequestClose();
            break;
    }
};

Console.WriteLine("Approvals: A = AI types, K = automation grabs keyboard, C = you take control, Esc = quit");

// Show one prompt at startup so there is something to answer. Do NOT await:
// the task completes only once the overlay is answered, and the overlay can
// only be answered after win.Run() below starts pumping events. Awaiting here
// would suspend Main before the loop starts and deadlock. Discarding the Task
// (rather than async void) still observes faults via the returned Task.
_ = Authorize(planner, InteractionCapability.Edit, "Fill the address bar");

var presented = 0;
var autoAnswered = false;

loop.OnRender = (in Frame frame) =>
{
    var background = new Color { R = 0.10, G = 0.12, B = 0.16, A = 1.0 };
    unsafe
    {
        var wgpu = loop.Gpu.Api;
        var attachment = new RenderPassColorAttachment
        {
            View = frame.View,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = background,
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
        var pass = wgpu.CommandEncoderBeginRenderPass(frame.Encoder, in passDesc);
        wgpu.RenderPassEncoderEnd(pass);
        wgpu.RenderPassEncoderRelease(pass);

        // The overlay opens its own load pass and draws the modal and indicator.
        overlay.Encode(frame.View, frame.Encoder, frame.Info);
    }

    presented++;

    // Smoke path: answer the startup prompt by clicking Allow, then close.
    if (auto && !autoAnswered && presented >= 3 && overlay.State.Snapshot.HasModal)
    {
        var layout = overlay.State.Layout(frame.Info.LogicalWidth, frame.Info.LogicalHeight);
        overlay.OnPointerDown(layout.Allow.X + layout.Allow.Width / 2f, layout.Allow.Y + layout.Allow.Height / 2f);
        autoAnswered = true;
    }
    if (auto && presented >= maxFrames)
    {
        win.RequestClose();
    }
};

loop.RequestRedraw(); // draw the first frame (and the startup prompt)

var code = win.Run();
Console.WriteLine($"APPROVALS OK: presented {presented} frames");
return presented > 0 ? code : 1;
