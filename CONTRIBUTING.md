# Contributing to The Battle Library

Thanks for taking the time to contribute.

The Battle Library is a relatively small project, and I review contributions directly. Reviews may take a few days depending on the change. Code is welcome, but it is not the only useful contribution. Clear bug reports, data issues, and well-defined ideas are equally valuable.

## Before you start

- **Bugs and ideas:** Open an issue using one of the
  [templates](https://github.com/SamOhrenberg/BattleLibrary/issues/new/choose). Give enough detail to reproduce the problem or understand the proposed change.
- **Pull requests:** If the change is more than a small, self-contained fix, open an issue first. I would rather agree on the approach before you spend time implementing something that may not fit the project.
- **Wrong unit data:** Check the upstream BSData source before opening an issue here. (see
  [Data problems](#data-problems)).

## Local setup

Follow [Running locally](README.md#running-locally) in the README. 

The basic sequence is:
1. Start PostgreSQL with docker compose up -d.
2. Apply the database migrations with dotnet ef database update.
3. Start the API.
4. Trigger a catalogue refresh.
5. Start the UI.

The first account registered against a new local database becomes its administrator.

If you are working on BSData ingestion, expect to refresh catalogues frequently. Unauthenticated GitHub API access is limited to 60 requests per hour, which is easy to hit during development. Set a GitHub personal access token with no scopes as BsData__GitHubToken, or configure it in your local appsettings.json.

## Data rules (required for every change)

These rules apply to every contribution.

Army data belongs to Games Workshop and is provided, unlicensed, by the BSData community. The application intentionally consumes only the information needed to provide its functionality. Contributions need to preserve that boundary.

A pull request that violates these rules will not be merged.

1. **Never commit BSData-derived data.** Do not commit catalogue files, parsed output, fixtures, snapshots, or screenshots containing rules content. Tests that require real BSData must read it from BSDATA_TEST_DIR at runtime.
2. **Ingest the functional minimum.** Names, points, stat lines, wargear options, keywords, and ability names are appropriate because the application needs them to function. Lore, flavor text, and new verbatim rules prose are not.
3. **Store references, not copies.** Persisted user data such as lists, games, and presets should reference stable BSData identifiers and store computed application values where necessary. Do not create additional copies of upstream content.
4. **Keep BSData in memory and the kill switch working.** Parsed catalogue data lives only in the in-memory `CatalogueStore`; do not persist it to the database or disk. Anything added to the catalogue must also be removed by the corresponding per-system purge and kill switch.
5. **Stay non-commercial.** Do not add advertising, paid functionality, or donation gates tied to Games Workshop content.

## Project structure

The repository is divided into a small number of deliberately separated components:

- `BackEnd/src/BattleLibrary.Domain`: Domain entities with no framework dependencies.
- `BackEnd/src/BattleLibrary.Infrastructure`: EF Core DbContext, migrations, and the BSData ingestion pipeline under BsData/, including the GitHub client, JSON and XML readers, parser, in-memory catalogue store and refresh, and background loader/poller.
- `BackEnd/src/BattleLibrary.Api`: Minimal API endpoints organized by feature (Auth, Catalogue, Lists, Social, Games, Admin), application hosting, and the SignalR hub at /hubs/game.
- `FrontEnd/src`: React application organized by feature, with API clients under lib/.

Data sources are listed in `BsDataSource.All`. Each one names a repo, a format
(`Json` for 40k, `Xml` for Kill Team), and a schema of profile and cost type
names. Both formats feed one shared extraction step, so a new game system is
usually a new source entry plus schema, not a new parser.

## Code conventions

**General**

- Match the style of the surrounding code. `.editorconfig` sets indentation
  (4 spaces for C#, 2 for TypeScript, JSON, CSS, and YAML) and LF line endings.
- No em-dashes anywhere: code, comments, UI text, docs, or commit messages. Use
  a comma, colon, parentheses, or a second sentence.

**Comments**

- Keep them short, usually one line. Multi-line comments should be rare.
- Describe what the code does now, not future plans.
- Avoid first-person plural ("we", "our", "let's").

**Backend**

- Target `net10.0`. Nullable reference types and implicit usings are on.
- Entity keys are app-assigned `Guid.CreateVersion7()` values set by the base
  `Entity` constructor; don't add database-generated keys.
- Some package versions in the `.csproj` files are pinned on purpose.
  Framework-tied ASP.NET packages must stay at or below
  10.0.7, and EF Core stays at 10.0.4 to match the Npgsql provider. Don't bump
  these in an unrelated PR.
- Schema changes need a migration:
  ```bash
  cd BackEnd
  dotnet ef migrations add <Name> --project src/BattleLibrary.Infrastructure --startup-project src/BattleLibrary.Api
  ```

**Frontend**

- Mobile first. Style for a phone, then enhance with `min-width` media
  queries. Keep touch targets large, use bottom sheets for modals, and never
  let content scroll sideways.
- Use the theme tokens instead of hard-coded colors, so all five themes keep
  working.
- Check new screens at phone width (around 375px) before opening a PR.

## Tests

```bash
cd BackEnd
dotnet run --project tests/BattleLibrary.Tests
```

The tests use xUnit v3 on Microsoft.Testing.Platform. Run the project directly;
`dotnet test` through VSTest is not set up.

Parser tests that need real data skip unless `BSDATA_TEST_DIR` is set. To run
them, download these files from
[`BSData/wh40k-11e`](https://github.com/BSData/wh40k-11e) into a folder outside
the repository and point the variable at it:

- `Warhammer 40,000.json`, saved as `gst.json`
- `Orks.json`, saved as `orks.json`

For the UI, run `npm run lint` and `npm run build` in `FrontEnd/`. The build
includes the TypeScript type check.

## Pull requests

1. Fork the repository and branch from `main`.
2. Keep each PR focused on one change. Separate refactors from features.
3. Make sure the backend builds, the tests pass, and the UI lints and builds.
4. Fill in the PR template, including screenshots (phone width) for UI changes.

I review, test, and merge contributions. A merge to main automatically publishes the UI to Pages, so main must remain deployable. That is why unrelated cleanup, speculative refactoring, or dependency upgrades should not be mixed into otherwise focused changes.

### AI-assisted contributions

AI-assisted development is welcome. I use these tools as part of the project as well, as described in the README.

If an AI tool generated or substantially contributed to a meaningful part of the pull request, disclose that in the PR description. This is not a restriction on using the tool. It gives me useful context when reviewing the change.

Regardless of how the code was produced, the contributor is responsible for it. You should understand the code you submit, test it, and be able to explain why it behaves correctly. Generated code is held to the same standard as code written manually.

## Data problems

When unit or catalogue data appears to be wrong, first determine whether the problem exists in the upstream BSData source.

- **Wrong in BSData too:** report it to that repository
  ([`wh40k-11e`](https://github.com/BSData/wh40k-11e/issues) or
  [`wh40k-killteam`](https://github.com/BSData/wh40k-killteam/issues)). The app
  picks up the fix on its next refresh.
- **Right in BSData, wrong here:** that is a parser bug. Open a data issue here
  with the faction, unit, and what you expected.

This distinction matters because The Battle Library should not compensate for incorrect upstream catalogue data by maintaining its own corrected copy. Upstream data problems should be fixed upstream. Parsing and interpretation problems belong here.

## Security

Do not report security vulnerabilities through a public issue.

Use GitHub's private [vulnerability reporting](https://github.com/SamOhrenberg/BattleLibrary/security/advisories/new) instead.

## License

By contributing, you agree that your contributions are licensed under the
project's [MIT License](LICENSE).
