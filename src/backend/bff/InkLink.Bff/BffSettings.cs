using System.Security.Cryptography.X509Certificates;
using Npgsql;

namespace InkLink.Bff;

public sealed class BffSettings
{
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string GatewayAddress { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string CertificatePath { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
    public bool AllowHttpMetadata { get; set; }
    public static BffSettings Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = configuration.GetSection("Bff").Get<BffSettings>() ?? new();
        var local = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        if (new[] { settings.Authority, settings.ClientId, settings.ClientSecret, settings.GatewayAddress, settings.ConnectionString,
            settings.CertificatePath, settings.CertificatePassword }.Any(string.IsNullOrWhiteSpace)
            || (settings.AllowHttpMetadata && !local)
            || !Uri.TryCreate(settings.Authority, UriKind.Absolute, out var authority)
            || !Uri.TryCreate(settings.GatewayAddress, UriKind.Absolute, out var gateway)
            || (authority.Scheme != "https" && !(local && settings.AllowHttpMetadata && authority.Scheme == "http"))
            || (gateway.Scheme != "https" && !(local && gateway.Scheme == "http")))
            throw new InvalidOperationException("Bff configuration requires OIDC, upstream, private database and a shared protection certificate; HTTPS is required outside local/test environments.");
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database)
                || string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password))
                throw new ArgumentException();
        }
        catch (ArgumentException) { throw new InvalidOperationException("Bff database configuration is invalid."); }
        return settings;
    }
    public X509Certificate2 LoadCertificate()
    {
        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(CertificatePath, CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow) throw new InvalidOperationException();
            return certificate;
        }
        catch (Exception) { throw new InvalidOperationException("Bff protection certificate is unavailable or invalid."); }
    }
}
