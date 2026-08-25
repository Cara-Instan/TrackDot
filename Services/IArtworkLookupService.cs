using System.Threading;
using System.Threading.Tasks;

namespace TrackDot.Services;

/// <summary>
/// Service for resolving online album artwork URLs (iTunes, Deezer, etc.)
/// for external integrations like Discord Rich Presence.
/// </summary>
public interface IArtworkLookupService
{
    /// <summary>
    /// Fetches a high-resolution public artwork URL for the given track.
    /// Returns <see langword="null"/> if not found or unavailable.
    /// </summary>
    Task<string?> GetArtworkUrlAsync(
        string title,
        string artist,
        string album = "",
        CancellationToken cancellationToken = default);
}

