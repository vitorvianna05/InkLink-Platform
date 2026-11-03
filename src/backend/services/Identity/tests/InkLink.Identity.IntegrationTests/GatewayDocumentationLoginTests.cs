using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace InkLink.Identity.IntegrationTests;

[Collection("security")]
public sealed class GatewayDocumentationLoginTests(SecurityFixture fixture)
{
    [Fact]
    public async Task Swagger_client_uses_real_pkce_and_authorized_requests_reach_service_through_gateway()
    {
        var callback = "http://127.0.0.1:5191/swagger/oauth2-redirect.html";
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Guid.NewGuid().ToString("N");
        using var provider = new HttpClient(fixture.CreateProviderHandler(new CookieContainer()));
        var url = QueryHelpers.AddQueryString(fixture.Authority + "/protocol/openid-connect/auth", new Dictionary<string, string?>
        {
            ["client_id"] = "inklink-swagger",
            ["redirect_uri"] = callback,
            ["response_type"] = "code",
            ["scope"] = "openid profile",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["nonce"] = Guid.NewGuid().ToString("N")
        });
        var login = await provider.GetAsync(url);
        var form = Regex.Match(await login.Content.ReadAsStringAsync(), "<form[^>]*action=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(form.Success, "Keycloak login form missing.");
        var submit = await provider.PostAsync(WebUtility.HtmlDecode(form.Groups[1].Value), new FormUrlEncodedContent(new Dictionary<string, string>
        { ["username"] = "alice", ["password"] = fixture.Password, ["credentialId"] = "" }));
        Assert.Equal(HttpStatusCode.Redirect, submit.StatusCode);
        var query = QueryHelpers.ParseQuery(submit.Headers.Location!.Query);
        Assert.Equal(state, query["state"].ToString());
        using var exchange = new HttpRequestMessage(HttpMethod.Post, fixture.Authority + "/protocol/openid-connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "inklink-swagger",
                ["redirect_uri"] = callback,
                ["code"] = query["code"].ToString(),
                ["code_verifier"] = verifier
            })
        };
        exchange.Headers.Add("Origin", "http://127.0.0.1:5191");
        var response = await provider.SendAsync(exchange); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("http://127.0.0.1:5191", response.Headers.GetValues("Access-Control-Allow-Origin"));
        var token = (await response.Content.ReadFromJsonAsync<JsonObject>())!["access_token"]!.GetValue<string>();
        using var identity = new SecurityIdentityHost(fixture);
        using var gateway = new SecurityGatewayHost(fixture, identity.Server.CreateHandler());
        using var http = gateway.CreateClient(); http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var profile = await http.GetAsync("/api/v1/profiles/me"); Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var body = (await profile.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await http.PutAsJsonAsync("/api/v1/profiles/me", new { displayName = body["displayName"]!.GetValue<string>() })).StatusCode);
        var forbidden = await provider.PostAsync(fixture.Authority + "/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["grant_type"] = "password", ["client_id"] = "inklink-swagger", ["username"] = "alice", ["password"] = fixture.Password }));
        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
    }
}
