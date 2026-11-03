using System.Net;
using System.Net.Http.Json;
using InkLink.Finance.Application.Ledger;
using InkLink.Identity.IntegrationTests;

namespace InkLink.Finance.IntegrationTests;

public sealed class FinanceIdentityTests
{
    [Fact]
    public async Task Real_Keycloak_identity_authorizes_artist_and_rejects_client_role()
    {
        var provider = new SecurityFixture();
        var fixture = new FinanceFixture();
        try
        {
            await provider.InitializeAsync();
            fixture.Base.Base.UseRealIdentity(provider);
            await fixture.InitializeAsync();
            using var artist = fixture.Client("bob");
            var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/entries")
            { Content = JsonContent.Create(new EntryInput("Cost", "Custo gerencial", 100, day)) };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var response = await artist.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var entry = (await response.Content.ReadFromJsonAsync<EntryView>())!;
            Assert.Equal(fixture.Base.Base.Accounts["bob"], entry.ArtistId);
            using var client = fixture.Client("carol");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/finance/entries/{entry.Id}")).StatusCode);
        }
        finally
        {
            await fixture.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}
