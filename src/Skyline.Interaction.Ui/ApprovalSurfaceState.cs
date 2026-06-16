// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction.Ui;

/// <summary>
/// The GPU-free heart of the approvals overlay: it holds the pending requests,
/// live grants, and decision toasts, answers them, lays out the panel and
/// buttons, hit-tests a pointer, and builds the vertices the overlay draws.
/// Everything here runs headless, so the whole logic path is covered without a
/// window — only the wgpu encode in <see cref="ApprovalsOverlay"/> needs one.
///
/// One writer owns the snapshot: every mutation goes through the same lock and
/// rebuilds the immutable <see cref="ApprovalSnapshot"/>, so the render thread
/// always reads a consistent picture. Requests surface first-in-first-out, each
/// with its own deadline, so a stacked request never starves an earlier one.
/// </summary>
public sealed class ApprovalSurfaceState(TimeProvider time, Action requestRedraw) : IPointerApprovalSink
{
    private const int FloatsPerVertex = 6;

    // A panel wider than this floor unfolds the full prompt — badge, name,
    // capability, detail, countdown. Below it (a tiny window) only the panel
    // and the three buttons draw, so the answer is always reachable.
    private const float RichMinHeight = 200f;

    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(1.8);
    private const int MaxToasts = 4;

    // Rebuild animated geometry at roughly 30 Hz. While nothing animates the
    // key stays zero and BuildVertices hands back the same cached array.
    private static readonly long AnimationQuantumTicks = TimeSpan.FromMilliseconds(33).Ticks;

    private static readonly Rgba PanelColor = new(0.07f, 0.08f, 0.11f, 0.96f);
    private static readonly Rgba AllowColor = new(0.20f, 0.78f, 0.42f, 1f);
    private static readonly Rgba AllowOnceColor = new(0.95f, 0.68f, 0.18f, 1f);
    private static readonly Rgba DenyColor = new(0.90f, 0.28f, 0.26f, 1f);
    private static readonly Rgba TextBright = new(0.96f, 0.97f, 0.99f, 1f);
    private static readonly Rgba TextDim = new(0.60f, 0.65f, 0.74f, 1f);
    private static readonly Rgba TextOnBadge = new(0.05f, 0.06f, 0.09f, 1f);
    private static readonly Rgba MeterTrack = new(1f, 1f, 1f, 0.10f);
    private static readonly Rgba ToastBg = new(0.10f, 0.11f, 0.15f, 0.97f);

    private readonly Lock _gate = new();
    private readonly List<Entry> _pending = [];
    private readonly List<CapabilityGrant> _grants = [];
    private readonly List<DecisionToast> _toasts = [];

    private volatile ApprovalSnapshot _snapshot = ApprovalSnapshot.Empty;

    // The logical size the overlay was last laid out at. OnPointerDown reads it
    // to hit-test a click against the buttons where they were last drawn,
    // rather than against a separately-published layout that could lag.
    private volatile SurfaceSize _lastSize = SurfaceSize.None;

    // BuildVertices output cached on (size, snapshot, animation frame) so a
    // static modal does not rebuild identical geometry every frame, while a
    // countdown or fading toast still advances. Render-thread only.
    private float[] _cacheVertices = [];
    private float _cacheWidth;
    private float _cacheHeight;
    private long _cacheKey;
    private ApprovalSnapshot? _cacheSnapshot;

    /// <summary>The picture the render thread reads. Rebuilt on change and swapped atomically.</summary>
    public ApprovalSnapshot Snapshot => _snapshot;

    /// <summary>
    /// Queue a request and return a task that completes when it is answered,
    /// times out, or is cancelled. Posts a snapshot rebuild and a redraw.
    /// </summary>
    public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new Entry(request, tcs, time.GetUtcNow());
        lock (_gate)
        {
            _pending.Add(entry);
            RebuildLocked();
        }

