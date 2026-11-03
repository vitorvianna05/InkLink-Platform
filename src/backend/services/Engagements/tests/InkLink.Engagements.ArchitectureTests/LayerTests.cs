using InkLink.Engagements.Application.Negotiations;
using InkLink.Engagements.Domain;
using InkLink.Engagements.Infrastructure.Persistence;

namespace InkLink.Engagements.ArchitectureTests;

public sealed class LayerTests
{
    [Fact]
    public void Domain_depends_only_on_base_libraries() => Assert.All(typeof(Engagement).Assembly.GetReferencedAssemblies(), reference => Assert.StartsWith("System.", reference.Name!));

    [Fact]
    public void Application_depends_only_on_domain_and_base_libraries() => Assert.All(typeof(ManageEngagements).Assembly.GetReferencedAssemblies(), reference =>
        Assert.True(reference.Name!.StartsWith("System.", StringComparison.Ordinal) || reference.Name == "InkLink.Engagements.Domain", reference.Name));

    [Fact]
    public void Infrastructure_does_not_depend_on_host() => Assert.DoesNotContain(typeof(EngagementsDbContext).Assembly.GetReferencedAssemblies(), r => r.Name == "InkLink.Engagements.Api");

    [Fact]
    public void No_product_layer_references_other_service_implementations()
    {
        foreach (var assembly in new[] { typeof(Engagement).Assembly, typeof(ManageEngagements).Assembly, typeof(EngagementsDbContext).Assembly, typeof(EngagementsProgram).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), r => new[] { "InkLink.Identity.", "InkLink.Catalog.", "InkLink.Messaging.", "InkLink.Scheduling." }.Any(prefix => r.Name!.StartsWith(prefix, StringComparison.Ordinal)));
    }
}
