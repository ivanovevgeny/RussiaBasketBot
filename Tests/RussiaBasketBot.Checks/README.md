Requires the .NET 10 SDK. Run deterministic hosting, logging, API mapping and message checks:

```powershell
dotnet run --project Tests/RussiaBasketBot.Checks
```

To also exercise MongoDB persistence and Hangfire job storage, start MongoDB on localhost:27017 and add
`-- --mongo`. The checks create a unique `rbbot_api_checks_<guid>` database and
drop only that database in a finally block. They never use the application configuration.

Add `--live` to verify the real public APIs without writing their data to MongoDB:

```powershell
dotnet run --project Tests/RussiaBasketBot.Checks -- --mongo --live
```

Fixtures were captured from the new RFB APIs on 2026-11-13. Rosters contain only
team information. Calendar fixtures cover two finished games and two planned
games, including one with an unassigned start time. Live snapshot assertions
are date-specific and need updating as the competition progresses.
