using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

public class DomainLayerTests
{
    [Fact]
    public void Domain_should_not_depend_on_any_other_project()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Domain.Common.Entity<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Mediator",
                "FluentValidation",
                "Jobbliggaren.Application",
                "Jobbliggaren.Infrastructure",
                "Jobbliggaren.Api",
                "Jobbliggaren.Worker")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Domain läcker: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Jobbliggaren.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Application läcker mot Infrastructure: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_depend_on_AspNetCore()
    {
        // Application ska vara HTTP-agnostisk. ICurrentUser, IRequestContextProvider,
        // ICorrelationIdProvider är abstraktioner — implementationerna i Infrastructure
        // får bero på Microsoft.AspNetCore.Http, men Application-portarna får aldrig.
        // Bryts kontrakt = ASP.NET-pipeline-koncept läcker in i use-case-lagret
        // (senior-cto-advisor 2026-05-11 Viktigt #2).
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Authentication",
                "Microsoft.AspNetCore.Authorization",
                "Microsoft.AspNetCore.Identity",
                "Jobbliggaren.Api",
                "Jobbliggaren.Worker")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Application läcker mot ASP.NET / Api / Worker: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_depend_on_EFCore_database_providers()
    {
        // Application MÅ bero på Microsoft.EntityFrameworkCore (kärnabstraktioner som
        // DbSet<T>, DbContext-interface) per ADR 0009 — "medveten kompromiss".
        // Application får dock INTE bero på konkreta providers eller relational-specifika
        // paket, eftersom de hör hemma i Infrastructure.
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Microsoft.EntityFrameworkCore.SqlServer",
                "Microsoft.EntityFrameworkCore.Sqlite",
                "Microsoft.EntityFrameworkCore.Relational")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Application läcker mot databasprovider: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_should_not_depend_on_Api_or_Worker()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Jobbliggaren.Api",
                "Jobbliggaren.Worker")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Identity_types_should_stay_in_Infrastructure()
    {
        var domainResult = Types.InAssembly(typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.AspNetCore.Identity",
                "Microsoft.AspNetCore.Authentication.JwtBearer")
            .GetResult();

        var appResult = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore.Identity.EntityFrameworkCore")
            .GetResult();

        domainResult.IsSuccessful.ShouldBeTrue(
            $"Domain läcker mot Identity/JWT: {string.Join(", ", domainResult.FailingTypeNames ?? [])}");
        appResult.IsSuccessful.ShouldBeTrue(
            $"Application läcker mot Identity.EntityFrameworkCore: {string.Join(", ", appResult.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_aggregates_should_only_have_private_setters()
    {
        var aggregateTypes = Types.InAssembly(typeof(Jobbliggaren.Domain.Common.AggregateRoot<>).Assembly)
            .That()
            .Inherit(typeof(Jobbliggaren.Domain.Common.AggregateRoot<>))
            .GetTypes();

        foreach (var type in aggregateTypes)
        {
            var publicSetters = type.GetProperties()
                .Where(p => p.SetMethod is { IsPublic: true })
                .Select(p => $"{type.Name}.{p.Name}")
                .ToList();

            publicSetters.ShouldBeEmpty(
                $"Aggregate {type.Name} har public setters: {string.Join(", ", publicSetters)}");
        }
    }
}
