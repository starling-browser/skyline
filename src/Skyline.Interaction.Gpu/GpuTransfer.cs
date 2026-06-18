// SPDX-License-Identifier: Apache-2.0
using Silk.NET.WebGPU;

namespace Skyline.Interaction.Gpu;

/// <summary>
/// A GPU surface offered for transfer: a wgpu texture handle plus the metadata
/// a taker needs to bind it. The source owns the texture and keeps it alive for
/// the life of the offer; the taker must read or copy it before the offer
/// expires or is revoked. The handle never round-trips through the processor —
/// that is the whole point of the GPU bridge.
/// </summary>
public readonly record struct GpuSurfaceHandle(
    nint Texture, TextureFormat Format, uint Width, uint Height);

/// <summary>
/// One offered GPU surface, with provenance and policy — the GPU twin of
/// <see cref="TransferOffer"/>. Provenance and policy come from the same
/// <see cref="Skyline.Interaction"/> model, so a surface transfer answers the
/// same who/how-far/how-long questions a text transfer does.
/// </summary>
public sealed record GpuTransferOffer(
    string Id, GpuSurfaceHandle Surface, Provenance Provenance, TransferPolicy Policy);

/// <summary>
/// The GPU transfer seam: offer a surface, take one by id (subject to its
/// policy), revoke, or list the live ones. Mirrors <see cref="ITransferBroker"/>
/// for GPU handles instead of string payloads.
/// </summary>
public interface IGpuTransferBroker
{
    GpuTransferOffer Offer(GpuSurfaceHandle surface, Provenance provenance, TransferPolicy? policy = null);
    GpuTransferOffer? Take(string id, Actor taker);
    bool Revoke(string id);
    IReadOnlyList<GpuTransferOffer> List();
}

/// <summary>
/// The default in-process GPU transfer broker. Same lifetime and locality rules
/// as the text broker: it holds offers until they expire or are revoked, denies
/// a remote taker an offer its policy keeps local, and prunes lapsed offers
/// against an injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class InProcessGpuTransferBroker(TimeProvider? time = null) : IGpuTransferBroker
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly List<GpuTransferOffer> _offers = [];

    public GpuTransferOffer Offer(GpuSurfaceHandle surface, Provenance provenance, TransferPolicy? policy = null)
    {
        var offer = new GpuTransferOffer(
            Guid.NewGuid().ToString("n"), surface, provenance, policy ?? TransferPolicy.Default);
        lock (_gate)
        {
            _offers.Add(offer);
        }
        return offer;
    }

    public GpuTransferOffer? Take(string id, Actor taker)
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

    public IReadOnlyList<GpuTransferOffer> List()
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
