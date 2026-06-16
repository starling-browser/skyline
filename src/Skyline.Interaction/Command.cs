// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>
/// A semantic command name, namespaced like <c>edit.copy</c> — a string, not
/// an enum, so a new command is data, not a code change. The text before the
/// first dot is the <see cref="Namespace"/>, the rest is the <see cref="Name"/>.
/// </summary>
public readonly record struct CommandId
{
    public string Namespace { get; }
    public string Name { get; }

    public CommandId(string @namespace, string name)
    {
        if (string.IsNullOrEmpty(@namespace))
        {
            throw new ArgumentException("a command namespace is required", nameof(@namespace));
        }
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("a command name is required", nameof(name));
        }
        Namespace = @namespace;
        Name = name;
    }

    /// <summary>Parse a qualified id such as <c>edit.copy</c>. Throws when it is not <c>namespace.name</c>.</summary>
    public static CommandId Parse(string qualified)
    {
        if (qualified is null)
        {
            throw new FormatException("a command id is required (expected 'namespace.name')");
        }
        var dot = qualified.IndexOf('.');
        if (dot <= 0 || dot >= qualified.Length - 1)
        {
            throw new FormatException($"'{qualified}' is not a namespaced command id (expected 'namespace.name')");
        }
        return new CommandId(qualified[..dot], qualified[(dot + 1)..]);
    }

    public string Qualified => $"{Namespace}.{Name}";

    public override string ToString() => Qualified;
}

/// <summary>What a command is and what it costs: its id, a human title, and the capability it needs.</summary>
public sealed record CommandDescriptor(CommandId Id, string Title, InteractionCapability RequiredCapability);

/// <summary>One invocation of a command: who asked, and where it lands (null to use the focus graph).</summary>
public sealed record CommandRequest(CommandId Id, Actor Requester, TargetRef? Target = null);

/// <summary>The command ran.</summary>
public sealed record CommandAccepted(TargetRef Target);

/// <summary>The command did not run, with a reason.</summary>
public sealed record CommandRejected(string Reason);

/// <summary>How a command dispatch ended, as a closed set of two cases.</summary>
public union CommandResult(CommandAccepted, CommandRejected);

/// <summary>
/// A registry of the commands a surface understands. Pure lookup: register
/// descriptors, then resolve a request to its descriptor or learn it is
/// unknown. Authority still runs through the policy and the approvals shell —
/// this only says what a command is.
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<CommandId, CommandDescriptor> _commands = [];

    /// <summary>Register or replace a command's descriptor. Returns the registry for chaining.</summary>
    public CommandRegistry Register(CommandDescriptor descriptor)
    {
        _commands[descriptor.Id] = descriptor;
        return this;
    }

    /// <summary>The descriptor for <paramref name="id"/>, or null when it is not registered.</summary>
    public CommandDescriptor? Find(CommandId id) =>
        _commands.TryGetValue(id, out var descriptor) ? descriptor : null;

    /// <summary>True when <paramref name="id"/> is registered.</summary>
    public bool Knows(CommandId id) => _commands.ContainsKey(id);

    /// <summary>Every registered descriptor.</summary>
    public IReadOnlyCollection<CommandDescriptor> All => _commands.Values;
}
