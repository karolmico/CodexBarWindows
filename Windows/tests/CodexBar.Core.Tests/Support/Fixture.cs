using System.IO;

namespace CodexBar.Core.Tests.Support;

internal static class Fixture
{
    public static string Read(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture '{name}' is missing from the test output.", path);
        }

        return File.ReadAllText(path);
    }
}
