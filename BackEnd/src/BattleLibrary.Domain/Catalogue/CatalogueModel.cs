namespace BattleLibrary.Domain.Catalogue;

// Slim, GW-agnostic read model produced from BSData. Intentionally minimal:
// the functional data for list building, no lore or full rules text.

public sealed record GameSystemInfo(string Id, string Name, int Revision);

public sealed record CatalogueModel(
    GameSystemInfo GameSystem,
    string FactionId,
    string FactionName,
    IReadOnlyList<Datasheet> Datasheets,
    IReadOnlyList<string> Equipment,
    IReadOnlyList<Detachment> Detachments);

// A detachment choice, its Detachment Points cost, and the enhancements it
// offers (each with its pts cost).
public sealed record Detachment(string Id, string Name, int Dp, IReadOnlyList<Enhancement> Enhancements);

public sealed record Enhancement(string Id, string Name, int Points);

public sealed record Datasheet(
    string Id,
    string Name,
    int? Points,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<UnitProfile> Profiles,
    IReadOnlyList<WeaponProfile> Weapons,
    IReadOnlyList<string> Abilities,
    IReadOnlyList<DatasheetOption> Options,
    IReadOnlyList<string> Equipment);

// A selectable wargear/upgrade choice. Points is the additive pts cost (0 when
// the option is free, as most 11e wargear is). Group is the choice set it
// belongs to (e.g. "Melee Weapons"), for presentation.
public sealed record DatasheetOption(string Id, string Name, int Points, string Group);

public sealed record UnitProfile(string Name, IReadOnlyList<Characteristic> Characteristics);

public sealed record WeaponProfile(string Name, bool Ranged, IReadOnlyList<Characteristic> Characteristics);

public sealed record Characteristic(string Name, string Value);
