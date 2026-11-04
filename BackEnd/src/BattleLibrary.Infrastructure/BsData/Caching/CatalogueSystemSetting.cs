namespace BattleLibrary.Infrastructure.BsData.Caching;

// Per-system kill switch, the only catalogue state kept in Postgres. Holds no
// BSData content. A system with no row is enabled.
public sealed class CatalogueSystemSetting
{
    public string Key { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
