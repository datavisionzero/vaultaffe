using System.Xml.Linq;

namespace Vaultaffe.UnitTests;

/// <summary>
/// ADR 0002 has the dependencies point inward and only inward, and
/// docs/codebase.md calls Domain's emptiness the cheapest check that nothing has
/// leaked into it. A convention nobody can run is not a check, so this reads the
/// project files and turns the layering into a failing build.
/// </summary>
public sealed class LayeringTests
{
    [Fact]
    public void Domain_depends_on_nothing_at_all()
    {
        var domain = Layer.Read("Vaultaffe.Domain");

        Assert.Empty(domain.ProjectReferences);
        Assert.Empty(domain.PackageReferences);
    }

    [Fact]
    public void Application_depends_on_domain_only()
    {
        Assert.Equal(["Vaultaffe.Domain"], Layer.Read("Vaultaffe.Application").ProjectReferences);
    }

    [Fact]
    public void Infrastructure_depends_on_application_only()
    {
        Assert.Equal(
            ["Vaultaffe.Application"],
            Layer.Read("Vaultaffe.Infrastructure").ProjectReferences);
    }

    [Fact]
    public void Api_is_the_composition_root_and_depends_on_the_two_outer_layers()
    {
        Assert.Equal(
            ["Vaultaffe.Application", "Vaultaffe.Infrastructure"],
            Layer.Read("Vaultaffe.Api").ProjectReferences);
    }

    private sealed record Layer(
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> PackageReferences)
    {
        public static Layer Read(string project)
        {
            var document = XDocument.Load(
                Path.Combine(RepositoryRoot(), "src", project, $"{project}.csproj"));

            return new Layer(
                References(document, "ProjectReference"),
                References(document, "PackageReference"));
        }

        private static IReadOnlyList<string> References(XDocument project, string element) =>
            [.. project.Descendants(element)
                .Select(reference => Path.GetFileNameWithoutExtension(
                    reference.Attribute("Include")?.Value ?? string.Empty))
                .Order(StringComparer.Ordinal)];

        // The test binary sits under tests/<project>/bin/…, and the solution
        // file is what says where the repository begins.
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null
                   && !File.Exists(Path.Combine(directory.FullName, "Vaultaffe.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("No Vaultaffe.slnx above the test binary.");
        }
    }
}
