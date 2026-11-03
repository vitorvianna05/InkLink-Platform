using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InkLink.Catalog.Application;
using InkLink.Catalog.Application.Accounts;

namespace InkLink.Catalog.Infrastructure.Integration;

public sealed class IdentityDirectory(HttpClient http) : IIdentityDirectory
{
    public async Task<CatalogAccount> ResolveAsync(string credential, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/profiles/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new CatalogFailure(CatalogError.Unauthenticated, "authentication.required");
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new CatalogFailure(CatalogError.Forbidden, "authorization.denied");
            if (!response.IsSuccessStatusCode) throw new CatalogFailure(CatalogError.Unavailable, "catalog.identity_unavailable");
            var account = await response.Content.ReadFromJsonAsync<IdentityProfile>(cancellationToken);
            if (account is null || account.Id == Guid.Empty || account.Roles is null || !account.Roles.Contains("Client", StringComparer.Ordinal))
                throw new CatalogFailure(CatalogError.Unavailable, "catalog.identity_unavailable");
            return new(account.Id, account.Roles.Contains("Artist", StringComparer.Ordinal));
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new CatalogFailure(CatalogError.Unavailable, "catalog.identity_unavailable");
        }
    }

    private sealed record IdentityProfile(Guid Id, string DisplayName, string[] Roles);
}
