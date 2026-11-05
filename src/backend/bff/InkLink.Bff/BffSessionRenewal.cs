using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InkLink.Bff;

/// <summary>
/// Sessão renovável (decisão 0019): 30 minutos sem uso, 8 horas no total e access token renovado no
/// servidor com refresh token de uso único, serializado por sessão entre instâncias do BFF.
/// </summary>
public sealed class BffSessionRenewal(
    IDbContextFactory<BffDbContext> databases,
    ITicketStore store,
    IOptionsMonitor<OpenIdConnectOptions> oidc,
    TimeProvider time,
    ILogger<BffSessionRenewal> logger)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromSeconds(60);
    public const string SessionKeyItem = "inklink.sid";
    public const string AbsoluteExpiryItem = "inklink.absolute";

    /// <summary>Validade inicial, gravada no login.</summary>
    public static void Start(AuthenticationProperties properties, DateTimeOffset now)
    {
        var absolute = now.Add(AbsoluteLifetime);
        properties.IssuedUtc = now;
        properties.Items[AbsoluteExpiryItem] = absolute.ToString("O", CultureInfo.InvariantCulture);
        properties.ExpiresUtc = Min(now.Add(IdleTimeout), absolute);
        properties.AllowRefresh = false;
    }

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var properties = context.Properties;
        var now = time.GetUtcNow();
        if (!TryDate(Item(properties, AbsoluteExpiryItem), out var absolute) || now >= absolute)
        {
            await RejectAsync(context);
            return;
        }
        var renewed = false;
        if (!TryDate(properties.GetTokenValue("expires_at"), out var accessExpires) || accessExpires - now <= RefreshBefore)
        {
            var outcome = await RefreshAsync(properties, now, context.HttpContext.RequestAborted);
            if (outcome == RefreshOutcome.Ended)
            {
                await RejectAsync(context);
                return;
            }
            renewed = outcome == RefreshOutcome.Renewed;
        }
        var expires = Min(now.Add(IdleTimeout), absolute);
        if (renewed || properties.ExpiresUtc is null || expires - properties.ExpiresUtc.Value >= TimeSpan.FromMinutes(1))
        {
            // O handler de cookie recalcula a validade como agora + (expiração - emissão); a emissão precisa
            // acompanhar a renovação para que a janela continue em 30 minutos e respeite o limite total.
            properties.IssuedUtc = now;
            properties.ExpiresUtc = expires;
            context.ShouldRenew = true;
        }
    }

    private async Task<RefreshOutcome> RefreshAsync(AuthenticationProperties properties, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (Item(properties, SessionKeyItem) is not { Length: > 0 } key) return RefreshOutcome.Ended;
        await using var database = await databases.CreateDbContextAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Serializa a renovação desta sessão em todas as instâncias; o bloqueio termina com a transação.
        await database.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var latest = await store.RetrieveAsync(key);
        if (latest is null) return RefreshOutcome.Ended;
        if (TryDate(latest.Properties.GetTokenValue("expires_at"), out var latestExpires) && latestExpires - now > RefreshBefore)
        {
            // Outra requisição já renovou: reaproveita os tokens gravados.
            properties.StoreTokens(latest.Properties.GetTokens());
            return RefreshOutcome.Renewed;
        }
        var refreshToken = latest.Properties.GetTokenValue("refresh_token");
        if (string.IsNullOrEmpty(refreshToken)) return RefreshOutcome.Ended;

        var options = oidc.Get("oidc");
        Dictionary<string, JsonElement>? body;
        try
        {
            var configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
            using var response = await options.Backchannel.PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
            }), cancellationToken);
            body = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = body?.GetValueOrDefault("error").ToString();
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && error == "invalid_grant")
                {
                    logger.LogInformation("Session refresh rejected by the identity provider; the session ends.");
                    await store.RemoveAsync(key);
                    await transaction.CommitAsync(cancellationToken);
                    return RefreshOutcome.Ended;
                }
                logger.LogWarning("Session refresh failed with status {StatusCode}; the current token is kept.", (int)response.StatusCode);
                return RefreshOutcome.Unavailable;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            logger.LogWarning("Session refresh is temporarily unavailable; the current token is kept.");
            return RefreshOutcome.Unavailable;
        }

        if (body is null || !body.TryGetValue("access_token", out var access) || !body.TryGetValue("expires_in", out var expiresIn)
            || !body.TryGetValue("refresh_token", out var rotated))
            return RefreshOutcome.Unavailable;
        var tokens = latest.Properties.GetTokens().ToDictionary(token => token.Name, token => token.Value);
        tokens["access_token"] = access.GetString()!;
        tokens["refresh_token"] = rotated.GetString()!;
        if (body.TryGetValue("id_token", out var idToken) && idToken.GetString() is { Length: > 0 } id) tokens["id_token"] = id;
        tokens["expires_at"] = now.AddSeconds(expiresIn.GetInt32()).ToString("O", CultureInfo.InvariantCulture);
        var updated = tokens.Select(pair => new AuthenticationToken { Name = pair.Key, Value = pair.Value }).ToArray();
        properties.StoreTokens(updated);
        latest.Properties.StoreTokens(updated);
        latest.Properties.ExpiresUtc = properties.ExpiresUtc;
        // Grava antes de liberar o bloqueio, para que a próxima requisição use o refresh token novo.
        await store.RenewAsync(key, latest);
        await transaction.CommitAsync(cancellationToken);
        return RefreshOutcome.Renewed;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync("session");
    }

    private static string? Item(AuthenticationProperties properties, string name) =>
        properties.Items.TryGetValue(name, out var value) ? value : null;

    private static bool TryDate(string? value, out DateTimeOffset date) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    private enum RefreshOutcome { Renewed, Unavailable, Ended }
}
