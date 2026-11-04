using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace InkLink.ObjectStorage;

public static class ObjectStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registra o adaptador S3 a partir da seção informada (normalmente "ObjectStorage"), validando a
    /// configuração na inicialização. Credenciais vêm de User Secrets ou do ambiente, nunca do repositório.
    /// </summary>
    public static IServiceCollection AddObjectStorage(this IServiceCollection services, IConfiguration section, bool localEnvironment)
    {
        services.AddOptions<ObjectStorageOptions>()
            .Bind(section)
            .Validate(options => { options.Validate(localEnvironment); return true; })
            .ValidateOnStart();
        services.AddSingleton<S3ObjectStorage>();
        services.AddSingleton<IObjectStorage>(provider => provider.GetRequiredService<S3ObjectStorage>());
        return services;
    }

    /// <summary>Prontidão do armazenamento: credenciais aceitas e bucket público acessível.</summary>
    public static IHealthChecksBuilder AddObjectStorageCheck(this IHealthChecksBuilder builder, string name = "object-storage") =>
        builder.AddCheck<ObjectStorageHealthCheck>(name, tags: ["ready"]);
}

public sealed class ObjectStorageHealthCheck(IOptions<ObjectStorageOptions> options) : IHealthCheck, IDisposable
{
    private readonly AmazonS3Client client = S3ObjectStorage.CreateClient(options.Value);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Um objeto inexistente responde 404 com credenciais válidas; 403 indica credencial ou permissão incorreta.
            await client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = options.Value.PublicBucket, Key = "health/probe" }, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is Amazon.Runtime.AmazonServiceException or Amazon.Runtime.AmazonClientException or HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Object storage is unavailable or rejected the service credentials.");
        }
    }

    public void Dispose() => client.Dispose();
}
