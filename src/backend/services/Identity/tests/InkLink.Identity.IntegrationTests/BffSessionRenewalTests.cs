using System.Net;
using System.Text.Json.Nodes;
using InkLink.Bff;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InkLink.Identity.IntegrationTests;

/// <summary>Decisão 0019: 30 minutos sem uso, 8 horas no total e refresh token de uso único no servidor.</summary>
[Collection("security")]
public sealed class BffSessionRenewalTests(SecurityFixture fixture)
{
    private sealed class Session : IDisposable
    {
        public required SecurityIdentityHost Identity { get; init; }
        public required SecurityGatewayHost Gateway { get; init; }
        public required SecurityBffHost Bff { get; init; }
        public required TestBrowser Browser { get; init; }
        public required ManualTime Time { get; init; }
        public void Dispose() { Browser.Dispose(); Bff.Dispose(); Gateway.Dispose(); Identity.Dispose(); }

        public async Task<AuthenticationTicket?> ServerTicketAsync()
        {
            var cookie = Browser.Cookies.GetCookies(TestBrowser.Origin)["__Host-inklink-session"];
            if (cookie is null) return null;
            var options = Bff.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("session");
            var key = options.TicketDataFormat.Unprotect(cookie.Value)!.Principal.Claims.Single().Value;
            return await Bff.Services.GetRequiredService<ITicketStore>().RetrieveAsync(key);
        }
    }

    private async Task<Session> LoginAsync(string user = "alice")
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var identity = new SecurityIdentityHost(fixture);
        var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler());
        var bff = new SecurityBffHost(fixture, gateway.Server.CreateHandler(), time: time);
        var browser = new TestBrowser(bff);
        await browser.LoginAsync(fixture, user);
        return new Session { Identity = identity, Gateway = gateway, Bff = bff, Browser = browser, Time = time };
    }

    private async Task<HttpStatusCode> RefreshGrantAsync(string refreshToken)
    {
        using var http = new HttpClient(fixture.CreateProviderHandler());
        var response = await http.PostAsync(fixture.Authority + "/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "inklink-bff",
            ["client_secret"] = fixture.BffSecret,
            ["refresh_token"] = refreshToken,
        }));
        return response.StatusCode;
    }

    [Fact]
    public async Task Access_token_is_renewed_on_the_server_and_the_refresh_token_rotates()
    {
        using var session = await LoginAsync();
        var before = (await session.ServerTicketAsync())!.Properties;
        var oldAccess = before.GetTokenValue("access_token");
        var oldRefresh = before.GetTokenValue("refresh_token")!;

        session.Time.Advance(TimeSpan.FromMinutes(4.5));
        Assert.Equal(HttpStatusCode.OK, (await session.Browser.GetAsync("/bff/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Browser.GetAsync("/api/v1/profiles/me")).StatusCode);

        var after = (await session.ServerTicketAsync())!.Properties;
        Assert.NotEqual(oldAccess, after.GetTokenValue("access_token"));
        Assert.NotEqual(oldRefresh, after.GetTokenValue("refresh_token"));
        // O ticket serializa datas com precisão de segundos.
        Assert.InRange(after.ExpiresUtc!.Value, session.Time.GetUtcNow().AddMinutes(30).AddSeconds(-1), session.Time.GetUtcNow().AddMinutes(30));
        // Uso único: o refresh token anterior já não é aceito pelo provedor.
        Assert.Equal(HttpStatusCode.BadRequest, await RefreshGrantAsync(oldRefresh));
    }

    [Fact]
    public async Task Session_slides_with_use_and_expires_after_30_minutes_idle()
    {
        using var session = await LoginAsync();
        session.Time.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await session.Browser.GetAsync("/bff/session")).StatusCode);
        session.Time.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await session.Browser.GetAsync("/bff/session")).StatusCode);
        session.Time.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Browser.GetAsync("/bff/session")).StatusCode);
    }

    [Fact]
    public async Task Continuous_use_ends_at_the_8_hour_limit()
    {
        using var session = await LoginAsync("bob");
        for (var elapsed = TimeSpan.FromMinutes(25); elapsed < TimeSpan.FromHours(8); elapsed += TimeSpan.FromMinutes(25))
        {
            session.Time.Advance(TimeSpan.FromMinutes(25));
            Assert.Equal(HttpStatusCode.OK, (await session.Browser.GetAsync("/bff/session")).StatusCode);
        }
        var ticket = (await session.ServerTicketAsync())!.Properties;
        Assert.True(ticket.ExpiresUtc <= session.Time.Start.AddHours(8));
        session.Time.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Browser.GetAsync("/bff/session")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_requests_near_expiry_share_one_renewal()
    {
        using var session = await LoginAsync("carol");
        session.Time.Advance(TimeSpan.FromMinutes(4.5));
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => session.Browser.GetAsync("/bff/session")));
        Assert.All(results, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        // A cadeia de renovação continua válida depois da corrida.
        session.Time.Advance(TimeSpan.FromMinutes(4.5));
        Assert.Equal(HttpStatusCode.OK, (await session.Browser.GetAsync("/bff/session")).StatusCode);
    }

    [Fact]
    public async Task Revoked_refresh_token_ends_the_session_without_exposing_data()
    {
        using var session = await LoginAsync();
        var refresh = (await session.ServerTicketAsync())!.Properties.GetTokenValue("refresh_token")!;
        using (var http = new HttpClient(fixture.CreateProviderHandler()))
        {
            var revoked = await http.PostAsync(fixture.Authority + "/protocol/openid-connect/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "inklink-bff",
                ["client_secret"] = fixture.BffSecret,
                ["token"] = refresh,
                ["token_type_hint"] = "refresh_token",
            }));
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        }
        session.Time.Advance(TimeSpan.FromMinutes(4.5));
        var response = await session.Browser.GetAsync("/api/v1/profiles/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("session.required", await response.Content.ReadAsStringAsync());
        Assert.Null(await session.ServerTicketAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Browser.GetAsync("/bff/session")).StatusCode);
    }
}

/// <summary>Relógio controlado do BFF; o Keycloak continua usando o tempo real.</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;
    public DateTimeOffset Start { get; } = start;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan delta) => now = now.Add(delta);
}
