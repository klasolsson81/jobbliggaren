using Mediator;

namespace JobbPilot.Application.UnitTests.Common.Behaviors;

public sealed record TestCommand(string Payload) : ICommand<string>;
public sealed record TestQuery(string Payload) : IQuery<string>;
