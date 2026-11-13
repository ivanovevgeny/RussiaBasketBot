# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A .NET 10 ASP.NET Core application that runs as a Windows Service. It loads Russian men's Superliga teams, games and standings from the APIs used by `russiabasket.ru/competitions/msl` and sends scheduled Telegram notifications to subscribed groups.

## Commands

```bash
# Build
dotnet build

# Run locally
dotnet run

# Publish for deployment
dotnet publish -c Release

# API mapping checks (no external services required)
dotnet run --project Tests/RussiaBasketBot.Checks

# Also check persistence in a temporary local MongoDB database and the live APIs
dotnet run --project Tests/RussiaBasketBot.Checks -- --mongo --live
```

The app runs on `http://localhost:5233` (configured in `appsettings.json`).
Building requires the .NET 10 SDK; framework-dependent deployments require ASP.NET Core Runtime 10.

## Architecture

### Entry Point (`Program.cs`)
Minimal API host that wires up all services and defines two main things:
- **Hangfire recurring jobs**: `MorningUpdate` (9:00 MSK) and `EveningUpdate` (22:00 MSK), both calling `NotifyService.ParseAndNotify`
- **REST endpoints**: `GET /` (health), `GET /init` (seed DB), `GET /teams`, `GET /newest`, `GET /latest`

### Service Layer

| Service | Role |
|---|---|
| `BasketballApiClient` | Loads the current season from `a.russiabasket.org/api/view/page/league/msl`, then calls `basket2.sportoteka.org/api/abc/comps/{rosters,calendar,standings}?tag=msl&season=...`. Calendar requests include `calendarType=-1&maxResultCount=1000`; incomplete responses fail before database writes. |
| `ParserService` | Upserts API teams and matches by external ID, preserving MongoDB IDs and creation dates. All mutable match fields update even when status is unchanged. Full imports remove obsolete records only after successful upserts; empty responses preserve existing data. |
| `BasketballService` | Refreshes API data before user-facing MongoDB queries. `GetMatches(newestOrLatest)` returns upcoming (`Plan`) or recent (`Live`/`Finish`) matches as `MatchVm`; `refresh: false` reuses the preceding background import. Date filters use Moscow dates. |
| `NotifyService` | Orchestrates parse + notify cycle. `ParseAndNotify` is the Hangfire job target. `NotifyTelegramGroups` sends formatted HTML messages to all subscribed chats (or a single chat when responding to a command). Also handles `/subscribe` and `/unsubscribe`. |
| `TelegramBotHandler` | Implements `IUpdateHandler`. Routes bot commands (`/start`, `/newest`, `/latest`, `/subscribe`, `/unsubscribe`) to `NotifyService` or direct responses. |
| `BackgroundWorker` | `IHostedService` that starts the Telegram long-polling loop via `botClient.ReceiveAsync`. |

### Data Layer

`MongoDbContext` wraps three MongoDB collections:
- `team` → `Team` model
- `matches` → `Match` model (status: `Plan=0`, `Live=1`, `Finish=2`)
- `telegram_groups` → `TelegramGroup` model (stores subscribed chat IDs)

All dates are stored as UTC; Moscow time (UTC+3) is applied in `MatchVm.DateMsc` and `DateExtensions`.

### Configuration

- `appsettings.json` — base config (committed, token is empty)
- `appsettings.Personal.json` — local overrides with real `TelegramBotToken` and Hangfire settings (gitignored, not committed)
- `AppSettings` class uses **static properties** populated via `builder.Configuration.GetSection("AppSettings").Get<AppSettings>()` — binding populates static fields directly.

### Hangfire

Stored in MongoDB (same database, `hangfire.mongo` prefix). Dashboard exposed at the URL configured in `AppSettings.Hangfire.DashboardUrl` (default `/hangfire`, personal override `/hangfire-rbbot`). Auth via `HangfireAuthorizationFilter`.

### Logging

Serilog writes to console and daily rolling files at `logs/log-<date>.txt` (retained 10 days). Working directory is set to the assembly location so logs land next to the executable.

## Key Conventions

- `MatchId` (int) is the external ID from the website; MongoDB `Id` (ObjectId) is internal.
- API statuses map as follows: `Scheduled` → `Plan`, `Online` → `Live`, `Result`/`ResultConfirmed` → `Finish`. Unknown statuses fail the import.
- Calendar `team1.teamId`/`team2.teamId` are stable team IDs; their `id` fields refer to competition registrations and must not be used as `TeamId`.
- API `scheduledTime` includes a UTC offset. Convert it to UTC; games without an assigned time retain the existing 12:00 Moscow placeholder for `localDate`.
- The `newestOrLatest` bool parameter is used throughout: `true` = upcoming/planned matches, `false` = recent/finished matches.
- Telegram messages use HTML parse mode. Links, bold text, and emoji are part of the message format.
