using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Domain;
using InkLink.Messaging.Infrastructure.Persistence;

namespace InkLink.Messaging.ArchitectureTests;

public sealed class LayerTests
{
    [Fact]
    public void Domain_remains_independent() => Assert.All(typeof(Conversation).Assembly.GetReferencedAssemblies(), reference => Assert.StartsWith("System.", reference.Name!));
    [Fact]
    public void Application_depends_only_on_domain_and_base_libraries() => Assert.All(typeof(ManageConversations).Assembly.GetReferencedAssemblies(), reference =>
        Assert.True(reference.Name!.StartsWith("System.", StringComparison.Ordinal) || reference.Name == "InkLink.Messaging.Domain", reference.Name));
    [Fact]
    public void Infrastructure_does_not_depend_on_api() => Assert.DoesNotContain(typeof(MessagingDbContext).Assembly.GetReferencedAssemblies(), reference => reference.Name == "InkLink.Messaging.Api");
    [Fact]
    public void Services_do_not_share_business_implementation()
    {
        foreach (var assembly in new[] { typeof(Conversation).Assembly, typeof(ManageConversations).Assembly, typeof(MessagingDbContext).Assembly, typeof(MessagingProgram).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("InkLink.Engagements.", StringComparison.Ordinal) || reference.Name!.StartsWith("InkLink.Identity.", StringComparison.Ordinal));
    }
}
