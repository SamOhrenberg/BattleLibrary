using System.Security.Claims;
using BattleLibrary.Domain.Lists;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Lists;

// Loadout presets: saved wargear/size choices for a datasheet, reusable when
// adding that unit to any list. Part of ListEndpoints so it reuses the sheet /
// option / size helpers (LoadSheet, ResolveOptions, PickSize).
public static partial class ListEndpoints
{
    // At most this many presets per datasheet per user, to keep the list tidy.
    private const int MaxPresetsPerSheet = 25;

    public static RouteGroupBuilder MapPresetEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/presets").RequireAuthorization();

        group.MapGet("", GetPresets);
        group.MapPost("", CreatePreset);
        group.MapDelete("/{id:guid}", DeletePreset);

        return group;
    }

    private static async Task<IResult> GetPresets(
        string? system, string? datasheetId, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var query = db.LoadoutPresets.Where(p => p.UserId == userId);
        if (!string.IsNullOrWhiteSpace(system))
        {
            query = query.Where(p => p.GameSystemKey == system);
        }
        if (!string.IsNullOrWhiteSpace(datasheetId))
        {
            query = query.Where(p => p.DatasheetId == datasheetId);
        }

        var presets = await query
            .OrderBy(p => p.DatasheetName)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);
        return Results.Ok(presets.Select(PresetToDto));
    }

    private static async Task<IResult> CreatePreset(
        CreatePresetRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 80)
        {
            return Problem("name", "Preset name must be 1-80 characters.");
        }
        if (string.IsNullOrWhiteSpace(request.GameSystemKey) || string.IsNullOrWhiteSpace(request.DatasheetId))
        {
            return Problem("datasheet", "A game system and datasheet are required.");
        }

        var sheet = await LoadSheet(db, request.DatasheetId, request.GameSystemKey, ct);
        if (sheet is null)
        {
            return Results.NotFound(new { error = "Unknown datasheet for this game system." });
        }

        var size = PickSize(sheet.Sizes, request.Models);
        List<SelectedOption> options =
        [
            .. ResolveOptions(sheet.Options, request.SelectedOptionIds),
            .. ResolveVariants(sheet.VariantGroups, request.Variants),
        ];

        // Saving over a same-named preset for this unit overwrites it, so a tweak
        // doesn't pile up duplicates.
        var preset = await db.LoadoutPresets.FirstOrDefaultAsync(
            p => p.UserId == userId && p.GameSystemKey == request.GameSystemKey
                 && p.DatasheetId == request.DatasheetId && p.Name == name, ct);
        if (preset is null)
        {
            var count = await db.LoadoutPresets.CountAsync(
                p => p.UserId == userId && p.GameSystemKey == request.GameSystemKey && p.DatasheetId == request.DatasheetId, ct);
            if (count >= MaxPresetsPerSheet)
            {
                return Problem("name", $"You already have {MaxPresetsPerSheet} presets for this unit.");
            }
            preset = new LoadoutPreset
            {
                UserId = userId,
                GameSystemKey = request.GameSystemKey,
                DatasheetId = request.DatasheetId,
                Name = name,
            };
            db.LoadoutPresets.Add(preset);
        }

        preset.DatasheetName = sheet.Name;
        preset.ModelCount = size?.Models ?? 1;
        preset.Points = size?.Points ?? sheet.Points;
        preset.SelectedOptions = options;
        preset.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(PresetToDto(preset));
    }

    private static async Task<IResult> DeletePreset(
        Guid id, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var preset = await db.LoadoutPresets.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId, ct);
        if (preset is null)
        {
            return Results.NotFound();
        }
        db.LoadoutPresets.Remove(preset);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static LoadoutPresetDto PresetToDto(LoadoutPreset p)
    {
        var optionsPoints = p.SelectedOptions.Sum(o => o.Points * Math.Max(1, o.Quantity));
        return new LoadoutPresetDto(
            p.Id, p.GameSystemKey, p.DatasheetId, p.DatasheetName, p.Name, p.ModelCount,
            p.Points, p.Points + optionsPoints,
            p.SelectedOptions.Select(o => new SelectedOptionDto(o.Id, o.Name, o.Points, Math.Max(1, o.Quantity))).ToList(),
            p.UpdatedAt);
    }
}
