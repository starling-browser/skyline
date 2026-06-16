// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>How far a transferred payload may travel.</summary>
public enum TransferScope { Application, Session, System }

/// <summary>Where a payload came from: the actor, when, and an optional origin label.</summary>
public sealed record Provenance(Actor Source, DateTimeOffset At, string? Origin = null);

/// <summary>The rules on a transfer: how far it reaches, whether a remote actor may take it, and when it lapses.</summary>
public sealed record TransferPolicy(TransferScope Scope, bool AllowRemote, DateTimeOffset? ExpiresAt = null)
{
    /// <summary>Session-scoped, local-only, no expiry — the safe default for a clipboard-style transfer.</summary>
    public static readonly TransferPolicy Default = new(TransferScope.Session, AllowRemote: false);
}

/// <summary>
/// One offered payload: a MIME type plus a string body, with provenance and
/// policy. Text and metadata only.
/// </summary>
public sealed record TransferOffer(
    string Id, string MimeType, string Payload, Provenance Provenance, TransferPolicy Policy);

/// <summary>
/// The transfer seam: offer a payload, take one by id (subject to its policy),
/// revoke, or list what is live. The clipboard is one offer with the
/// <c>text/plain</c> MIME type.
/// </summary>
public interface ITransferBroker
{
    TransferOffer Offer(string mimeType, string payload, Provenance provenance, TransferPolicy? policy = null);
    TransferOffer? Take(string id, Actor taker);
    bool Revoke(string id);
    IReadOnlyList<TransferOffer> List();
}

/// <summary>
/// The default in-process broker. Holds offers until they expire or are
/// revoked, denies a remote taker an offer its policy keeps local, and prunes
/// lapsed offers against an injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class InProcessTransferBroker(TimeProvider? time = null) : ITransferBroker
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly List<TransferOffer> _offers = [];

    public TransferOffer Offer(string mimeType, string payload, Provenance provenance, TransferPolicy? policy = null)
    {
        var offer = new TransferOffer(
            Guid.NewGuid().ToString("n"), mimeType, payload, provenance, policy ?? TransferPolicy.Default);
        lock (_gate)
        {
            _offers.Add(offer);
        }
        return offer;
    }

    public TransferOffer? Take(string id, Actor taker)
    {
        lock (_gate)
        {
            PruneLocked();
            var offer = _offers.Find(o => o.Id == id);
            if (offer is null)
            {
                return null;
            }
            if (taker.Locality == ActorLocality.Remote && !offer.Policy.AllowRemote)
            {
                return null;
            }
            return offer;
        }
    }

    public bool Revoke(string id)
    {
        lock (_gate)
        {
            return _offers.RemoveAll(o => o.Id == id) > 0;
        }
    }

    public IReadOnlyList<TransferOffer> List()
    {
        lock (_gate)
        {
            PruneLocked();
            return _offers.ToArray();
        }
    }

    private void PruneLocked()
    {
        var now = _time.GetUtcNow();
        _offers.RemoveAll(o => o.Policy.ExpiresAt is { } expiresAt && expiresAt <= now);
    }
}
