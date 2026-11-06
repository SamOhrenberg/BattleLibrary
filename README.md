# The Battle Library

An open-source companion app for tabletop wargames: build army lists tied to
your account, add friends, track games (including live, round-by-round
multiplayer sessions), and keep a public or private profile. Supports
**Warhammer 40,000 (11th Edition)** and **Kill Team (2024)**.

- **App:** https://samohrenberg.github.io/BattleLibrary/
- **API:** https://battlelibrary-production.up.railway.app (health check at `/api/health`)

Registration on the hosted app is currently invite-only.

> **Unofficial.** The Battle Library is a non-commercial fan project. It is not
> affiliated with, endorsed by, or sponsored by Games Workshop. Warhammer
> 40,000, Kill Team, and all associated names and marks are trademarks of Games
> Workshop Limited, used here without permission and with no challenge to
> their status intended.

## Features

- **List building:** detachments, enhancements, wargear options, battle sizes,
  legality checks, Leader attachment, allied forces, loadout presets, and
  BattleScribe `.rosz` import/export.
- **Kill Team rosters:** operatives, weapons, abilities, and faction equipment.
- **Friends and profiles:** username search, friend requests, public or private
  profiles.
- **Game tracking:** log a finished game, or run a live session for up to four
  players with real-time scoring, CP, and kills, then review your history and
  win rate.
- **Rule search:** search unit abilities, weapons, and detachment rules while
  building or playing.
- **Mobile first:** designed for a phone at the table, with five color themes.
- **Installable:** a Progressive Web App. Army data is cached on the device. Saving lists and games still
  needs a connection.

## Stack

- **API:** ASP.NET Core (.NET 10) minimal APIs, EF Core, PostgreSQL, SignalR
- **UI:** React 19 + TypeScript (Vite)
- **Hosting:** GitHub Pages (UI), Railway + Docker (API and database)

## Repository layout

```
BackEnd/
  BattleLibrary.slnx
  src/BattleLibrary.Domain/          entities, no framework dependencies
  src/BattleLibrary.Infrastructure/  EF Core, migrations, BSData parser and in-memory catalogue
  src/BattleLibrary.Api/             endpoints, auth, SignalR hub
  tests/BattleLibrary.Tests/         xUnit v3 tests
FrontEnd/                            Vite + React + TypeScript app
.github/                             Pages workflow, issue and PR templates
docker-compose.yml                   local PostgreSQL
```

## Running locally

### Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) (npm 10+)
- [Docker](https://www.docker.com/) (for the local database)
- The EF Core CLI: `dotnet tool install --global dotnet-ef`

Run each step from the repository root.

**1. Start PostgreSQL** (port 5432, user/password/database all `battlelibrary`):

```bash
docker compose up -d
```

**2. Create the database schema.** Migrations apply automatically in
production, but not in Development.

```bash
cd BackEnd
dotnet ef database update --project src/BattleLibrary.Infrastructure --startup-project src/BattleLibrary.Api
```

**3. Run the API** at http://localhost:5080. The interactive API reference is at
http://localhost:5080/scalar/v1.

```bash
cd BackEnd
dotnet run --project src/BattleLibrary.Api
```

**4. Load the army data.** The catalogue starts empty. With the API running,
trigger a refresh.

```bash
curl -X POST -H "X-Admin-Key: dev-admin-key" http://localhost:5080/api/admin/catalogue/refresh
```

**5. Run the UI** at http://localhost:5173:

```bash
cd FrontEnd
npm install
npm run dev
```

**6. Create an account.** Registration is invite-only by default. The first
account after initial creation is allowed, though, and is made the admin. There is an admin panel on the Profile page can open registration or create
invites for test accounts.

### Running the tests

For the backend, which uses xUnit.

```bash
cd BackEnd
dotnet run --project tests/BattleLibrary.Tests
```

For the UI, `npm run lint` runs oxlint and `npm run build` type-checks and
builds.

## Configuration

Development defaults live in `BackEnd/src/BattleLibrary.Api/appsettings.Development.json`.

Create an `appsettings.json` (gitignored) to override the defaults without modifying the development.json file. 

| Setting | Purpose |
|---|---|
| `ConnectionStrings:Default` | PostgreSQL connection (Npgsql key/value form) |
| `DATABASE_URL` | Alternative `postgresql://` URL, converted automatically (Railway sets this) |
| `Cors:AllowedOrigins` | Array of browser origins allowed to call the API |
| `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience` | Token signing and validation. Use a long random key in production. |
| `Admin:ApiKey` | Value required in the `X-Admin-Key` header for catalogue admin endpoints |
| `Admin:SeedEmails` | Emails that may always register and become site admins |
| `BsData:AutoRefreshEnabled` | Poll BSData for new commits (off in Development) |
| `BsData:PollIntervalMinutes` | Poll interval, default 1 |
| `BsData:GitHubToken` | Optional GitHub token; raises the GitHub API limit from 60/hr to 5000/hr |
| `Axiom:Token`, `Axiom:Dataset` | Optional log shipping to Axiom; console logging only when unset |

The UI reads build-time variables (see `FrontEnd/.env.example`):

- `VITE_API_BASE_URL`: base URL of the API, no trailing slash
- `VITE_BASE`: Pages subpath for builds, `/BattleLibrary/` for the hosted site

### Catalogue admin endpoints

All require the `X-Admin-Key` header.

- `POST /api/admin/catalogue/refresh`: refresh every source. Add
  `?system=wh40k-11e` or `?system=kill-team-2024` for one, and `&force=true` to
  rebuild even when the source commit is unchanged.
- `POST /api/admin/catalogue/{key}/enabled?value=false`: disable a game system
  and drop it from memory. It stays off across restarts until re-enabled.
- `POST /api/admin/catalogue/{key}/purge`: drop a game system from memory until
  the next refresh.

## Data source and data rights

Army data comes from the community BattleScribe data repositories in the
[`BSData`](https://github.com/BSData) organization:
[`wh40k-11e`](https://github.com/BSData/wh40k-11e) (JSON) and
[`wh40k-killteam`](https://github.com/BSData/wh40k-killteam) (XML, 2024
edition). Thanks to everyone who maintains them.

That data has no license and describes Games Workshop's intellectual property,
so this project treats it conservatively:

- It is fetched at runtime and held only in the API server's memory: never
  written to the database, committed to this repository, or redistributed. It
  is reloaded from the source repositories each time the server starts.
- Only the functional minimum is kept: names, points, stat lines, wargear
  options, and ability names. No lore or flavor text.
- Saved lists store references to BSData ids, not copies of the data.
- Each game system can be disabled and purged immediately on request.
- The project is, and will stay, non-commercial.

If you represent a rights holder and want content removed, please open an
issue or contact the maintainer, and it will be disabled promptly.

Errors in unit data (wrong points, missing options) usually come from the
BSData files themselves and are best reported to the relevant BSData
repository. If the data is right upstream but wrong here, open a
[data issue](https://github.com/SamOhrenberg/BattleLibrary/issues/new/choose).

## Contributing

Bug reports, ideas, and pull requests are welcome. Read
[CONTRIBUTING.md](CONTRIBUTING.md) first; it covers the workflow, code
conventions, and the data rules every change has to follow.

## AI assistance disclaimer

I do use [Claude Code](https://claude.com/claude-code) for assistance with developing this project. This includes the following uses:

* Assistance parsing / searching large files (BSData, in particular)
* Assistance with front-end development
  * I am normally more of a backend developer
* Generating boilerplate / common code patterns that I can update
* Rough drafts for documentation / prose

I do this for two reasons:
1. To practice with tools that are becoming industry standard
   * At the end of the day, while this is a hobby project for something I want to use, it is also meant to be a part of my public portfolio for potential job seeking
2. To speed up development
   * I work a full time job, contract-based gigs, am married, have a toddler, am studying for certificates, maintain creative hobbies with friends. This is a tool I've wanted to build for a long time, but I was barely making any headway on it until I opened myself up to using Claude Code for help. You can look at the commit history and see this has been something that has been slowly growing over the last few months, and it is something I have killed and restarted multiple times over the past year. 

My goal is to get all the benefits of hand-written code in a more timely manner without it feeling like, as the internet would say, AI-vibe-coded-slop. However, many people do not want to use any tool that AI touches, so I wanted to provide an upfront disclaimer on its usage. 

**ALL CHANGES ARE REVIEWED AND TESTED BY MYSELF BEFORE THEY ARE EVER COMMITTED**

## License

The source code is [MIT](LICENSE) licensed. The license covers this project's
code only, not BSData files or any Games Workshop content.
