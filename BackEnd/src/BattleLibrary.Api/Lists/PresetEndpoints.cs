using System.Security.Claims;
using BattleLibrary.Domain.Lists;
using BattleLibrary.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BattleLibrary.Api.Lists;

// Unit presets: saved typed fields for a unit, reusable when adding it to any of
// the owner's lists for the same game system.
public static partial class ListEndpoints
{
    private const int MaxPresets = 200;

    public static RouteGroupBuilder MapPresetEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/presets").RequireAuthorization();

        group.MapGet("", GetPresets);
        group.MapPost("", CreatePreset);
        group.MapDelete("/{id:guid}", DeletePreset);

        return group;
    }

    private static async Task<IResult> GetPresets(
        string? system, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var query = db.LoadoutPresets.Where(p => p.UserId == userId);
        if (!string.IsNullOrWhiteSpace(system))
        {
            var key = GameSystems.Normalize(system);
            query = query.Where(p => p.GameSystemKey == key);
        }

        var presets = await query.OrderBy(p => p.UnitName).ThenBy(p => p.Name).ToListAsync(ct);
        return Results.Ok(presets.Select(PresetToDto));
    }

    // Saving over a same-named preset for the same unit and system overwrites it.
    private static async Task<IResult> CreatePreset(
        PresetFieldsRequest request, ClaimsPrincipal principal, BattleLibraryDbContext db, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var system = GameSystems.Normalize(request.GameSystemKey);
        var unitName = Clip(request.UnitName, 200) ?? DefaultEntryName;
        var name = Clip(request.Name, 80) ?? Clip(unitName, 80)!;

        var preset = await db.LoadoutPresets.FirstOrDefaultAsync(
            p => p.UserId == userId && p.GameSystemKey == system && p.UnitName == unitName && p.Name == name, ct);
        if (preset is null)
        {
            if (await db.LoadoutPresets.CountAsync(p => p.UserId == userId, ct) >= MaxPresets)
            {
                return Problem("name", $"You already have {MaxPresets} presets.");
            }
            preset = new LoadoutPreset { UserId = userId, GameSystemKey = system, UnitName = unitName, Name = name };
            db.LoadoutPresets.Add(preset);
        }

        preset.Points = request.Points;
        preset.ModelCount = request.ModelCount;
        preset.Wargear = Clip(request.Wargear, 2000);
        preset.Enhancement = Clip(request.Enhancement, 200);
        preset.Notes = Clip(request.Notes, 2000);
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

    private static LoadoutPresetDto PresetToDto(LoadoutPreset p) => new(
        p.Id, p.GameSystemKey, p.Name, p.UnitName, p.Points, p.ModelCount, p.Wargear, p.Enhancement, p.Notes,
        p.UpdatedAt);
}
