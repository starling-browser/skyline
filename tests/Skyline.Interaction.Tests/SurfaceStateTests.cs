namespace Skyline.Interaction.Tests;

[TestClass]
public class SurfaceStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);

    private static ApprovalRequest Req(string id, DateTimeOffset? expires = null) =>
        new(id, Actors.Planner, InteractionCapability.Edit, $"prompt {id}", expires, true);

    private static (ApprovalSurfaceState state, ManualTimeProvider time, Func<int> redraws) NewState()
    {
        var time = new ManualTimeProvider(Start);
        var count = 0;
        var state = new ApprovalSurfaceState(time, () => count++);
        return (state, time, () => count);
    }

    [TestMethod]
    public async Task Allow_ResolvesTheTaskAndPrunesThePending()
    {
        var (state, _, redraws) = NewState();
        var task = state.RequestAsync(Req("r1"));
        Assert.IsTrue(state.Snapshot.HasModal);

        Assert.IsTrue(state.Resolve("r1", ApprovalDecision.Allow(Start)));
        var decision = await task;
        Assert.AreEqual(ApprovalVerb.Allow, decision.Verb);
        Assert.IsFalse(state.Snapshot.HasModal);
        Assert.IsTrue(redraws() >= 2, "request and resolve each ask for a redraw");
    }

    [TestMethod]
    public void Pending_SurfacesFirstInFirstOut()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        _ = state.RequestAsync(Req("r2"));
        Assert.AreEqual("r1", state.Snapshot.Pending[0].Request.Id);

        state.Resolve("r1", ApprovalDecision.Deny(Start));
        Assert.AreEqual("r2", state.Snapshot.Pending[0].Request.Id);
    }

    [TestMethod]
    public async Task Deadline_DeniesAnUnansweredRequest()
    {
        var (state, time, _) = NewState();
        var task = state.RequestAsync(Req("r1", Start + TimeSpan.FromSeconds(30)));
        time.Advance(TimeSpan.FromSeconds(31));
        var decision = await task;
        Assert.AreEqual(ApprovalVerb.Deny, decision.Verb);
        Assert.IsFalse(state.Snapshot.HasModal);
    }

    [TestMethod]
    public async Task PastDeadline_DeniesImmediatelyOnAdvance()
    {
        var (state, time, _) = NewState();
        var task = state.RequestAsync(Req("r1", Start - TimeSpan.FromSeconds(5)));
        time.Advance(TimeSpan.Zero);
        Assert.AreEqual(ApprovalVerb.Deny, (await task).Verb);
    }

    [TestMethod]
    public async Task Cancellation_DeniesTheRequest()
    {
        var (state, _, _) = NewState();
        using var cts = new CancellationTokenSource();
        var task = state.RequestAsync(Req("r1"), cts.Token);
        cts.Cancel();
        Assert.AreEqual(ApprovalVerb.Deny, (await task).Verb);
    }

    [TestMethod]
    public void DoubleResolve_IsRejected()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        Assert.IsTrue(state.Resolve("r1", ApprovalDecision.Allow(Start)));
        Assert.IsFalse(state.Resolve("r1", ApprovalDecision.Deny(Start)), "already answered");
        Assert.IsFalse(state.Resolve("nope", ApprovalDecision.Deny(Start)), "unknown id");
    }

    [TestMethod]
    public void LiveGrant_DrivesTheIndicator()
    {
        var (state, _, _) = NewState();
        Assert.IsFalse(state.Snapshot.IndicatorActive);
        var grant = new CapabilityGrant("g1", Actors.Planner, Actors.Os, InteractionCapability.Edit, null, true);
        state.AddLiveGrant(grant);
        Assert.IsTrue(state.Snapshot.IndicatorActive);

        Assert.IsFalse(state.RemoveLiveGrant("nope"));
        Assert.IsTrue(state.RemoveLiveGrant("g1"));
        Assert.IsFalse(state.Snapshot.IndicatorActive);
    }

    [TestMethod]
    public async Task PointerDown_HitsEachButton()
    {
        await AssertButton(l => l.Allow, ApprovalVerb.Allow);
        await AssertButton(l => l.AllowOnce, ApprovalVerb.AllowOnce);
        await AssertButton(l => l.Deny, ApprovalVerb.Deny);
    }

    private static async Task AssertButton(Func<LayoutRects, Rect> pick, ApprovalVerb expected)
    {
        var (state, _, _) = NewState();
        var task = state.RequestAsync(Req("r1"));
        var layout = state.Layout(800f, 600f);
        var rect = pick(layout);
        state.OnPointerDown(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        Assert.AreEqual(expected, (await task).Verb);
    }

    [TestMethod]
    public void PointerDown_OutsideButtons_DoesNothing()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        state.Layout(800f, 600f);
        state.OnPointerDown(0f, 0f); // top-left corner, no button there
        Assert.IsTrue(state.Snapshot.HasModal, "a miss leaves the request up");
    }

    [TestMethod]
    public void PointerDown_WithNoModal_IsIgnored()
    {
        var (state, _, _) = NewState();
        state.Layout(800f, 600f);
        state.OnPointerDown(400f, 300f); // no request pending
        Assert.IsFalse(state.Snapshot.HasModal);
    }

    [TestMethod]
    public void PointerDown_BeforeAnyLayout_ResolvesNothing()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        // No Layout/BuildVertices yet, so there is no drawn size to hit-test.
        state.OnPointerDown(400f, 300f);
        Assert.IsTrue(state.Snapshot.HasModal, "a click before the first draw resolves nothing");
    }

    [TestMethod]
    public void RequestAsync_PreCancelledToken_ResolvesAndCleansUp()
    {
        var (state, _, _) = NewState();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // The registration callback runs synchronously here, resolving the
        // request before RequestAsync stores the registration on the entry.
        var task = state.RequestAsync(Req("r1"), cts.Token);
        Assert.IsTrue(task.IsCompleted);
        Assert.AreEqual(ApprovalVerb.Deny, task.Result.Verb);
        Assert.IsFalse(state.Snapshot.HasModal, "the cancelled request is pruned");
    }

    [TestMethod]
    public void BuildVertices_IsEmptyWhenIdle_AndFilledPerActiveLayer()
    {
        const int floatsPerQuad = 6 * 6;
        var (state, _, _) = NewState();
        Assert.AreEqual(0, state.BuildVertices(800f, 600f).Length);

        _ = state.RequestAsync(Req("r1"));
        var modal = state.BuildVertices(800f, 600f).Length;
        Assert.AreEqual(0, modal % floatsPerQuad, "vertices come in whole quads");
        // Panel, accent, three buttons, a badge, and the name, capability,
        // detail, and button labels are all quads — well past the bare chrome.
        Assert.IsTrue(modal > 8 * floatsPerQuad, "the rich panel carries text");

        // A live grant is tracked as data but draws nothing of its own.
        state.AddLiveGrant(new CapabilityGrant("g1", Actors.Planner, Actors.Os, InteractionCapability.Edit, null, true));
        Assert.IsTrue(state.Snapshot.IndicatorActive);
        Assert.AreEqual(modal, state.BuildVertices(800f, 600f).Length, "a live grant adds no geometry");
    }

    [TestMethod]
    public void BuildVertices_SmallSurface_FallsBackToPanelAndButtons()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        var rich = state.BuildVertices(800f, 600f).Length;
        var small = state.BuildVertices(200f, 150f).Length;
        Assert.IsTrue(small > 0, "a small surface still draws the panel and buttons");
        Assert.IsTrue(small < rich, "the small surface drops the prompt detail");
    }

    [TestMethod]
    public void BuildVertices_Countdown_DepletesAndShiftsColor()
    {
        var (state, time, _) = NewState();
        _ = state.RequestAsync(Req("r1", Start + TimeSpan.FromSeconds(30)));
        var full = state.BuildVertices(800f, 600f); // full window: meter fills green
        Assert.IsTrue(full.Length > 0);

        // A quarter left is well into the red; the request is still pending
        // because its deadline timer is not due until 30s.
        time.Advance(TimeSpan.FromSeconds(25));
        var low = state.BuildVertices(800f, 600f);
        Assert.AreNotSame(full, low, "advancing the clock rebuilds the animated geometry");
        Assert.IsTrue(low.Length > 0);
    }

    [TestMethod]
    public void BuildVertices_PastDeadline_ClampsToZero()
    {
        var (state, _, _) = NewState();
        // A deadline already behind us: the request is still pending until the
        // first clock advance, so the build clamps the remaining time to zero.
        _ = state.RequestAsync(Req("r1", Start - TimeSpan.FromSeconds(5)));
        Assert.IsTrue(state.BuildVertices(800f, 600f).Length > 0, "an overdue prompt still draws");
    }

    [TestMethod]
    public void BuildVertices_CachesWhenNothingChanges()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        var first = state.BuildVertices(800f, 600f);
        var second = state.BuildVertices(800f, 600f);
        Assert.AreSame(first, second, "identical size and snapshot reuse the built vertices");
        var resized = state.BuildVertices(640f, 480f);
        Assert.AreNotSame(first, resized, "a new size rebuilds");
    }

    [TestMethod]
    public void BuildVertices_StaysInClipSpace()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        var verts = state.BuildVertices(800f, 600f);
        for (var i = 0; i < verts.Length; i += 6)
        {
            Assert.IsTrue(verts[i] >= -1f && verts[i] <= 1f, "x in clip space");
            Assert.IsTrue(verts[i + 1] >= -1f && verts[i + 1] <= 1f, "y in clip space");
        }
    }

    [TestMethod]
    public void Layout_ClampsToTinySurfaces()
    {
        var (state, _, _) = NewState();
        var layout = state.Layout(0f, 0f);
        Assert.IsTrue(layout.Panel.Width > 0f);
    }

    [TestMethod]
    public async Task NoDeadline_NeverExpires()
    {
        var (state, time, _) = NewState();
        var task = state.RequestAsync(Req("r1", expires: null));
        time.Advance(TimeSpan.FromHours(1));
        Assert.IsFalse(task.IsCompleted, "a request with no deadline waits");
        state.Resolve("r1", ApprovalDecision.Allow(Start));
        await task;
    }

    [TestMethod]
    public void Resolve_RaisesADecisionToast()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        state.Resolve("r1", ApprovalDecision.Allow(Start));

        Assert.IsTrue(state.Snapshot.HasToasts);
        var toast = state.Snapshot.Toasts[0];
        Assert.AreEqual(ToastKind.Allowed, toast.Kind);
        Assert.AreEqual("ALLOWED", toast.Title);
        StringAssert.Contains(toast.Subtitle, "PLANNER");
        StringAssert.Contains(toast.Subtitle, "TYPING");
    }

    [TestMethod]
    public void Toasts_ReportEachVerbAndColor()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        _ = state.RequestAsync(Req("r2"));
        _ = state.RequestAsync(Req("r3"));
        state.Resolve("r1", ApprovalDecision.Allow(Start));
        state.Resolve("r2", ApprovalDecision.AllowOnce(Start));
        state.Resolve("r3", ApprovalDecision.Deny(Start));

        var toasts = state.Snapshot.Toasts;
        Assert.AreEqual(3, toasts.Count);
        Assert.AreEqual(ToastKind.Allowed, toasts[0].Kind);
        Assert.AreEqual(ToastKind.AllowedOnce, toasts[1].Kind);
        Assert.AreEqual(ToastKind.Denied, toasts[2].Kind);
        Assert.IsTrue(state.BuildVertices(800f, 600f).Length > 0, "every toast draws its chip and words");
    }

    [TestMethod]
    public void Toasts_FadeInHoldOut_ThenPrune()
    {
        var (state, time, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        state.Resolve("r1", ApprovalDecision.Allow(Start));
        Assert.IsTrue(state.Snapshot.HasToasts);

        state.BuildVertices(800f, 600f);              // p = 0     : sliding in
        time.Advance(TimeSpan.FromSeconds(0.9));
        state.BuildVertices(800f, 600f);              // p ~ 0.5   : held
        time.Advance(TimeSpan.FromSeconds(0.6));
        state.BuildVertices(800f, 600f);              // p ~ 0.83  : fading out
        time.Advance(TimeSpan.FromSeconds(0.5));      // past the 1.8s lifetime
        Assert.IsFalse(state.Snapshot.HasToasts, "the toast fades and the timer prunes it");
    }

    [TestMethod]
    public void Toasts_BornExpired_DrawNothing()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        // A decision stamped in the past is already beyond its fade window.
        state.Resolve("r1", ApprovalDecision.Allow(Start - TimeSpan.FromSeconds(5)));
        Assert.IsTrue(state.Snapshot.HasToasts, "still listed until the prune timer fires");
        Assert.AreEqual(0, state.BuildVertices(800f, 600f).Length, "a finished toast draws nothing");
    }

    [TestMethod]
    public void Toasts_FutureStamp_ClampsProgress()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        // A decision stamped ahead of the render clock clamps to the start.
        state.Resolve("r1", ApprovalDecision.Allow(Start + TimeSpan.FromSeconds(10)));
        Assert.IsTrue(state.BuildVertices(800f, 600f).Length > 0, "the toast draws at its first frame");
    }

    [TestMethod]
    public void Toasts_TinySurface_DrawNothing()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        state.Resolve("r1", ApprovalDecision.Allow(Start));
        Assert.AreEqual(0, state.BuildVertices(20f, 600f).Length, "no room for a toast on a sliver of a surface");
    }

    [TestMethod]
    public void Toasts_CapAtFour_AndPruneIsIdempotent()
    {
        var (state, time, _) = NewState();
        for (var i = 1; i <= 6; i++)
        {
            _ = state.RequestAsync(Req($"r{i}"));
            state.Resolve($"r{i}", ApprovalDecision.Allow(Start));
        }
        Assert.AreEqual(4, state.Snapshot.Toasts.Count, "only the four most recent toasts stay");

        // Every toast shares the same due time, so the first prune clears them
        // and the later prunes find nothing left to drop.
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.IsFalse(state.Snapshot.HasToasts);
    }

    [TestMethod]
    public void BuildVertices_RichScene_StaysInClipSpace()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1", Start + TimeSpan.FromSeconds(30)));
        state.AddLiveGrant(new CapabilityGrant("g1", Actors.Planner, Actors.Os, InteractionCapability.Edit, null, true));
        _ = state.RequestAsync(Req("r2"));
        state.Resolve("r2", ApprovalDecision.Deny(Start));

        var verts = state.BuildVertices(800f, 600f);
        Assert.IsTrue(verts.Length > 0);
        for (var i = 0; i < verts.Length; i += 6)
        {
            Assert.IsTrue(verts[i] >= -1f && verts[i] <= 1f, "x in clip space");
            Assert.IsTrue(verts[i + 1] >= -1f && verts[i + 1] <= 1f, "y in clip space");
        }
    }

    [TestMethod]
    public void KindColorAndBadge_CoverEveryKind()
    {
        foreach (var kind in Enum.GetValues<ActorKind>())
        {
            var color = ApprovalSurfaceState.KindColor(kind);
            Assert.IsTrue(color.A > 0f, $"{kind} has an accent color");
            Assert.IsFalse(string.IsNullOrEmpty(ApprovalSurfaceState.KindBadge(kind)), $"{kind} has a badge");
        }
    }

    [TestMethod]
    public void CapabilityText_NamesGatedPowersAndFallsBack()
    {
        Assert.AreEqual("REQUESTS CONTROL", ApprovalSurfaceState.CapabilityPhrase(InteractionCapability.Administer));
        Assert.AreEqual("WANTS THE KEYBOARD", ApprovalSurfaceState.CapabilityPhrase(InteractionCapability.LowLevelInput));
        Assert.AreEqual("WANTS TO TYPE", ApprovalSurfaceState.CapabilityPhrase(InteractionCapability.Edit));
        Assert.AreEqual("REQUESTS ACCESS", ApprovalSurfaceState.CapabilityPhrase(InteractionCapability.Observe));

        Assert.AreEqual("CONTROL", ApprovalSurfaceState.CapabilityShort(InteractionCapability.Administer));
        Assert.AreEqual("KEYBOARD", ApprovalSurfaceState.CapabilityShort(InteractionCapability.LowLevelInput));
        Assert.AreEqual("TYPING", ApprovalSurfaceState.CapabilityShort(InteractionCapability.Edit));
        Assert.AreEqual("ACCESS", ApprovalSurfaceState.CapabilityShort(InteractionCapability.Observe));
    }

    [TestMethod]
    public void UrgencyColor_RunsRedThroughAmberToGreen()
    {
        var spent = ApprovalSurfaceState.UrgencyColor(0f);
        var mid = ApprovalSurfaceState.UrgencyColor(0.5f);
        var fresh = ApprovalSurfaceState.UrgencyColor(1f);
        Assert.IsTrue(spent.R > spent.G, "no time left reads red");
        Assert.IsTrue(fresh.G > fresh.R, "a full window reads green");
        Assert.IsTrue(mid.R > 0.5f && mid.G > 0.5f, "the midpoint is amber");
    }

    [TestMethod]
    public void FormatClock_FormatsMinutesAndClampsNegative()
    {
        Assert.AreEqual("0:27", ApprovalSurfaceState.FormatClock(TimeSpan.FromSeconds(27)));
        Assert.AreEqual("1:05", ApprovalSurfaceState.FormatClock(TimeSpan.FromSeconds(65)));
        Assert.AreEqual("0:00", ApprovalSurfaceState.FormatClock(TimeSpan.FromSeconds(-3)));
    }

    [TestMethod]
    public void BuildVertices_EmptyPrompt_SkipsTheDetailLine()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(new ApprovalRequest("e1", Actors.Planner, InteractionCapability.Edit, "", null, true));
        Assert.IsTrue(state.BuildVertices(800f, 600f).Length > 0, "an empty detail line just leaves a gap");
    }

    [TestMethod]
    public void BuildVertices_NarrowRichPanel_DropsLinesWithNoRoom()
    {
        var (state, _, _) = NewState();
        _ = state.RequestAsync(Req("r1"));
        // Tall but narrow: the panel unfolds, yet the name has no room between
        // the badge and the timer, so that line is dropped, not overflowed.
        Assert.IsTrue(state.BuildVertices(208f, 260f).Length > 0);
    }
}
