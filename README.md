# Gym Tracker

A small personal workout log: start a session (Push, Pull, Legs, Core…), add exercises, log sets
with weight and reps, and look back at what you did last time. Runs locally — no accounts, no cloud.

## What's in it

- **Workout** – start a session, add exercises, log sets; each exercise shows what you did last time.
- **History** – every finished workout and its sets.
- **Progress** – per exercise: estimated one-rep max, a trend chart, then-and-now comparisons
  (a month, three months and a year ago), personal records and recent sessions.
- **Exercises** – manage the exercise list and the sessions they belong to.

## Run it

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/GymTracker
```

Then open <http://localhost:5080>.

**On your phone:** connect it to the same Wi-Fi as the computer and open `http://<computer-ip>:5080`
(find the IP with `ipconfig` on Windows). The first time, Windows may ask whether to allow the app
through the firewall — allow it for private networks. There is no login, so only run it on a
network you trust.

The database is a single file, `gymtracker.db`, created in `src/GymTracker/` on first run and seeded
with four sessions and a starter exercise list. Back it up by copying the file; delete it to start over.

## Settings (`src/GymTracker/appsettings.json`)

| Key | Default | |
| --- | --- | --- |
| `Urls` | `http://0.0.0.0:5080` | Address and port to listen on (`dotnet run` takes this from `Properties/launchSettings.json`) |
| `ConnectionStrings:GymTracker` | `Data Source=gymtracker.db` | Where the SQLite file lives |
| `WeightUnit` | `kg` | Label shown next to weights |

## Tests

```
dotnet test
```

The tests in `tests/GymTracker.Tests` start the real app in memory against a throwaway SQLite file
and drive it through its HTTP API, so they cover startup, seeding, validation and every endpoint.
They do not exercise the JavaScript front end.

## Structure

```
GymTracker.slnx             Solution: the app plus its tests
src/GymTracker/
  Program.cs                App startup: database, static files, API routes
  Data/                     EF Core entities, DbContext, first-run seed data
  Api/                      Minimal API endpoints (sessions, exercises, workouts, progress), request/response types, validation
                            ProgressCalculator.cs holds the 1RM, records and comparison maths
  Migrations/               EF Core migrations (applied automatically on startup)
  wwwroot/                  The front end — plain HTML/CSS/JavaScript, no build step
    index.html, app.css
    js/app.js               Hash router
    js/api.js               fetch wrapper for the API
    js/util.js              Formatting, toasts, icons
    js/chart.js             Dependency-free SVG line chart
    js/views/               One file per screen: home, workout, history, progress, exercises
tests/GymTracker.Tests/
  GymApp.cs, ApiTest.cs     Test host and shared helpers
  *Tests.cs                 One class per area: startup, sessions, exercises, workouts, sets, "last time", history, progress
```

## Changing the database schema

Edit the entities in `Data/`, then:

```
dotnet tool restore
dotnet ef migrations add <Name> --project src/GymTracker
```

The migration is applied to your existing database the next time the app starts.
