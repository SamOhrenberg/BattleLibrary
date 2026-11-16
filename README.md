> [!NOTE]
> The Battle Library used to load army data from the community BSData
> repositories. That source has been shut down, so the app no longer ships or
> fetches any army data. Lists are now freeform: write in your own units, the
> way you would on paper.

# The Battle Library

An open-source companion app for tabletop wargames: write army lists tied to
your account, add friends, track games (including live, round-by-round
multiplayer sessions), and keep a public or private profile. Lists can be
tagged **Warhammer 40,000**, **Kill Team**, or **Other**.

- **App:** https://samohrenberg.github.io/BattleLibrary/
- **API:** https://battlelibrary-production.up.railway.app (health check at `/api/health`)

Registration on the hosted app is currently invite-only.

> **Unofficial.** The Battle Library is a non-commercial fan project. It is not
> affiliated with, endorsed by, or sponsored by Games Workshop. Warhammer
> 40,000, Kill Team, and all associated names and marks are trademarks of Games
> Workshop Limited, used here without permission and with no challenge to
> their status intended.

## Features

- **Freeform lists:** like a paper list. Each unit has a name plus optional
  models, points, wargear, enhancement, and notes. Nothing is required and
  nothing is checked against game rules; points simply add up to a running
  total.
- **List extras:** named sections (for allies or anything else), a warlord
  marker, attaching one unit to another, reusable unit presets, a printable
  play view, and plain-text export.
- **Friends and profiles:** username search, friend requests, public or private
  profiles.
- **Game tracking:** log a finished game, or run a live session for up to four
  players with real-time scoring, CP, and kills, then review your history and
  win rate.
- **AI assistant access (MCP):** connect Claude or another MCP client to read
  your lists, games, and stats. Read-only, approved by you over
  OAuth, and revocable from your profile.
- **Mobile first:** designed for a phone at the table, with five color themes.
- **Installable:** a Progressive Web App. Saving lists and games needs a
  connection.

## Stack

- **API:** ASP.NET Core (.NET 10) minimal APIs, EF Core, PostgreSQL, SignalR
- **UI:** React 19 + TypeScript (Vite)
- **Hosting:** GitHub Pages (UI), Railway + Docker (API and database)

## Repository layout

```
BackEnd/
  BattleLibrary.slnx
  src/BattleLibrary.Domain/          entities, no framework dependencies
  src/BattleLibrary.Infrastructure/  EF Core, migrations
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

**4. Run the UI** at http://localhost:5173:

```bash
cd FrontEnd
npm install
npm run dev
```

**5. Create an account.** Registration is invite-only by default. The first
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
| `Admin:SeedEmails` | Emails that may always register and become site admins |
| `Axiom:Token`, `Axiom:Dataset` | Optional log shipping to Axiom; console logging only when unset |
| `OAuth:FrontendUrl` | UI base URL where MCP clients are sent to approve access (with trailing slash) |
| `OAuth:PublicUrl` | Optional public API base URL for OAuth metadata; defaults to the request's scheme and host |
| `OAuth:AccessTokenMinutes`, `OAuth:RefreshTokenDays` | MCP token lifetimes, default 60 minutes and 30 days |

The UI reads build-time variables (see `FrontEnd/.env.example`):

- `VITE_API_BASE_URL`: base URL of the API, no trailing slash
- `VITE_BASE`: Pages subpath for builds, `/BattleLibrary/` for the hosted site

### MCP server

The API serves a read-only [Model Context Protocol](https://modelcontextprotocol.io/)
endpoint at `/mcp` (Streamable HTTP). Add it to an MCP client as a remote
server, for example in Claude as a custom connector with the URL
`https://battlelibrary-production.up.railway.app/mcp`, or locally with
`claude mcp add --transport http battle-library http://localhost:5173/mcp`.

The client discovers sign-in through OAuth 2.1 metadata
(`/.well-known/oauth-protected-resource/mcp` and
`/.well-known/oauth-authorization-server`), registers itself, and sends you to
the UI to approve read access. Tokens only work on `/mcp`, and disconnecting an
app under Profile > Connected apps cuts it off immediately.

Tools: `get_my_account`, `list_army_lists`, `get_army_list`, `list_games`,
`get_game`, `get_game_stats`, `list_live_games`, `get_live_game`,
`list_friends`, `get_player_profile`, `search_players`. They follow the same
ownership and profile-visibility rules as the app.

## Game content

The app contains no Games Workshop rules or army data, and does not fetch any.
Everything in a list is typed in by its owner, and nothing is validated
against game rules.

Earlier versions read army data at runtime from the community
[`BSData`](https://github.com/BSData) repositories. That source has been shut
down, and all code that read it has been removed. Thanks to everyone who
maintained it.

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
