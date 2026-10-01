using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GymTracker.Tests;

/// <summary>
/// Hosts the real application in memory against its own throwaway SQLite file, so every test
/// goes through the same startup (migrations, seeding), routing and database as production.
/// </summary>
public sealed class GymApp : WebApplicationFactory<Program>
{
    private readonly bool _ownsDatabase;
    private readonly Dictionary<string, string> _settings;

    /// <param name="databasePath">Reuse an existing database file (to simulate a restart). The caller deletes it.</param>
    /// <param name="settings">Extra configuration overrides.</param>
    public GymApp(string? databasePath = null, Dictionary<string, string>? settings = null)
    {
        _ownsDatabase = databasePath is null;
        DatabasePath = databasePath ?? NewDatabasePath();
        _settings = settings ?? [];
    }

    public string DatabasePath { get; }

    public static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"gymtracker-tests-{Guid.NewGuid():N}.db");

    public static void DeleteDatabase(string path) => File.Delete(path);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Pooling is off so the file is released as soon as the app is disposed, without
        // clearing pools that tests running in parallel are still using.
        builder.UseSetting("ConnectionStrings:GymTracker", $"Data Source={DatabasePath};Pooling=False");
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsDatabase)
            DeleteDatabase(DatabasePath);
    }
}
