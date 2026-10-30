namespace BattleLibrary.Infrastructure.Rosters;

// A BattleScribe roster (.ros / .rosz), reduced to the fields that round-trip a
// Battle Library list: the game-system header, one force per catalogue, and a
// tree of selections carrying their source entry ids and points.

public sealed record RosterDocument(
    string Name,
    string GameSystemId,
    string GameSystemName,
    int GameSystemRevision,
    string PointsTypeId,
    IReadOnlyList<RosterForce> Forces);

public sealed record RosterForce(
    string CatalogueId,
    string CatalogueName,
    int CatalogueRevision,
    IReadOnlyList<RosterSelection> Selections,
    // The game-system force slot this force fills (e.g. "Army Roster"); required
    // on the <force> node or importers reject the roster.
    string ForceEntryId = "",
    string ForceName = "");

// entryId is the BSData id the selection came from (the key for matching on
// import); Number is the count; Type is BattleScribe's selection type
// ("unit"/"model"/"upgrade"). Points is the pts cost of this selection alone.
// CustomName is the player's name for the unit, emitted as the customName
// attribute; null when none.
public sealed record RosterSelection(
    string EntryId,
    string Name,
    int Number,
    string Type,
    int Points,
    IReadOnlyList<RosterSelection> Selections,
    string? CustomName = null);
