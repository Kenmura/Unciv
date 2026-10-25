using System.Text.Json;
using Unciv.Utils;

namespace Unciv.Commands;

// Maps wire type names to command classes.
public sealed class CommandRegistry<TState>(JsonSerializerOptions options)
{
    private readonly Dictionary<string, Type> typeByName = [];
    private readonly Dictionary<Type, string> nameByType = [];

    public void Register<TCommand>(string type) where TCommand : ICommand<TState> => Register(typeof(TCommand), type);

    public void Register(Type commandClass, string type)
    {
        if (!typeof(ICommand<TState>).IsAssignableFrom(commandClass))
            throw new InvalidOperationException($"{commandClass.Name} is not a command for {typeof(TState).Name}");
        if (typeByName.ContainsKey(type))
            throw new InvalidOperationException($"Command type \"{type}\" is already registered");
        if (nameByType.ContainsKey(commandClass))
            throw new InvalidOperationException($"{commandClass.Name} is already registered");
        typeByName.Add(type, commandClass);
        nameByType.Add(commandClass, type);
    }

    /// <summary>The registered wire names, in registration order.</summary>
    public IReadOnlyCollection<string> Types => typeByName.Keys;

    public CommandEnvelope ToEnvelope(long seq, string actorId, ICommand<TState> command)
    {
        if (!nameByType.TryGetValue(command.GetType(), out string? registered) || registered != command.Type)
            throw new InvalidOperationException($"{command.GetType().Name} is not registered under its type \"{command.Type}\"");
        var payload = JsonSerializer.SerializeToElement(command, command.GetType(), options);
        return new CommandEnvelope(seq, command.Type, actorId, payload);
    }

    // Untrusted input: reports problems as a failed validation instead of throwing.
    public bool TryFromEnvelope(CommandEnvelope envelope, out ICommand<TState>? command, out CommandValidation failure)
    {
        command = null;
        if (!typeByName.TryGetValue(envelope.Type, out var type))
        {
            failure = CommandValidation.Fail(CommandCodes.UnknownCommand, $"Unknown command type \"{envelope.Type}\"");
            return false;
        }
        if (envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            failure = CommandValidation.Fail(CommandCodes.MalformedCommand, $"Payload of \"{envelope.Type}\" must be a json object");
            return false;
        }
        try
        {
            command = (ICommand<TState>?)envelope.Payload.Deserialize(type, options);
        }
        catch (Exception e) when (e is JsonException or FormatException or NotSupportedException)
        {
            failure = CommandValidation.Fail(CommandCodes.MalformedCommand, $"Payload of \"{envelope.Type}\" is invalid: {e.Message}");
            return false;
        }
        if (command == null)
        {
            failure = CommandValidation.Fail(CommandCodes.MalformedCommand, $"Payload of \"{envelope.Type}\" is empty");
            return false;
        }
        failure = CommandValidation.Ok;
        return true;
    }

    // Trusted input (a log we wrote ourselves): throws if it cannot be read.
    public ICommand<TState> FromEnvelope(CommandEnvelope envelope) =>
        TryFromEnvelope(envelope, out var command, out var failure)
            ? command.NotNull()
            : throw new CommandDesyncException($"Cannot read command {envelope.Seq}: {failure.Message}");
}
