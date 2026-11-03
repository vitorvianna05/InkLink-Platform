using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InkLink.Finance.Application;
using InkLink.Finance.Application.Accounts;

namespace InkLink.Finance.Infrastructure.Integration;

public sealed class IdentityDirectory(HttpClient http) : IIdentityDirectory
{
    public async Task<FinanceAccount> ResolveAsync(string credential, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/profiles/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new FinanceFailure(FinanceError.Unauthenticated, "authentication.required");
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new FinanceFailure(FinanceError.Forbidden, "authorization.denied");
            if (!response.IsSuccessStatusCode) throw new FinanceFailure(FinanceError.Unavailable, "finance.identity_unavailable");
            var account = await response.Content.ReadFromJsonAsync<IdentityProfile>(cancellationToken);
            if (account is null || account.Id == Guid.Empty || account.Roles is null || !account.Roles.Contains("Client", StringComparer.Ordinal))
                throw new FinanceFailure(FinanceError.Unavailable, "finance.identity_unavailable");
            return new(account.Id, account.Roles.Contains("Artist", StringComparer.Ordinal));
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new FinanceFailure(FinanceError.Unavailable, "finance.identity_unavailable");
        }
    }

    private sealed record IdentityProfile(Guid Id, string DisplayName, string[] Roles);
}
