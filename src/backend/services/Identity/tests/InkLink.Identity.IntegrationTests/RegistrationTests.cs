using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using InkLink.Identity.Application.Registration;
using InkLink.Identity.Domain;
using InkLink.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Identity.IntegrationTests;

/// <summary>Cadastro pelo próprio usuário e confirmação de contato por código de 8 dígitos (decisão 0020, T10.23).</summary>
[Collection("security")]
public sealed class RegistrationTests(SecurityFixture fixture)
{
    [Fact]
    public async Task Self_registered_account_stays_pending_until_terms_email_and_phone_are_confirmed()
    {
        var messenger = new CapturingMessenger();
        using var host = new RegistrationHost(fixture, messenger, new ManualTime(DateTimeOffset.UtcNow));
        using var client = await SignInAsync(host, "pending");

        // Conta pendente: o perfil (base de todos os serviços) e as escritas do perfil são recusados.
        var me = await client.GetAsync("/api/v1/profiles/me");
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal("account.confirmation_required", (await me.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/v1/profiles/me", new { displayName = "Outro" })).StatusCode);
        var status = await ReadAsync(client);
        Assert.Equal("Pending", status["state"]!.GetValue<string>());
        Assert.Equal("pending@example.test", status["email"]!.GetValue<string>());
        Assert.False(status["termsAccepted"]!.GetValue<bool>());
        var terms = status["termsVersion"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/v1/registration/terms", new { version = "antiga" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/registration/terms", new { version = terms })).StatusCode);
        var invalidPhone = await client.PutAsJsonAsync("/api/v1/registration/phone", new { phoneNumber = "12345" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPhone.StatusCode);
        var phone = await (await client.PutAsJsonAsync("/api/v1/registration/phone", new { phoneNumber = "(11) 98765-4321" })).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("+5511987654321", phone!["phoneNumber"]!.GetValue<string>());

        // E-mail: código enviado ao endereço do token, reenvio imediato recusado, código errado recusado.
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(client, "email")).StatusCode);
        var emailCode = messenger.Last(ContactChannel.Email, "pending@example.test");
        Assert.Matches("^[0-9]{8}$", emailCode);
        var tooSoon = await SendAsync(client, "email");
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        Assert.True(tooSoon.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        var wrong = await VerifyAsync(client, "email", emailCode == "00000000" ? "11111111" : "00000000");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("registration.code_invalid", (await wrong.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(client, "email", emailCode)).StatusCode);
        // Uso único: o mesmo código não confirma de novo.
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, "email")).StatusCode);
        Assert.Equal("Pending", (await ReadAsync(client))["state"]!.GetValue<string>());

        // Celular: o código do e-mail não vale para o celular; o código certo conclui o cadastro.
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(client, "phone")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, "phone", emailCode)).StatusCode);
        var active = await (await VerifyAsync(client, "phone", messenger.Last(ContactChannel.Phone, "+5511987654321"))).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("Active", active!["state"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profiles/me")).StatusCode);

        // Artist depende de aprovação: a solicitação fica pendente e aparece para a ferramenta administrativa.
        var request = await (await client.PostAsync("/api/v1/registration/artist-request", null)).Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("Pending", request!["artistRequest"]!.GetValue<string>());
        using (var scope = host.Services.CreateScope())
        {
            var pending = await scope.ServiceProvider.GetRequiredService<IArtistRequestReader>().PendingAsync(default);
            Assert.Contains(pending, item => item.DisplayName.Contains("pending", StringComparison.OrdinalIgnoreCase));
        }

        // Só o resumo do código é guardado.
        await using var database = fixture.Database.CreateOwnerContext();
        var hashes = await database.VerificationCodes.Select(code => code.CodeHash).ToListAsync();
        Assert.DoesNotContain(emailCode, hashes);
        Assert.All(hashes, hash => Assert.Matches("^[0-9a-f]{64}$", hash));
    }

    [Fact]
    public async Task Codes_expire_limit_attempts_and_sends_and_a_confirmed_phone_belongs_to_one_account()
    {
        var messenger = new CapturingMessenger();
        var time = new ManualTime(DateTimeOffset.UtcNow);
        using var host = new RegistrationHost(fixture, messenger, time);
        using var client = await SignInAsync(host, "pending2");
        await client.PutAsJsonAsync("/api/v1/registration/phone", new { phoneNumber = "+5521912345678" });

        // Validade de 10 minutos.
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(client, "phone")).StatusCode);
        var expired = messenger.Last(ContactChannel.Phone, "+5521912345678");
        time.Advance(TimeSpan.FromMinutes(11));
        var late = await VerifyAsync(client, "phone", expired);
        Assert.Equal("registration.code_expired", (await late.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());

        // Cinco tentativas: depois delas, nem o código certo confirma.
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(client, "phone")).StatusCode);
        var code = messenger.Last(ContactChannel.Phone, "+5521912345678");
        var wrong = code == "00000000" ? "11111111" : "00000000";
        for (var attempt = 1; attempt < VerificationCode.MaxAttempts; attempt++) Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, "phone", wrong)).StatusCode);
        var exhausted = await VerifyAsync(client, "phone", wrong);
        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, "phone", code)).StatusCode);

        // Cinco envios por hora por canal, com 60 s entre eles; o sexto é recusado até a janela abrir.
        for (var send = 3; send <= VerificationCode.MaxSendsPerHour; send++)
        {
            time.Advance(TimeSpan.FromSeconds(61));
            Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(client, "phone")).StatusCode);
        }
        time.Advance(TimeSpan.FromSeconds(61));
        var limit = await SendAsync(client, "phone");
        Assert.Equal(HttpStatusCode.TooManyRequests, limit.StatusCode);
        Assert.Equal("registration.send_limit", (await limit.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());

        // O último código ainda vale e confirma o número; outra conta não confirma o mesmo número.
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(client, "phone", messenger.Last(ContactChannel.Phone, "+5521912345678"))).StatusCode);
        using var other = await SignInAsync(host, "carol");
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/v1/profiles/me")).StatusCode); // conta administrativa: ativa
        await other.PutAsJsonAsync("/api/v1/registration/phone", new { phoneNumber = "+55 21 91234-5678" });
        Assert.Equal(HttpStatusCode.Accepted, (await SendAsync(other, "phone")).StatusCode);
        var taken = await VerifyAsync(other, "phone", messenger.Last(ContactChannel.Phone, "+5521912345678"));
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal("registration.code_invalid", (await taken.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unavailable_channel_does_not_issue_a_usable_code_and_pending_account_cannot_request_artist()
    {
        var messenger = new CapturingMessenger { Unavailable = true };
        using var host = new RegistrationHost(fixture, messenger, new ManualTime(DateTimeOffset.UtcNow));
        using var client = await SignInAsync(host, "pending2");
        var failed = await SendAsync(client, "email");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal("registration.channel_unavailable", (await failed.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, "fax")).StatusCode);
        using var anonymous = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/registration")).StatusCode);
        var state = await ReadAsync(client);
        if (state["state"]!.GetValue<string>() == "Pending")
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/v1/registration/artist-request", null)).StatusCode);
    }

    private async Task<HttpClient> SignInAsync(RegistrationHost host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await fixture.TokenAsync(user))["access_token"]!.GetValue<string>());
        return client;
    }

    private static async Task<JsonObject> ReadAsync(HttpClient client) => (await client.GetFromJsonAsync<JsonObject>("/api/v1/registration"))!;
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string channel) => client.PostAsJsonAsync("/api/v1/registration/codes", new { channel });
    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string channel, string code) => client.PostAsJsonAsync("/api/v1/registration/codes/verify", new { channel, code });
}

/// <summary>Canal de envio que guarda os códigos para o teste (substitui SMTP e SMS).</summary>
internal sealed class CapturingMessenger : IContactMessenger
{
    private readonly ConcurrentQueue<(ContactChannel Channel, string Target, string Code)> sent = new();
    public bool Unavailable { get; init; }

    public Task SendAsync(ContactChannel channel, string target, string code, CancellationToken cancellationToken)
    {
        if (Unavailable) throw new ContactUnavailable("Test channel unavailable.");
        sent.Enqueue((channel, target, code));
        return Task.CompletedTask;
    }

    public string Last(ContactChannel channel, string target) => sent.Last(item => item.Channel == channel && item.Target == target).Code;
}

internal sealed class RegistrationHost(SecurityFixture fixture, IContactMessenger messenger, TimeProvider time) : WebApplicationFactory<IdentityProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing")
        .UseSecuritySettings(SecurityConfiguration.Identity(fixture))
        .ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>("Bearer", options => options.BackchannelHttpHandler = fixture.CreateProviderHandler());
            services.AddSingleton(messenger);
            services.AddSingleton(time);
        });
}
