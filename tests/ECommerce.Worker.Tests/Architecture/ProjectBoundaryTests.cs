using System.Xml.Linq;
using ECommerce.Worker.Contracts;
using FluentAssertions;

namespace ECommerce.Worker.Tests.Architecture;

public sealed class ProjectBoundaryTests
{
    [Fact]
    public void WorkerProject_DoesNotReferenceApiProjects()
    {
        var root = FindSolutionRoot();
        var project = XDocument.Load(Path.Combine(
            root,
            "src",
            "ECommerce.Worker",
            "ECommerce.Worker.csproj"));

        project.Descendants("ProjectReference").Should().BeEmpty();

        var forbiddenNamespaces = new[]
        {
            "ECommerce.Application",
            "ECommerce.Domain",
            "ECommerce.Infrastructure",
            "ECommerce.Persistence",
            "ECommerce.Shared"
        };
        var source = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Select(File.ReadAllText)
            .ToArray();

        foreach (var forbiddenNamespace in forbiddenNamespaces)
            source.Should().NotContain(text => text.Contains(forbiddenNamespace, StringComparison.Ordinal));
    }

    [Fact]
    public void Version1Contract_IsDocumentedAndProtectedByAHeader()
    {
        var root = FindSolutionRoot();
        var contract = File.ReadAllText(Path.Combine(root, "docs", "integration-events-v1.md"));

        contract.Should().Contain(IntegrationEventContract.VersionHeaderName);
        contract.Should().Contain("`1`");
        contract.Should().Contain("`order.created`");
        contract.Should().Contain("`stock.updated`");
        contract.Should().Contain("`email.sent`");
    }

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "ECommerceWorker.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Worker solution root.");
    }
}
