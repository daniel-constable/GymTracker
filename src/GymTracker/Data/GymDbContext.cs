using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GymTracker.Data;

public class GymDbContext(DbContextOptions<GymDbContext> options) : DbContext(options)
{
    public const int NameMaxLength = 60;

    public DbSet<SessionType> SessionTypes => Set<SessionType>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<Workout> Workouts => Set<Workout>();
    public DbSet<WorkoutExercise> WorkoutExercises => Set<WorkoutExercise>();
    public DbSet<SetEntry> Sets => Set<SetEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no timezone info; everything is stored as UTC and read back as UTC
        // so the API serialises timestamps with a trailing "Z".
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionType>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(NameMaxLength).UseCollation("NOCASE");
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Exercise>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(NameMaxLength).UseCollation("NOCASE");
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.SessionType).WithMany(x => x.Exercises)
                .HasForeignKey(x => x.SessionTypeId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Workout>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(NameMaxLength);
            e.HasIndex(x => x.StartedAt);
            e.HasOne(x => x.SessionType).WithMany()
                .HasForeignKey(x => x.SessionTypeId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WorkoutExercise>(e =>
        {
            e.HasOne(x => x.Workout).WithMany(x => x.Exercises)
                .HasForeignKey(x => x.WorkoutId).OnDelete(DeleteBehavior.Cascade);
            // Exercises with history are archived, never deleted.
            e.HasOne(x => x.Exercise).WithMany()
                .HasForeignKey(x => x.ExerciseId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SetEntry>(e =>
        {
            e.HasOne(x => x.WorkoutExercise).WithMany(x => x.Sets)
                .HasForeignKey(x => x.WorkoutExerciseId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
