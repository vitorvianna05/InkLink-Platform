using InkLink.Finance.Application.Ledger;
using InkLink.Finance.Domain;
using InkLink.Finance.Infrastructure.Persistence;
namespace InkLink.Finance.ArchitectureTests;

public sealed class LayerTests
{
    [Fact]
    public void Domain_has_only_base_dependencies() =>
        Assert.All(typeof(LedgerEntry).Assembly.GetReferencedAssemblies(), r => Assert.StartsWith("System.", r.Name!));
    [Fact]
    public void Application_has_only_domain_and_base_dependencies() =>
        Assert.All(typeof(ManageLedger).Assembly.GetReferencedAssemblies(), r => Assert.True(r.Name!.StartsWith("System.", StringComparison.Ordinal) || r.Name == "InkLink.Finance.Domain", r.Name));
    [Fact]
    public void No_product_layer_references_other_service_implementations()
    {
        foreach (var assembly in new[] { typeof(LedgerEntry).Assembly, typeof(ManageLedger).Assembly, typeof(FinanceDbContext).Assembly, typeof(FinanceProgram).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), r => new[] { "InkLink.Identity.", "InkLink.Catalog.", "InkLink.Engagements.", "InkLink.Messaging.", "InkLink.Scheduling." }.Any(p => r.Name!.StartsWith(p, StringComparison.Ordinal)));
    }
    [Fact]
    public void Infrastructure_does_not_depend_on_host() =>
        Assert.DoesNotContain(typeof(FinanceDbContext).Assembly.GetReferencedAssemblies(), r => r.Name == "InkLink.Finance.Api");
}
