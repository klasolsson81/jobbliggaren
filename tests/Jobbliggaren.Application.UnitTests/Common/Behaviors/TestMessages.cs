using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.UnitTests.Common.Behaviors;

public sealed record TestCommand(string Payload) : ICommand<string>;
public sealed record TestQuery(string Payload) : IQuery<string>;

// För AdminAuthorizationBehavior-tester. Implementerar IAdminRequest (som ärver
// IAuthenticatedRequest) så att Auth-behaviorn också kan testas i komposition.
public sealed record TestAdminCommand(string Payload) : ICommand<string>, IAdminRequest;
