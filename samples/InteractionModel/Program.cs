using Skyline.Interaction;

// The pure interaction model that the approvals overlay sits on. No window, no
// GPU — just the decision tier: actors, a policy, the approval shell, semantic
// commands, the split focus graph, and the transfer broker. Run it to see each
// piece make a decision.

Console.WriteLine("Skyline interaction model\n");

var me = new Actor("me", "You", ActorKind.Human, ActorLocality.Local);
var ai = new Actor("planner", "Planner (AI)", ActorKind.Ai, ActorLocality.Local);
var guest = new Actor("guest", "Guest", ActorKind.Human, ActorLocality.Remote);

// 1. The policy routes the same ask differently by who is asking.
var policy = new DefaultApprovalPolicy();

void ShowPolicy(Actor actor, InteractionCapability capability)
{
    var word = policy.Evaluate(actor, capability) switch
    {
        ImplicitGrant => "grant",
        Ask => "ask",
        Deny => "deny",
        _ => "?",
    };
    Console.WriteLine($"  {actor.DisplayName,-14} {capability,-14} -> {word}");
}

Console.WriteLine("Policy:");
ShowPolicy(me, InteractionCapability.Point);
ShowPolicy(me, InteractionCapability.Edit);
ShowPolicy(ai, InteractionCapability.Edit);
ShowPolicy(ai, InteractionCapability.LowLevelInput);
ShowPolicy(guest, InteractionCapability.Edit);

// 2. The shell runs an "ask" through an approval UI. Here a console stand-in
//    auto-allows; on the desktop this is the composited overlay.
var shell = new InProcessApprovalShell(new ConsoleApprovalUi());
var grant = await shell.AuthorizeAsync(ai, InteractionCapability.Edit, "Planner wants to type into Address");
Console.WriteLine($"Shell: {(grant is null ? "denied" : $"granted {grant.Capabilities}")}\n");

// 3. Semantic commands are namespaced strings, not an enum.
var commands = new CommandRegistry()
    .Register(new CommandDescriptor(CommandId.Parse("edit.copy"), "Copy", InteractionCapability.Observe))
    .Register(new CommandDescriptor(CommandId.Parse("edit.paste"), "Paste", InteractionCapability.Edit));
Console.WriteLine($"Commands: {string.Join(", ", commands.All.Select(c => c.Id))}");

// 4. The split focus graph: gaze sets attention, a pointer sets pointer, and
//    BestCommandTarget picks the most specific live slot.
var focus = FocusGraph.Empty
    .ObserveFrom(InputModality.Gaze, new TargetRef("page", new ObjectTarget("headline")))
    .ObserveFrom(InputModality.Pointer, new TargetRef("page", new ObjectTarget("button")));
var best = focus.BestCommandTarget?.Target is ObjectTarget target ? target.ObjectId : "(none)";
Console.WriteLine($"Focus: a command lands on '{best}'");

// 5. The transfer broker moves text with a policy. A local actor may take the
//    offer; a remote actor is denied a local-only one.
var broker = new InProcessTransferBroker();
var offer = broker.Offer("text/plain", "hello from Skyline", new Provenance(me, DateTimeOffset.UtcNow));
Console.WriteLine($"Transfer: local take = '{broker.Take(offer.Id, me)?.Payload}'");
Console.WriteLine($"          remote take = {(broker.Take(offer.Id, guest) is null ? "denied" : "allowed")}");

Console.WriteLine("\nINTERACTION MODEL OK");
return 0;

sealed class ConsoleApprovalUi : IApprovalUi
{
    public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        Console.WriteLine($"  [prompt] {request.Prompt}  ->  Allow");
        return Task.FromResult(ApprovalDecision.Allow(DateTimeOffset.UtcNow));
    }
}
