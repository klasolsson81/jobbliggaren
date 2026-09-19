using Jobbliggaren.Application.Dev.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Dev.Commands.TakeLoginCode;

/// <summary>DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). A pass-through to <see cref="IDevLoginCodeReader"/>.</summary>
public sealed class DevTakeLoginCodeCommandHandler(IDevLoginCodeReader reader)
    : ICommandHandler<DevTakeLoginCodeCommand, string?>
{
    public ValueTask<string?> Handle(DevTakeLoginCodeCommand command, CancellationToken cancellationToken) =>
        ValueTask.FromResult(reader.TakeCode(command.Email));
}
