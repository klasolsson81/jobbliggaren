using System.Net;
using Jobbliggaren.Api.Configuration;
using Jobbliggaren.Api.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

// The socket peer is supplied by TestServer; these are middleware/limiter contract tests,
// not a measurement of Caddy, Next or Program.cs wiring. Synthetic networks model an
// explicitly trusted proxy and an untrusted caller without touching a live environment.
public sealed class ForwardedClientBudgetTests
{
    [Theory]
    [InlineData("192.0.2.10", "198.51.100.1", "198.51.100.2")]
    [InlineData("2001:db8:1::10", "2001:db8:2::1", "2001:db8:2::2")]
    public async Task TrustedProxy_DistinctClients_ReceiveIndependentBudgets(
        string peer, string first, string second)
    {
        using var host = await CreateHostAsync();
        var server = host.GetTestServer();
        var budget = new RateLimitingOptions().AuthWrite.PermitLimit;
        for (var i = 0; i < budget; i++)
            (await SendAsync(server, peer, first)).Response.StatusCode.ShouldBe(StatusCodes.Status204NoContent);

        var refused = await SendAsync(server, peer, first);
        refused.Response.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        int.TryParse(refused.Response.Headers.RetryAfter, out var seconds).ShouldBeTrue();
        seconds.ShouldBeInRange(1, new RateLimitingOptions().AuthWrite.WindowSeconds);
        (await SendAsync(server, peer, second)).Response.StatusCode.ShouldBe(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task UntrustedPeer_ChangingForwardedAddress_DoesNotResetBudget()
    {
        using var host = await CreateHostAsync();
        var server = host.GetTestServer();
        const string peer = "203.0.113.10";
        var budget = new RateLimitingOptions().AuthWrite.PermitLimit;
        for (var i = 0; i < budget; i++)
        {
            var accepted = await SendAsync(server, peer, $"198.51.100.{i + 1}");
            accepted.Response.StatusCode.ShouldBe(StatusCodes.Status204NoContent);
            accepted.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse(peer));
        }

        (await SendAsync(server, peer, "198.51.100.200")).Response.StatusCode
            .ShouldBe(StatusCodes.Status429TooManyRequests);
        (await SendAsync(server, "203.0.113.11", "198.51.100.200")).Response.StatusCode
            .ShouldBe(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task TrustedProxy_MissingHeader_PreservesConnectionAddress()
    {
        using var host = await CreateHostAsync();
        var server = host.GetTestServer();
        var result = await SendAsync(server, "192.0.2.10", null);
        result.Response.StatusCode.ShouldBe(StatusCodes.Status204NoContent);
        result.Connection.RemoteIpAddress.ShouldBe(IPAddress.Parse("192.0.2.10"));
    }

    private static Task<IHost> CreateHostAsync()
    {
        var config = new ForwardedHeadersConfig
        {
            KnownNetworks = ["192.0.2.0/24", "2001:db8:1::/64"],
        };
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = config.ValidateForwardLimit(),
        };
        foreach (var network in config.ParseKnownNetworks())
            forwarded.KnownIPNetworks.Add(network);

        return new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddJobbliggarenRateLimiting(new ConfigurationBuilder().Build());
            })
            .Configure(app =>
            {
                app.UseForwardedHeaders(forwarded);
                app.UseRouting();
                app.UseRateLimiter();
                app.UseEndpoints(endpoints => endpoints.MapGet("/probe", context =>
                {
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return Task.CompletedTask;
                }).RequireRateLimiting(RateLimitingExtensions.AuthWritePolicy));
            })).StartAsync(TestContext.Current.CancellationToken);
    }

    private static Task<HttpContext> SendAsync(TestServer server, string peer, string? client) =>
        server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/probe";
            if (client is not null)
                context.Request.Headers["X-Forwarded-For"] = client;
        }, TestContext.Current.CancellationToken);
}
