using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using InkLink.Bff;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InkLink.Identity.IntegrationTests;

[Collection("security")]
public sealed class BffTests(SecurityFixture fixture)
{
    [Fact]
    public async Task Login_uses_pkce_and_state_and_anonymous_session_is_rejected()
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler());
        using var bff = new SecurityBffHost(fixture, gateway.Server.CreateHandler());
        using var browser = new TestBrowser(bff);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/bff/session")).StatusCode);
        var challenge = await browser.GetAsync("/bff/login");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var oidc = bff.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("oidc");
        Assert.True(oidc.UsePkce);
        Assert.Equal("code", oidc.ResponseType);
        Assert.True(challenge.Headers.Location!.Query.Contains("request_uri=", StringComparison.Ordinal)
            || challenge.Headers.Location.Query.Contains("code_challenge_method=S256", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/signin-oidc?code=forged&state=invalid")).StatusCode);
    }

    [Fact]
    public async Task Real_login_keeps_tokens_server_side_and_session_works_across_instances_with_csrf_and_logout()
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler());
        using var first = new SecurityBffHost(fixture, gateway.Server.CreateHandler());
        using var second = new SecurityBffHost(fixture, gateway.Server.CreateHandler());
        using var browser = new TestBrowser(first);
        await browser.LoginAsync(fixture, "alice");
        var session = await browser.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var text = await session.Content.ReadAsStringAsync();
        Assert.DoesNotContain("access_token", text);
        Assert.DoesNotContain("refresh_token", text);
        var cookie = browser.Cookies.GetCookies(TestBrowser.Origin)["__Host-inklink-session"]!;
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        var options = first.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("session");
        var clientTicket = options.TicketDataFormat.Unprotect(cookie.Value)!;
        Assert.Null(clientTicket.Properties.GetTokenValue("access_token"));
        var sessionId = clientTicket.Principal.Claims.Single().Value;
        var serverTicket = await first.Services.GetRequiredService<ITicketStore>().RetrieveAsync(sessionId);
        var accessToken = serverTicket!.Properties.GetTokenValue("access_token")!;
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.True(serverTicket.Properties.ExpiresUtc <= new DateTimeOffset(new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(accessToken).ValidTo));
        await using (var database = await first.Services.GetRequiredService<IDbContextFactory<BffDbContext>>().CreateDbContextAsync())
        {
            var row = await database.Sessions.SingleAsync(row => row.Id == sessionId);
            Assert.DoesNotContain(accessToken, Encoding.UTF8.GetString(row.ProtectedTicket));
        }
        using var secondClient = second.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = TestBrowser.Origin, HandleCookies = false, AllowAutoRedirect = false });
        var replayCookie = "__Host-inklink-session=" + cookie.Value;
        secondClient.DefaultRequestHeaders.Add("Cookie", replayCookie);
        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync("/bff/session")).StatusCode);
        var profile = await browser.GetAsync("/api/v1/profiles/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var missingCsrf = await browser.PutAsync("/api/v1/profiles/me", new { displayName = "Rejected" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/bff/logout")).StatusCode);
        var csrf = JsonNode.Parse(await (await browser.GetAsync("/bff/antiforgery")).Content.ReadAsStringAsync())!["requestToken"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PutAsync("/api/v1/profiles/me", new { displayName = "Alice BFF" }, csrf)).StatusCode);
        var logout = await browser.PostAsync("/bff/logout", csrf);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.StartsWith(fixture.Authority + "/protocol/openid-connect/logout", logout.Headers.Location!.AbsoluteUri);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.GetAsync("/bff/session")).StatusCode);
    }

    [Fact]
    public async Task Expired_server_session_and_tampered_cookie_are_rejected()
    {
        using var identity = new SecurityIdentityHost(fixture);
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler());
        using var bff = new SecurityBffHost(fixture, gateway.Server.CreateHandler());
        using var browser = new TestBrowser(bff);
        await browser.LoginAsync(fixture, "bob");
        var cookie = browser.Cookies.GetCookies(TestBrowser.Origin)["__Host-inklink-session"]!;
        using var client = bff.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = TestBrowser.Origin, HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", "__Host-inklink-session=invalid" + cookie.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/bff/session")).StatusCode);
        var options = bff.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("session");
        var sessionId = options.TicketDataFormat.Unprotect(cookie.Value)!.Principal.Claims.Single().Value;
        await using var database = await bff.Services.GetRequiredService<IDbContextFactory<BffDbContext>>().CreateDbContextAsync();
        await database.Sessions.Where(session => session.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/bff/session")).StatusCode);
    }
}

internal sealed class TestBrowser : IDisposable
{
    public static readonly Uri Origin = new("https://localhost:5192");
    public CookieContainer Cookies { get; } = new();
    private readonly HttpClient bff;
    public TestBrowser(SecurityBffHost host) => bff = host.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = Origin, HandleCookies = false, AllowAutoRedirect = false });
    public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(new(HttpMethod.Get, path));
    public Task<HttpResponseMessage> PostAsync(string path, string? csrf = null) => SendAsync(new(HttpMethod.Post, path), csrf);
    public Task<HttpResponseMessage> PutAsync(string path, object body, string? csrf = null) =>
        SendAsync(new(HttpMethod.Put, path) { Content = JsonContent.Create(body) }, csrf);
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string? csrf = null)
    {
        request.RequestUri = new Uri(Origin, request.RequestUri ?? throw new InvalidOperationException("Request URI is required."));
        var cookies = Cookies.GetCookieHeader(request.RequestUri);
        if (cookies.Length > 0) request.Headers.Add("Cookie", cookies);
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        var response = await bff.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            foreach (var value in values) Cookies.SetCookies(request.RequestUri, value);
        return response;
    }
    public async Task LoginAsync(SecurityFixture fixture, string user)
    {
        var challenge = await GetAsync("/bff/login");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        using var provider = new HttpClient(fixture.CreateProviderHandler(Cookies));
        var login = await provider.GetAsync(challenge.Headers.Location);
        for (var redirect = 0; redirect < 10 && login.StatusCode == HttpStatusCode.Redirect; redirect++)
        {
            var location = new Uri(login.RequestMessage!.RequestUri!, login.Headers.Location!);
            if (location.AbsoluteUri.StartsWith(Origin.AbsoluteUri, StringComparison.Ordinal)) break;
            Assert.StartsWith(fixture.Authority[..fixture.Authority.IndexOf("/realms", StringComparison.Ordinal)], location.AbsoluteUri);
            login = await provider.GetAsync(location);
        }
        var html = await login.Content.ReadAsStringAsync();
        var form = Regex.Match(html, "<form[^>]*action=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(form.Success, "Keycloak login form was not returned. Status=" + login.StatusCode + " Title=" + Regex.Match(html, "<title>(.*?)</title>", RegexOptions.Singleline).Groups[1].Value.Trim() + " Detail=" + Regex.Match(html, "<p[^>]*class=\"instruction\"[^>]*>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value.Trim() + " Redirect=" + login.Headers.Location?.GetLeftPart(UriPartial.Path) + " Error=" + (login.Headers.Location is null ? "" : Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(login.Headers.Location.Query)["error"].ToString()));
        var submit = await provider.PostAsync(WebUtility.HtmlDecode(form.Groups[1].Value),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = user, ["password"] = fixture.Password, ["credentialId"] = "" }));
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);
        var callback = await GetAsync(submit.Headers.Location!.AbsoluteUri);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
    }
    public void Dispose() => bff.Dispose();
}
