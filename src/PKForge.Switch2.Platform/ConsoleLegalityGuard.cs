using System.Globalization;
using System.Security.Cryptography;
using PKForge.Domain;
using PKForge.Infrastructure;

namespace PKForge.Switch2.Platform;

/// <summary>
/// Refuses a console write that would bring in an illegal Pokémon (a condition for writes, 2026-11-23): one that
/// PKHeX calls illegal in the candidate and that was not already in the save as an illegal Pokémon. Moving a Pokémon is
/// not an edit, so a box-and-slot-free fingerprint names "the same Pokémon". A restore point's bytes are always allowed
/// (restoring is the way back), and HaX mode is the deliberate override.
/// </summary>
public sealed class ConsoleLegalityGuard(ISaveEngine engine, ILegalityService legality, IBackupService backups, Func<bool> haxOn)
    : IConsoleWriteGuard
{
    public async ValueTask<string?> RefusalAsync(string documentId, ReadOnlyMemory<byte> current, ReadOnlyMemory<byte> candidate,
        CancellationToken cancellationToken)
    {
        if (haxOn())
            return null;
        var hash = Convert.ToHexString(SHA256.HashData(candidate.Span));
        if ((await backups.ListAsync(cancellationToken)).Any(point =>
                point.DocumentId == documentId && string.Equals(point.Sha256, hash, StringComparison.OrdinalIgnoreCase)))
            return null;
        var game = ConsoleDocumentId.TryParse(documentId, out var titleId, out _, out _)
            && EmulatorSaveHeuristics.SwitchTitles.TryGetValue(titleId.ToString("X16", CultureInfo.InvariantCulture), out var name) ? name : null;
        var known = Illegal(current, game, cancellationToken).Select(found => found.Fingerprint).ToHashSet(StringComparer.Ordinal);
        var added = Illegal(candidate, game, cancellationToken).Where(found => !known.Contains(found.Fingerprint)).Select(found => found.Name).ToList();
        return added.Count == 0
            ? null
            : $"Not saved: {string.Join(", ", added)} would be illegal. Illegal Pokémon used online (trades, battles, Pokémon HOME) can get an "
              + "account banned. Turn on HaX mode in Settings to save them anyway.";
    }

    List<(string Fingerprint, string Name)> Illegal(ReadOnlyMemory<byte> save, string? game, CancellationToken cancellationToken)
    {
        using var session = engine.OpenSession(save, game);
        return [.. legality.Sweep(session, cancellationToken: cancellationToken).Where(slot => !slot.Valid).Select(slot =>
        {
            var detail = session.ReadEntity(slot.Box, slot.Slot);
            return (Fingerprint(detail), string.IsNullOrWhiteSpace(detail.Nickname) ? detail.SpeciesName : detail.Nickname);
        })];
    }

    static string Fingerprint(EntityDetail d) => string.Join('|', d.Species, d.Form, d.Nickname, d.Level, d.Nature, d.Ability,
        d.HeldItem, d.Move1, d.Move2, d.Move3, d.Move4, string.Join(',', d.IVs), string.Join(',', d.EVs), d.IsShiny, d.Ball,
        d.OriginalTrainer, d.Gender);
}
