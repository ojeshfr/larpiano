using System;
using System.Collections.Generic;

namespace Keynote.Core;

public enum PlaylistRepeatMode
{
    None = 0,
    One = 1,
    All = 2
}

public sealed record PlaylistItem(
    string Id,
    string PlaylistId,
    string SongSha256,
    string Title,
    string Artist,
    double DurationSeconds = 0.0,
    int OrderIndex = 0,
    DateTimeOffset? CreatedAt = null);

public sealed record Playlist(
    string Id,
    string Name,
    string? UserId = null,
    bool IsPublic = false,
    int ItemCount = 0,
    double TotalDuration = 0.0,
    IReadOnlyList<PlaylistItem>? Items = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null)
{
    public override string ToString() => $"{Name} ({ItemCount} songs)";
}
