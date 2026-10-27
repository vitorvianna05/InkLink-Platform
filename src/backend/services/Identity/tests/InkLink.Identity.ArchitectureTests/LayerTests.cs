using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using InkLink.Identity.Application;
using InkLink.Identity.Domain;
using InkLink.Identity.Infrastructure;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace InkLink.Identity.ArchitectureTests;

public sealed class LayerTests
{
    private static readonly Architecture Architecture = new ArchLoader().LoadAssemblies(
        typeof(Profile).Assembly, typeof(GetMyProfile).Assembly,
        typeof(IdentityDbContext).Assembly, typeof(Program).Assembly, typeof(ForbiddenFixture).Assembly).Build();

    [Theory]
    [InlineData("InkLink.Identity.Domain", "InkLink.Identity.Application")]
    [InlineData("InkLink.Identity.Domain", "InkLink.Identity.Infrastructure")]
    [InlineData("InkLink.Identity.Domain", "InkLink.Identity.Api")]
    [InlineData("InkLink.Identity.Application", "InkLink.Identity.Infrastructure")]
    [InlineData("InkLink.Identity.Application", "InkLink.Identity.Api")]
    [InlineData("InkLink.Identity.Infrastructure", "InkLink.Identity.Api")]
    public void Dependencies_point_inward(string source, string forbidden) =>
        Types().That().ResideInAssembly(System.Reflection.Assembly.Load(source).FullName!).Should()
            .NotDependOnAny(Types().That().ResideInAssembly(System.Reflection.Assembly.Load(forbidden).FullName!)).Check(Architecture);

    [Fact]
    public void Domain_has_no_package_or_framework_dependency()
    {
        var references = typeof(Profile).Assembly.GetReferencedAssemblies();
        Assert.All(references, reference => Assert.StartsWith("System.", reference.Name!));
    }

    [Fact]
    public void Application_depends_only_on_domain_and_base_libraries()
    {
        var references = typeof(GetMyProfile).Assembly.GetReferencedAssemblies();
        Assert.All(references, reference => Assert.True(reference.Name!.StartsWith("System.", StringComparison.Ordinal)
            || reference.Name == "InkLink.Identity.Domain", reference.Name));
    }

    [Fact]
    public void Controlled_forbidden_dependency_is_detected()
    {
        var rule = Types().That().HaveNameContaining(nameof(ForbiddenFixture)).Should()
            .NotDependOnAny(Types().That().ResideInAssembly(typeof(IdentityDbContext).Assembly.FullName!));
        Assert.False(rule.HasNoViolations(Architecture));
        Assert.ThrowsAny<Exception>(() => rule.Check(Architecture));
    }
}

// This type is only a negative fixture; it must never enter a production assembly.
public sealed class ForbiddenFixture(IdentityDbContext database)
{
    public IdentityDbContext Database { get; } = database;
}
