using InkLink.Catalog.Application.Queries;
using InkLink.Catalog.Domain;
using InkLink.Catalog.Infrastructure.Persistence;

namespace InkLink.Catalog.ArchitectureTests;

public sealed class LayerTests
{
    [Fact]
    public void Domain_depends_only_on_base_libraries() => Assert.All(typeof(Artist).Assembly.GetReferencedAssemblies(), reference => Assert.StartsWith("System.", reference.Name!));

    [Fact]
    public void Application_depends_only_on_domain_and_base_libraries() => Assert.All(typeof(ReadCatalog).Assembly.GetReferencedAssemblies(), reference =>
        Assert.True(reference.Name!.StartsWith("System.", StringComparison.Ordinal) || reference.Name == "InkLink.Catalog.Domain", reference.Name));

    [Fact]
    public void Infrastructure_does_not_depend_on_http_host() => Assert.DoesNotContain(typeof(CatalogDbContext).Assembly.GetReferencedAssemblies(), reference => reference.Name == "InkLink.Catalog.Api");

    [Fact]
    public void Catalog_has_no_reference_to_Identity_implementation()
    {
        foreach (var assembly in new[] { typeof(Artist).Assembly, typeof(ReadCatalog).Assembly, typeof(CatalogDbContext).Assembly, typeof(CatalogProgram).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("InkLink.Identity.", StringComparison.Ordinal));
    }
}
