using BusinessDirectory.Api.Data;

namespace BusinessDirectory.UnitTests;

public class MigrationRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hl-tests-" + Guid.NewGuid().ToString("N"));

    public MigrationRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void FindDirectory_finds_folder_in_a_parent_directory()
    {
        var migrations = Path.Combine(_root, "database", "migrations");
        var deep = Path.Combine(_root, "backend", "Api", "bin");
        Directory.CreateDirectory(migrations);
        Directory.CreateDirectory(deep);

        var found = MigrationRunner.FindDirectory(deep, "database", "migrations");

        Assert.Equal(migrations, found);
    }

    [Fact]
    public void FindDirectory_returns_null_when_nothing_matches()
    {
        var found = MigrationRunner.FindDirectory(_root, "no-such-folder", "anywhere-" + Guid.NewGuid().ToString("N"));

        Assert.Null(found);
    }
}
