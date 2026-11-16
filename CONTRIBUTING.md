# Contributing to The Battle Library

Thanks for taking the time to contribute.

The Battle Library is a relatively small project, and I review contributions directly. Reviews may take a few days depending on the change. Code is welcome, but it is not the only useful contribution. Clear bug reports and well-defined ideas are equally valuable.

## Before you start

- **Bugs and ideas:** Open an issue using one of the
  [templates](https://github.com/SamOhrenberg/BattleLibrary/issues/new/choose). Give enough detail to reproduce the problem or understand the proposed change.
- **Pull requests:** If the change is more than a small, self-contained fix, open an issue first. I would rather agree on the approach before you spend time implementing something that may not fit the project.

## Local setup

Follow [Running locally](README.md#running-locally) in the README. 

The basic sequence is:
1. Start PostgreSQL with docker compose up -d.
2. Apply the database migrations with dotnet ef database update.
3. Start the API.
4. Start the UI.

The first account registered against a new local database becomes its administrator.

## Content rules (required for every change)

The app ships no game data. Army lists are freeform: players type in their own
units, points, and wargear, the same as writing a list on paper. Contributions
need to keep it that way.

A pull request that breaks these rules will not be merged.

1. **No Games Workshop data or rules text.** Do not add unit catalogues, points
   tables, datasheets, rules text, or importers for them, whether bundled,
   fetched, or scraped. Screenshots in PRs should not show rules content.
2. **No rules validation.** The app does not check lists against game rules.
   Totals and labels are fine; legality checks are not.
3. **Stay non-commercial.** Do not add advertising, paid functionality, or
   donation gates.

## Project structure

The repository is divided into a small number of deliberately separated components:

- `BackEnd/src/BattleLibrary.Domain`: Domain entities with no framework dependencies.
- `BackEnd/src/BattleLibrary.Infrastructure`: EF Core DbContext, migrations, and the feedback GitHub client.
- `BackEnd/src/BattleLibrary.Api`: Minimal API endpoints organized by feature (Auth, Lists, Social, Games, Feedback, OAuth, Mcp, Admin), application hosting, and the SignalR hub at /hubs/game.
- `FrontEnd/src`: React application organized by feature, with API clients under lib/.

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

## Security

Do not report security vulnerabilities through a public issue.

Use GitHub's private [vulnerability reporting](https://github.com/SamOhrenberg/BattleLibrary/security/advisories/new) instead.

## License

By contributing, you agree that your contributions are licensed under the
project's [MIT License](LICENSE).
