using TrackDot.Services;
using Xunit;

namespace TrackDot.Tests;

/// <summary>
/// Pure-function tests for <see cref="AudioVolumeService.AumidMatchesProcess"/>.
/// These cover the heuristic that maps an SMTC Source App User Model ID
/// (AUMID) to an audio session's owning process — without spinning up
/// any COM, P/Invoke, or live audio device.
/// </summary>
/// <remarks>
/// <para>
/// The matcher has three stages, in priority order:
/// </para>
/// <list type="number">
///   <item><b>.exe suffix — process name.</b> Strip the extension and compare directly.</item>
///   <item><b>Reverse-DNS — process name.</b> Segment-substring on each ≥4-char segment.</item>
///   <item><b>Reverse-DNS — session display name (secondary).</b> Same rules applied
///         to the OS-set session display name. Covers renderer-process shapes that
///         fail stages 1–2 (e.g. Spotify's <c>SpotifyRenderer.exe</c> vs
///         AUMID <c>com.spotify.client</c>, where the OS-set display name
///         "Spotify" matches the "spotify" segment).</item>
/// </list>
/// </remarks>
public sealed class AudioVolumeMatcherTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Stage 1 — .exe suffix / process name
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Exe_suffix_matches_exact_process_name()
    {
        Assert.True(AudioVolumeService.AumidMatchesProcess("chrome.exe", "chrome", displayName: null));
    }

    [Fact]
    public void Exe_suffix_matches_case_insensitively()
    {
        Assert.True(AudioVolumeService.AumidMatchesProcess("CHROME.EXE", "Chrome", displayName: null));
    }

    [Fact]
    public void Exe_suffix_does_not_partial_match()
    {
        // "chrome.exe" must NOT match "chromedriver" — that would over-match
        // and grab a different process's audio session.
        Assert.False(AudioVolumeService.AumidMatchesProcess("chrome.exe", "chromedriver", displayName: null));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Stage 2 — Reverse-DNS / process name
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reverse_dns_segment_matches_substring_of_process()
    {
        // "com.spotify.client" → "spotify" (length ≥ 4) → substring of "Spotify".
        Assert.True(AudioVolumeService.AumidMatchesProcess("com.spotify.client", "Spotify", displayName: null));
    }

    [Fact]
    public void Reverse_dns_segment_is_case_insensitive()
    {
        Assert.True(AudioVolumeService.AumidMatchesProcess("com.spotify.client", "SPOTIFY", displayName: null));
    }

    [Fact]
    public void Reverse_dns_returns_false_when_no_segment_overlaps()
    {
        // AUMID "com.app.x" splits to ["com", "app", "x"] — every
        // segment is below the 4-char floor, so stage 2 cannot match.
        // Stage 3 also has no segment ≥ 4 to work with, so the result
        // is false even with a display name that contains all of them.
        Assert.False(AudioVolumeService.AumidMatchesProcess(
            "com.app.x", "totally-unrelated", displayName: "Com App X Client"));
    }

    [Fact]
    public void Reverse_dns_splits_on_underscore_and_dash()
    {
        // Spotify's package family name uses underscores; the segment rules
        // must split them like dots.
        Assert.True(AudioVolumeService.AumidMatchesProcess("SpotifyAB_SpotifyMusic!test-foo", "spotifymusic", displayName: null));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Stage 3 — display name secondary match (the new behaviour)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Display_name_segment_matches_when_process_name_has_no_overlap()
    {
        // Motivating case: a renderer-subprocess player whose process
        // name shares no segment with its AUMID.
        //   AUMID:       Contoso.MediaPlayer.PremiumAudio
        //   ProcessName: audioprocessor.exe   ← none of "contoso"/"mediaplayer"/"premiumaudio"
        //                                         are substrings → stage 1+2 fail
        //   DisplayName: Contoso Media Player PremiumAudio
        //                ← contains "contoso" + "premiumaudio" (length ≥ 4) →
        //                stage 3 succeeds
        Assert.True(AudioVolumeService.AumidMatchesProcess(
            aumid: "Contoso.MediaPlayer.PremiumAudio",
            processName: "audioprocessor.exe",
            displayName: "Contoso Media Player PremiumAudio"));
    }

    [Fact]
    public void Display_name_segment_skips_segments_shorter_than_four_chars()
    {
        // AUMID "com.x.app" splits to ["com", "x", "app"] — every
        // segment is below the 4-char floor; stage 3 cannot match even
        // though the display name contains "app" and "x".
        Assert.False(AudioVolumeService.AumidMatchesProcess(
            aumid: "com.x.app",
            processName: "RendererProcess",
            displayName: "My App X Client"));
    }

    [Fact]
    public void Secondary_match_requires_non_empty_display_name()
    {
        // Stage 1+2 fail (no segment overlap), display name is null —
        // stage 3 cannot run, no match.
        Assert.False(AudioVolumeService.AumidMatchesProcess(
            aumid: "Contoso.MediaPlayer.PremiumAudio",
            processName: "audioprocessor.exe",
            displayName: null));
    }

    [Fact]
    public void Secondary_match_treats_whitespace_display_name_as_no_signal()
    {
        Assert.False(AudioVolumeService.AumidMatchesProcess(
            aumid: "Contoso.MediaPlayer.PremiumAudio",
            processName: "audioprocessor.exe",
            displayName: "   "));
    }

    [Fact]
    public void Primary_match_short_circuits_before_secondary_path_runs()
    {
        // Stage 1+2 already returned true (segment "spotify" is in
        // "Spotify"); the secondary path is irrelevant. Verifies the
        // "first match wins" ordering.
        Assert.True(AudioVolumeService.AumidMatchesProcess(
            aumid: "com.spotify.client",
            processName: "Spotify",
            displayName: null));
    }

    [Fact]
    public void Exe_aumid_does_not_use_secondary_match()
    {
        // The .exe-suffix primary path uses stem equality; the secondary
        // path is intentionally NOT applied to .exe AUMIDs to avoid
        // cross-app collisions from overly permissive substring rules
        // (e.g. "chrome.exe" AUMID matching a session whose display name
        // is "Google Chrome" — see comment in AudioVolumeService).
        Assert.False(AudioVolumeService.AumidMatchesProcess(
            aumid: "chrome.exe",
            processName: "totally-different-process",
            displayName: "Google Chrome"));
    }

    [Fact]
    public void No_match_when_both_process_and_display_name_disagree()
    {
        // AUMID has no segment overlap with either signal — the canonical
        // "wrong app" failure.
        Assert.False(AudioVolumeService.AumidMatchesProcess(
            aumid: "Contoso.MediaPlayer.PremiumAudio",
            processName: "Discord",
            displayName: "Discord"));
    }
}
