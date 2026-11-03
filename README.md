# The Battle Library

An open-source companion app for tabletop wargames: load army data, build lists
tied to your account, add friends, track games, and keep a public or private
profile. Priority support for Warhammer 40,000 and Kill Team.

## Stack

- **API:** ASP.NET Core (.NET 10) Web API, EF Core, PostgreSQL
- **UI:** React + TypeScript (Vite)
- **Hosting:** GitHub Pages (UI), Railway + Docker (API and database)

## Repository layout

```
backend/    ASP.NET Core solution (Domain, Infrastructure, Api)
frontend/   Vite + React + TypeScript app
```

## Prerequisites

- .NET SDK 10
- Node.js 20+
- Docker (for local PostgreSQL)

## Local development

Run each step from the repository root.

**1. Start PostgreSQL:**

```bash
docker compose up -d
```

**2. Run the API** (http://localhost:5080, API reference at `/scalar/v1`):

```bash
cd backend
dotnet run --project src/BattleLibrary.Api
```

**3. Run the UI** (http://localhost:5173):

```bash
cd frontend
npm install
npm run dev
```

The landing page shows a green **API status** when the UI reaches the API.

## Configuration

**API** reads its PostgreSQL connection from, in order:

1. `ConnectionStrings:Default` (used by `appsettings.Development.json`)
2. `DATABASE_URL` (a `postgresql://…` URL, converted to Npgsql form)

Allowed browser origins are set via `Cors:AllowedOrigins` (an array). In
deployment, pass them as environment variables, e.g.
`Cors__AllowedOrigins__0=https://<user>.github.io`.

**UI** reads build-time variables (see `frontend/.env.example`):

- `VITE_API_BASE_URL` - base URL of the API
- `VITE_BASE` - Pages subpath for project sites, e.g. `/battle-library/`

## Deployment

- **API + database (Railway):** set the service root directory to `backend` so
  the `Dockerfile` is used. Railway injects `PORT` (honored automatically) and
  `DATABASE_URL`. Add a `Cors__AllowedOrigins__0` variable for the UI origin.
- **UI (GitHub Pages):** the `Deploy frontend to Pages` workflow builds and
  publishes `frontend/` on pushes to `main`. Set repository **Variables**
  `VITE_API_BASE_URL` and `VITE_BASE`, and enable Pages with the GitHub Actions
  source.

## Data source

Army data comes from the BattleScribe data repositories (the
[`BSData`](https://github.com/BSData) organization).

## License

[MIT](LICENSE).
