using InkLink.Scheduling.Application.Reservations;
using InkLink.Scheduling.Domain;
using InkLink.Scheduling.Infrastructure.Persistence;

namespace InkLink.Scheduling.ArchitectureTests;

public sealed class LayerTests
{
    [Fact]
    public void Domain_remains_independent() => Assert.All(typeof(InkLink.Scheduling.Domain.ArtistCalendar).Assembly.GetReferencedAssemblies(), reference => Assert.StartsWith("System.", reference.Name!));
    [Fact]
    public void Application_depends_only_on_domain_and_base_libraries() => Assert.All(typeof(ManageSchedule).Assembly.GetReferencedAssemblies(), reference =>
        Assert.True(reference.Name!.StartsWith("System.", StringComparison.Ordinal) || reference.Name == "InkLink.Scheduling.Domain", reference.Name));
    [Fact]
    public void Infrastructure_does_not_depend_on_api() => Assert.DoesNotContain(typeof(SchedulingDbContext).Assembly.GetReferencedAssemblies(), reference => reference.Name == "InkLink.Scheduling.Api");
    [Fact]
    public void Services_do_not_share_business_implementation()
    {
        foreach (var assembly in new[] { typeof(InkLink.Scheduling.Domain.ArtistCalendar).Assembly, typeof(ManageSchedule).Assembly, typeof(SchedulingDbContext).Assembly, typeof(SchedulingProgram).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => new[] { "InkLink.Engagements.", "InkLink.Identity.", "InkLink.Catalog.", "InkLink.Messaging." }.Any(prefix => reference.Name!.StartsWith(prefix, StringComparison.Ordinal)));
    }
}