        // Build the timeout timer and cancellation hook, then store them under
        // the lock. Either can resolve the request the instant it is wired — an
        // immediate timer, or an already-cancelled token whose registration
        // callback runs synchronously here. If that happened before the entry
        // held them, Resolve had nothing to dispose, so dispose them now.
        ITimer? timer = null;
        if (request.ExpiresAt is { } expiresAt)
        {
            var delay = expiresAt - time.GetUtcNow();
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }
            timer = time.CreateTimer(
                _ => Resolve(request.Id, ApprovalDecision.Deny(time.GetUtcNow())),
                null, delay, Timeout.InfiniteTimeSpan);
        }
        var registration = ct.Register(() => Resolve(request.Id, ApprovalDecision.Deny(time.GetUtcNow())));

        lock (_gate)
        {
            if (entry.Resolved)
            {
                timer?.Dispose();
                registration.Dispose();
            }
            else
            {
                entry.Timer = timer;
                entry.Registration = registration;
            }
        }

        requestRedraw();
        return tcs.Task;
    }

    /// <summary>
    /// Answer a request by id. Completes its task, prunes it, raises a decision
    /// toast, and surfaces the next. Returns false if the request was already
    /// answered — the double-resolve guard that makes a click-and-timeout race
    /// safe.
    /// </summary>
    public bool Resolve(string requestId, ApprovalDecision decision)
    {
        Entry entry;
        lock (_gate)
        {
            var index = _pending.FindIndex(e => e.Request.Id == requestId);
            if (index < 0)
            {
                return false;
            }
            entry = _pending[index];
            entry.Resolved = true;
            _pending.RemoveAt(index);
            AddToastLocked(entry.Request, decision.Verb, decision.At);
            RebuildLocked();
        }

        entry.Timer?.Dispose();
        entry.Registration.Dispose();
        entry.Tcs.TrySetResult(decision);
        ScheduleToastPrune();
        requestRedraw();
        return true;
    }

    /// <summary>Record a live visible grant so a host can read what capabilities are currently held.</summary>
    public void AddLiveGrant(CapabilityGrant grant)
    {
        lock (_gate)
        {
            _grants.Add(grant);
            RebuildLocked();
        }
        requestRedraw();
    }

    /// <summary>Drop a live grant. Returns false if it was not tracked.</summary>
    public bool RemoveLiveGrant(string grantId)
    {
        lock (_gate)
        {
            if (_grants.RemoveAll(g => g.Id == grantId) == 0)
            {
                return false;
            }
            RebuildLocked();
        }
        requestRedraw();
        return true;
    }

    /// <summary>The front request the modal is showing, the one a pointer answers. Down from the main thread.</summary>
    public void OnPointerDown(float x, float y)
    {
        var snap = _snapshot;
        if (!snap.HasModal)
        {
            return;
        }
        var size = _lastSize;
        if (size.Width <= 0f || size.Height <= 0f)
        {
            // Nothing has been drawn yet, so there is no button to hit.
            return;
        }
        var front = snap.Pending[0].Request;
        var layout = BuildLayout(size.Width, size.Height);
        if (layout.Allow.Contains(x, y))
        {
            Resolve(front.Id, ApprovalDecision.Allow(time.GetUtcNow()));
        }
        else if (layout.AllowOnce.Contains(x, y))
        {
            Resolve(front.Id, ApprovalDecision.AllowOnce(time.GetUtcNow()));
        }
        else if (layout.Deny.Contains(x, y))
        {
            Resolve(front.Id, ApprovalDecision.Deny(time.GetUtcNow()));
        }
    }

    /// <summary>
    /// Place the panel, its content rows, the three buttons, and the indicator
    /// pill for a logical surface size, cache the rects for the next pointer
    /// hit-test, and return them. Pure given the size.
    /// </summary>
    public LayoutRects Layout(float width, float height)
    {
        width = MathF.Max(1f, width);
        height = MathF.Max(1f, height);
        _lastSize = new SurfaceSize(width, height);
        return BuildLayout(width, height);
    }

    // Pure: place every rect for a clamped logical size. OnPointerDown and
    // Layout both call this, so a hit-test always matches what was drawn. Min
    // and Max (not Math.Clamp) keep a degenerate surface from throwing.
    private static LayoutRects BuildLayout(float width, float height)
    {
        const float pad = 18f;
        const float accentW = 6f;
        const float buttonH = 40f;
        const float buttonGap = 12f;
        const float badgeW = 46f;
        const float badgeH = 18f;
        const float timerW = 56f;

        var panelW = MathF.Max(160f, MathF.Min(width - 48f, 540f));
        var panelH = MathF.Max(96f, MathF.Min(236f, height - 32f));
        var panelX = (width - panelW) / 2f;
        var panelY = (height - panelH) / 2f;
        var panel = new Rect(panelX, panelY, panelW, panelH);
        var accent = new Rect(panelX, panelY, accentW, panelH);

        var innerX = panelX + accentW + pad;
        var innerW = panelW - accentW - pad * 2f;

        var buttonY = panelY + panelH - buttonH - pad;
        var buttonW = (innerW - buttonGap * 2f) / 3f;
        var allow = new Rect(innerX, buttonY, buttonW, buttonH);
        var allowOnce = new Rect(innerX + buttonW + buttonGap, buttonY, buttonW, buttonH);
        var deny = new Rect(innerX + (buttonW + buttonGap) * 2f, buttonY, buttonW, buttonH);

        var headerY = panelY + pad;
        var badge = new Rect(innerX, headerY, badgeW, badgeH);
        var header = new Rect(innerX + badgeW + 10f, headerY, innerW - badgeW - 10f - (timerW + 10f), badgeH);
        var timer = new Rect(panelX + panelW - pad - timerW, headerY, timerW, badgeH);

        var headline = new Rect(innerX, headerY + badgeH + 14f, innerW, 22f);
        var detail = new Rect(innerX, headline.Y + headline.Height + 8f, innerW, 12f);

        const float captionH = 10f;
        const float meterH = 8f;
        var caption = new Rect(innerX, buttonY - pad - captionH, innerW, captionH);
        var meter = new Rect(innerX, caption.Y - 6f - meterH, innerW, meterH);

        return new LayoutRects(panel, allow, allowOnce, deny, accent, badge, header, timer, headline, detail, meter, caption);
    }

    /// <summary>
    /// Build the overlay's triangle vertices for a logical surface size:
    /// position (x, y) in clip space then color (r, g, b, a), six floats a
    /// vertex, six vertices a quad. Empty when nothing is showing.
    /// </summary>
    public float[] BuildVertices(float width, float height)
    {
        var w = MathF.Max(1f, width);
        var h = MathF.Max(1f, height);
        var snap = _snapshot;
        var now = time.GetUtcNow();
        var key = IsAnimating(snap, now) ? now.UtcTicks / AnimationQuantumTicks : 0L;
        if (ReferenceEquals(snap, _cacheSnapshot) && w == _cacheWidth && h == _cacheHeight && key == _cacheKey)
        {
            return _cacheVertices;
        }

        var layout = Layout(w, h);
        var quads = new List<(Rect Rect, Rgba Color)>();
        if (snap.HasModal)
        {
            BuildModal(quads, layout, snap.Pending[0], now);
        }
        BuildToasts(quads, snap.Toasts, now, w);

        var vertices = new float[quads.Count * 6 * FloatsPerVertex];
        var offset = 0;
        foreach (var (rect, color) in quads)
        {
            offset = EmitQuad(vertices, offset, rect, color, w, h);
        }

        _cacheSnapshot = snap;
        _cacheWidth = w;
        _cacheHeight = h;
        _cacheKey = key;
        _cacheVertices = vertices;
        return vertices;
    }

    private static void BuildModal(List<(Rect Rect, Rgba Color)> quads, LayoutRects l, PendingApproval pending, DateTimeOffset now)
    {
        var req = pending.Request;
        var accent = KindColor(req.Requester.Kind);
        quads.Add((l.Panel, PanelColor));
        quads.Add((l.Accent, accent));

        if (l.Panel.Height >= RichMinHeight)
        {
            quads.Add((l.Badge, accent));
            EmitFit(quads, KindBadge(req.Requester.Kind), Pad(l.Badge, 3f, 2f), TextOnBadge, 0.9f, 0.74f);
            EmitLine(quads, req.Requester.DisplayName.ToUpperInvariant(), l.Header, TextBright, 0.9f);
            EmitLine(quads, CapabilityPhrase(req.Requested), l.Headline, TextBright, 0.92f);
            EmitLine(quads, req.Prompt.ToUpperInvariant(), l.Detail, TextDim, 1f);

            if (req.ExpiresAt is { } expiresAt)
            {
                var remaining = expiresAt - now;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }
                var total = expiresAt - pending.CreatedAt;
                var frac = total > TimeSpan.Zero ? Math.Clamp((float)(remaining / total), 0f, 1f) : 0f;
                var urgent = UrgencyColor(frac);

                EmitLine(quads, FormatClock(remaining), l.Timer, urgent, 0.9f);
                quads.Add((l.Meter, MeterTrack));
                if (frac > 0f)
                {
                    quads.Add((l.Meter with { Width = l.Meter.Width * frac }, urgent));
                }
                EmitLine(quads, $"EXPIRES IN {(int)Math.Ceiling(remaining.TotalSeconds)}S", l.Caption, TextDim, 1f);
            }
            else
            {
                EmitLine(quads, "NO TIME LIMIT", l.Caption, TextDim, 1f);
            }
        }

        quads.Add((l.Allow, AllowColor));
        quads.Add((l.AllowOnce, AllowOnceColor));
        quads.Add((l.Deny, DenyColor));
        EmitFit(quads, "ALLOW", l.Allow, TextBright, 0.82f, 0.5f);
        EmitFit(quads, "ALLOW ONCE", l.AllowOnce, TextBright, 0.82f, 0.5f);
        EmitFit(quads, "DENY", l.Deny, TextBright, 0.82f, 0.5f);
    }

    private static void BuildToasts(List<(Rect Rect, Rgba Color)> quads, IReadOnlyList<DecisionToast> toasts, DateTimeOffset now, float width)
    {
        var toastW = MathF.Min(280f, width - 24f);
        if (toastW <= 0f)
        {
            return;
        }
        const float toastH = 34f;
        const float gap = 8f;
        var x = (width - toastW) / 2f;
        for (var i = 0; i < toasts.Count; i++)
        {
            // Newest sits on top; an answer flashes there and pushes the rest down.
            var t = toasts[toasts.Count - 1 - i];
            var p = (float)((now - t.StartedAt) / ToastLifetime);
            if (p < 0f)
            {
                p = 0f;
            }
            if (p >= 1f)
            {
                continue;
            }
            float alpha;
            float slide;
            if (p < 0.12f)
            {
                alpha = p / 0.12f;
                slide = (1f - alpha) * 12f;
            }
            else if (p > 0.70f)
            {
                var k = (p - 0.70f) / 0.30f;
                alpha = 1f - k;
                slide = -k * 6f;
            }
            else
            {
                alpha = 1f;
                slide = 0f;
            }

            var y = 16f + i * (toastH + gap) + slide;
            var color = ToastColor(t.Kind);
            quads.Add((new Rect(x, y, toastW, toastH), Fade(ToastBg, alpha)));
            quads.Add((new Rect(x, y, 5f, toastH), Fade(color, alpha)));
            EmitLine(quads, t.Title, new Rect(x + 14f, y + 5f, toastW - 28f, 13f), Fade(color, alpha), 1f);
            EmitLine(quads, t.Subtitle, new Rect(x + 14f, y + 19f, toastW - 28f, 9f), Fade(TextDim, alpha), 1f);
        }
    }

    private bool IsAnimating(ApprovalSnapshot snap, DateTimeOffset now)
    {
        if (snap.HasModal && snap.Pending[0].Request.ExpiresAt is not null)
        {
            return true;
        }
        for (var i = 0; i < snap.Toasts.Count; i++)
        {
            if (now - snap.Toasts[i].StartedAt < ToastLifetime)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The accent and badge color for an actor kind: cyan AI, amber automation, green human, violet system.</summary>
    internal static Rgba KindColor(ActorKind kind) => kind switch
    {
        ActorKind.Ai => new(0.30f, 0.78f, 0.94f, 1f),
        ActorKind.Automation => new(0.97f, 0.71f, 0.22f, 1f),
        ActorKind.Human => new(0.36f, 0.82f, 0.50f, 1f),
        _ => new(0.68f, 0.55f, 0.96f, 1f),
    };

    /// <summary>The short badge word for an actor kind.</summary>
    internal static string KindBadge(ActorKind kind) => kind switch
    {
        ActorKind.Ai => "AI",
        ActorKind.Automation => "AUTO",
        ActorKind.Human => "YOU",
        _ => "SYS",
    };

    /// <summary>The headline that says what the front request is asking for, most sensitive first.</summary>
    internal static string CapabilityPhrase(InteractionCapability c) =>
        c.HasFlag(InteractionCapability.Administer) ? "REQUESTS CONTROL"
        : c.HasFlag(InteractionCapability.LowLevelInput) ? "WANTS THE KEYBOARD"
        : c.HasFlag(InteractionCapability.Edit) ? "WANTS TO TYPE"
        : "REQUESTS ACCESS";

    /// <summary>The one-word capability a toast subtitle carries.</summary>
    internal static string CapabilityShort(InteractionCapability c) =>
        c.HasFlag(InteractionCapability.Administer) ? "CONTROL"
        : c.HasFlag(InteractionCapability.LowLevelInput) ? "KEYBOARD"
        : c.HasFlag(InteractionCapability.Edit) ? "TYPING"
        : "ACCESS";

    /// <summary>Green with time to spare, amber past the midpoint, red as the deadline closes.</summary>
    internal static Rgba UrgencyColor(float frac)
    {
        var red = new Rgba(0.92f, 0.30f, 0.26f, 1f);
        var amber = new Rgba(0.97f, 0.71f, 0.22f, 1f);
        var green = new Rgba(0.30f, 0.82f, 0.46f, 1f);
        return frac >= 0.5f ? Lerp(amber, green, (frac - 0.5f) / 0.5f) : Lerp(red, amber, frac / 0.5f);
    }

    /// <summary>A whole-second M:SS clock for the time a request has left.</summary>
    internal static string FormatClock(TimeSpan remaining)
    {
        var total = (int)Math.Ceiling(remaining.TotalSeconds);
        if (total < 0)
        {
            total = 0;
        }
        return $"{total / 60}:{total % 60:00}";
    }

    private static Rgba ToastColor(ToastKind kind) => kind switch
    {
        ToastKind.Allowed => AllowColor,
        ToastKind.AllowedOnce => AllowOnceColor,
        _ => DenyColor,
    };

    private void AddToastLocked(ApprovalRequest req, ApprovalVerb verb, DateTimeOffset at)
    {
        var (kind, title) = verb switch
        {
            ApprovalVerb.Allow => (ToastKind.Allowed, "ALLOWED"),
            ApprovalVerb.AllowOnce => (ToastKind.AllowedOnce, "ALLOWED ONCE"),
            _ => (ToastKind.Denied, "DENIED"),
        };
        var subtitle = $"{req.Requester.DisplayName} · {CapabilityShort(req.Requested)}".ToUpperInvariant();
        _toasts.Add(new DecisionToast(kind, title, subtitle, at));
        if (_toasts.Count > MaxToasts)
        {
            _toasts.RemoveRange(0, _toasts.Count - MaxToasts);
        }
    }

    private void ScheduleToastPrune()
    {
        ITimer? timer = null;
        timer = time.CreateTimer(_ =>
        {
            PruneToasts();
            timer?.Dispose();
        }, null, ToastLifetime, Timeout.InfiniteTimeSpan);
    }

    private void PruneToasts()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (_toasts.RemoveAll(t => now - t.StartedAt >= ToastLifetime) == 0)
            {
                return;
            }
            RebuildLocked();
        }
        requestRedraw();
    }

    private void RebuildLocked()
    {
        var requests = new PendingApproval[_pending.Count];
        for (var i = 0; i < _pending.Count; i++)
        {
            requests[i] = new PendingApproval(_pending[i].Request, _pending[i].CreatedAt);
        }
        _snapshot = new ApprovalSnapshot(requests, _grants.ToArray(), _toasts.ToArray());
    }

    // Lay one label centered in a rect, scaled to fit its width and height.
    private static void EmitFit(List<(Rect Rect, Rgba Color)> quads, string text, Rect rect, Rgba color, float wfill, float hfill)
    {
        var units = BitmapFont.MeasureUnits(text);
        var scale = MathF.Min(rect.Width * wfill / units, rect.Height * hfill / BitmapFont.Height);
        var x = rect.X + (rect.Width - units * scale) / 2f;
        var y = rect.Y + (rect.Height - BitmapFont.Height * scale) / 2f;
        EmitText(quads, text, x, y, scale, color);
    }

    // Lay one left-aligned line at the rect's height, clipping to its width.
    private static void EmitLine(List<(Rect Rect, Rgba Color)> quads, string text, Rect rect, Rgba color, float hfill)
    {
        if (text.Length == 0)
        {
            return;
        }
        var scale = rect.Height * hfill / BitmapFont.Height;
        var maxChars = (int)MathF.Floor((rect.Width / scale + 1f) / BitmapFont.Advance);
        if (maxChars <= 0)
        {
            return;
        }
        if (text.Length > maxChars)
        {
            text = text[..maxChars];
        }
        var y = rect.Y + (rect.Height - BitmapFont.Height * scale) / 2f;
        EmitText(quads, text, rect.X, y, scale, color);
    }

    private static void EmitText(List<(Rect Rect, Rgba Color)> quads, string text, float x, float y, float scale, Rgba color)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var rows = BitmapFont.Rows(text[i]);
            var charX = x + i * BitmapFont.Advance * scale;
            for (var row = 0; row < BitmapFont.Height; row++)
            {
                var bits = rows[row];
                for (var col = 0; col < BitmapFont.Width; col++)
                {
                    if ((bits & (1 << (BitmapFont.Width - 1 - col))) != 0)
                    {
                        quads.Add((new Rect(charX + col * scale, y + row * scale, scale, scale), color));
                    }
                }
            }
        }
    }

    private static Rgba Lerp(Rgba a, Rgba b, float t) =>
        new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    private static Rgba Fade(Rgba c, float a) => c with { A = c.A * a };

    private static Rect Pad(Rect r, float dx, float dy) => new(r.X + dx, r.Y + dy, r.Width - dx * 2f, r.Height - dy * 2f);

    private static int EmitQuad(float[] v, int o, Rect r, Rgba c, float width, float height)
    {
        var left = r.X / width * 2f - 1f;
        var right = (r.X + r.Width) / width * 2f - 1f;
        var top = 1f - r.Y / height * 2f;
        var bottom = 1f - (r.Y + r.Height) / height * 2f;
        o = EmitVertex(v, o, left, bottom, c);
        o = EmitVertex(v, o, right, bottom, c);
        o = EmitVertex(v, o, right, top, c);
        o = EmitVertex(v, o, left, bottom, c);
        o = EmitVertex(v, o, right, top, c);
        o = EmitVertex(v, o, left, top, c);
        return o;
    }

    private static int EmitVertex(float[] v, int o, float x, float y, Rgba c)
    {
        v[o++] = x;
        v[o++] = y;
        v[o++] = c.R;
        v[o++] = c.G;
        v[o++] = c.B;
        v[o++] = c.A;
        return o;
    }

    private sealed class Entry(ApprovalRequest request, TaskCompletionSource<ApprovalDecision> tcs, DateTimeOffset createdAt)
    {
        public ApprovalRequest Request { get; } = request;
        public TaskCompletionSource<ApprovalDecision> Tcs { get; } = tcs;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public ITimer? Timer { get; set; }
        public CancellationTokenRegistration Registration { get; set; }
        public bool Resolved { get; set; }
    }

    // A reference type so the volatile _lastSize swaps atomically.
    private sealed record SurfaceSize(float Width, float Height)
    {
        public static readonly SurfaceSize None = new(0f, 0f);
    }
}

/// <summary>The overlay's laid-out rectangles for one surface size, in logical pixels.</summary>
public sealed record LayoutRects(
    Rect Panel,
    Rect Allow,
    Rect AllowOnce,
    Rect Deny,
    Rect Accent,
    Rect Badge,
    Rect Header,
    Rect Timer,
    Rect Headline,
    Rect Detail,
    Rect Meter,
    Rect Caption);

/// <summary>A straight-alpha color, the four floats each overlay vertex carries.</summary>
public readonly record struct Rgba(float R, float G, float B, float A);
