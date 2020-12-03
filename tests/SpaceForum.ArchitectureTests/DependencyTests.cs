using System.Reflection;

namespace SpaceForum.ArchitectureTests;

public sealed class DependencyTests
{
    [Fact]
    public void DomainDoesNotReferenceOuterLayers()
    {
        var references = GetReferences(typeof(Domain.AssemblyMarker).Assembly);

        Assert.DoesNotContain("SpaceForum.Application", references);
        Assert.DoesNotContain("SpaceForum.Infrastructure", references);
        Assert.DoesNotContain("SpaceForum.Web", references);
    }

    [Fact]
    public void ApplicationDoesNotReferenceInfrastructureOrWeb()
    {
        var references = GetReferences(typeof(Application.AssemblyMarker).Assembly);

        Assert.DoesNotContain("SpaceForum.Infrastructure", references);
        Assert.DoesNotContain("SpaceForum.Web", references);
    }

    [Fact]
    public void InfrastructureDoesNotReferenceWeb()
    {
        var references = GetReferences(typeof(Infrastructure.AssemblyMarker).Assembly);

        Assert.DoesNotContain("SpaceForum.Web", references);
    }

    private static HashSet<string> GetReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
}
