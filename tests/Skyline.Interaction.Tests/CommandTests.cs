namespace Skyline.Interaction.Tests;

[TestClass]
public class CommandTests
{
    [TestMethod]
    public void CommandId_HoldsNamespaceAndName()
    {
        var id = new CommandId("edit", "copy");
        Assert.AreEqual("edit", id.Namespace);
        Assert.AreEqual("copy", id.Name);
        Assert.AreEqual("edit.copy", id.Qualified);
        Assert.AreEqual("edit.copy", id.ToString());
    }

    [TestMethod]
    public void CommandId_RejectsEmptyParts()
    {
        Assert.ThrowsException<ArgumentException>(() => new CommandId("", "copy"));
        Assert.ThrowsException<ArgumentException>(() => new CommandId("edit", ""));
    }

    [TestMethod]
    public void Parse_AcceptsAQualifiedId()
    {
        var id = CommandId.Parse("edit.paste");
        Assert.AreEqual("edit", id.Namespace);
        Assert.AreEqual("paste", id.Name);
    }

    [TestMethod]
    public void Parse_RejectsMalformedIds()
    {
        Assert.ThrowsException<FormatException>(() => CommandId.Parse(null!));
        Assert.ThrowsException<FormatException>(() => CommandId.Parse("nodot"));
        Assert.ThrowsException<FormatException>(() => CommandId.Parse(".leading"));
        Assert.ThrowsException<FormatException>(() => CommandId.Parse("trailing."));
    }

    [TestMethod]
    public void Descriptor_And_Request_CarryTheirFields()
    {
        var id = new CommandId("edit", "copy");
        var descriptor = new CommandDescriptor(id, "Copy", InteractionCapability.Edit);
        Assert.AreEqual(id, descriptor.Id);
        Assert.AreEqual("Copy", descriptor.Title);
        Assert.AreEqual(InteractionCapability.Edit, descriptor.RequiredCapability);

        var plain = new CommandRequest(id, Actors.Planner);
        Assert.AreEqual(id, plain.Id);
        Assert.AreSame(Actors.Planner, plain.Requester);
        Assert.IsNull(plain.Target);

        var target = new TargetRef("s", new ObjectTarget("o"));
        var aimed = new CommandRequest(id, Actors.LocalHuman, target);
        Assert.AreSame(target, aimed.Target);
    }

    [TestMethod]
    public void CommandResult_IsAcceptedOrRejected()
    {
        var target = new TargetRef("s", new ObjectTarget("o"));
        Assert.AreEqual("ok:s", Describe(new CommandAccepted(target)));
        Assert.AreEqual("no:denied", Describe(new CommandRejected("denied")));
    }

    private static string Describe(CommandResult result) => result switch
    {
        CommandAccepted a => $"ok:{a.Target.SurfaceId}",
        CommandRejected r => $"no:{r.Reason}",
        _ => "?",
    };

    [TestMethod]
    public void Registry_RegistersFindsAndLists()
    {
        var copy = new CommandDescriptor(new CommandId("edit", "copy"), "Copy", InteractionCapability.Edit);
        var paste = new CommandDescriptor(new CommandId("edit", "paste"), "Paste", InteractionCapability.Edit);

        var registry = new CommandRegistry();
        Assert.AreSame(registry, registry.Register(copy), "Register chains");
        registry.Register(paste);

        Assert.AreSame(copy, registry.Find(copy.Id));
        Assert.IsTrue(registry.Knows(paste.Id));
        Assert.IsNull(registry.Find(new CommandId("edit", "cut")));
        Assert.IsFalse(registry.Knows(new CommandId("edit", "cut")));
        Assert.AreEqual(2, registry.All.Count);
    }
}
