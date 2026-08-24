namespace Cleanuparr.Domain.Entities.Sonarr;

/// <summary>
/// The <c>ManualImport</c> command payload for <c>POST /api/v3/command</c>.
/// </summary>
public sealed record SonarrManualImportCommand
{
    /// <summary>
    /// The command name Sonarr's generic command endpoint dispatches on. Always "ManualImport".
    /// </summary>
    public string Name { get; init; } = "ManualImport";

    /// <summary>
    /// The files to import.
    /// </summary>
    public List<SonarrManualImportCommandFile> Files { get; init; } = [];

    /// <summary>
    /// How Sonarr should place the file in the library: Auto, Move or Copy.
    /// </summary>
    public string ImportMode { get; init; } = "Move";
}
