using GymTracker.Api;
using GymTracker.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<GymDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("GymTracker") ?? "Data Source=gymtracker.db"));

builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// Create or upgrade the local database on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GymDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db);
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Always revalidate so the phone picks up changes straight away.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

app.MapGet("/api/config", (IConfiguration config) => new { weightUnit = config["WeightUnit"] ?? "kg" });
app.MapSessionTypeEndpoints();
app.MapExerciseEndpoints();
app.MapWorkoutEndpoints();
app.MapProgressEndpoints();

app.Run();
