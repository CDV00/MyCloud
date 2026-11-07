using System.Xml.Linq;

namespace Architecture.Tests;

/// <summary>
/// Database-per-service & độc lập triển khai: project của service này không được tham chiếu project của service khác.
/// Chia sẻ chỉ qua BuildingBlocks và Contracts (docs/02 §5).
/// </summary>
public sealed class ServiceIsolationTests
{
    [Fact]
    public void Services_DoNotReferenceOtherServices()
    {
        DirectoryInfo servicesDir = FindServicesDirectory();
        var violations = new List<string>();

        foreach (DirectoryInfo serviceDir in servicesDir.GetDirectories())
        {
            foreach (FileInfo csproj in serviceDir.GetFiles("*.csproj", SearchOption.AllDirectories))
            {
                IEnumerable<string> references = XDocument.Load(csproj.FullName)
                    .Descendants("ProjectReference")
                    .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
                    .Select(include => Path.GetFullPath(Path.Combine(csproj.DirectoryName!, include)));

                foreach (string reference in references)
                {
                    bool insideServices = reference.StartsWith(servicesDir.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    bool sameService = reference.StartsWith(serviceDir.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    if (insideServices && !sameService)
                    {
                        violations.Add($"{csproj.Name} -> {Path.GetFileName(reference)}");
                    }
                }
            }
        }

        violations.ShouldBeEmpty();
    }

    private static DirectoryInfo FindServicesDirectory()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = new DirectoryInfo(Path.Combine(dir.FullName, "src", "Services"));
            if (candidate.Exists)
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/Services from the test output directory.");
    }
}
