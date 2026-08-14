using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TrackDot.Services;

/// <summary>
/// Wraps the CoreAudio <c>IAudioSessionManager2</c> COM API to read and
/// write the master volume and mute state for an application identified
/// by its SMTC Source App User Model ID (AUMID).
/// </summary>
/// <remarks>
/// <para>
/// <b>Matching strategy.</b> SMTC does not expose a per-session PID.
/// This service enumerates every audio session on the default render
/// endpoint, reads each session's PID via
/// <c>IAudioSessionControl2.GetProcessId</c>, and looks up the
/// process name. It then performs a case-insensitive heuristic match
/// against the AUMID using two stages:
/// </para>
/// <list type="number">
///   <item>
///     <b>Primary — process name ↔ AUMID.</b> Covers the common case
///     ("Spotify.exe" → process "Spotify",
///     "com.spotify.client" → any process whose name contains "Spotify").
///   </item>
///   <item>
///     <b>Secondary — session display name ↔ AUMID.</b> Covers players
///     whose audio is produced by a separate renderer process whose
///     name has no overlap with the AUMID (Spotify's
///     <c>SpotifyRenderer.exe</c> vs AUMID <c>com.spotify.client</c>,
///     some Electron-based players, etc.). The OS-set display name
///     (e.g. "Spotify", "Microsoft Edge") still contains a segment
///     matching the AUMID, so the same segment-substring rules apply.
///   </item>
/// </list>
/// <para>
/// <b>Failure safety.</b> Every public method wraps its body in a
/// <c>try/catch</c>. When matching fails (no audio session found, COM
/// error, process already exited), reads return
/// <c>Volume=1.0, IsMuted=false</c> and writes are silently dropped.
/// The popover remains fully functional; only the volume controls are
/// degraded.
/// </para>
/// <para>
/// <b>Threading.</b> All calls are made on the WPF UI thread (the
/// caller is <see cref="MediaControllerService"/>, which marshals
/// everything to the dispatcher context). COM is initialised by WPF's
/// STA apartment, so <c>CoCreateInstance</c> is safe without an
/// explicit <c>CoInitialize</c>.
/// </para>
/// </remarks>
internal sealed class AudioVolumeService : IDisposable
{
    // CLSID_MMDeviceEnumerator
    private static readonly Guid ClsidMmDeviceEnumerator =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    // IID_IAudioSessionManager2 — passed to IMMDevice.Activate
    private static readonly Guid IidAudioSessionManager2 =
        new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private IMMDeviceEnumerator? _enumerator;
    private bool _disposed;

    /// <summary>
    /// Initialises the COM enumerator. If CoreAudio is unavailable
    /// (e.g. running headless in tests, or on a system without an
    /// audio device), the constructor silently completes — all
    /// subsequent calls are no-ops.
    /// </summary>
    public AudioVolumeService()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator);
            if (type is not null)
                _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
        }
        catch
        {
            // No CoreAudio — degrade gracefully.
        }
    }

    /// <summary>
    /// Returns the current master volume (0.0–1.0) and mute state for
    /// the audio session matching <paramref name="aumid"/>.
    /// Returns <c>(1.0f, false)</c> on any failure.
    /// </summary>
    public (float Volume, bool IsMuted) GetVolumeInfo(string? aumid)
    {
        if (_disposed || _enumerator is null || string.IsNullOrEmpty(aumid))
            return (1.0f, false);
        try
        {
            var vol = FindSessionVolume(aumid);
            if (vol is null) return (1.0f, false);
            vol.GetMasterVolume(out float level);
            vol.GetMute(out bool muted);
            Marshal.ReleaseComObject(vol);
            return (level, muted);
        }
        catch { return (1.0f, false); }
    }

    /// <summary>
    /// Sets the master volume of the audio session matching
    /// <paramref name="aumid"/>. <paramref name="volume"/> is clamped
    /// to [0.0, 1.0]. No-op on failure.
    /// </summary>
    public void SetVolume(string? aumid, float volume)
    {
        if (_disposed || _enumerator is null || string.IsNullOrEmpty(aumid)) return;
        try
        {
            var vol = FindSessionVolume(aumid);
            if (vol is null) return;
            vol.SetMasterVolume(Math.Clamp(volume, 0f, 1f), Guid.Empty);
            Marshal.ReleaseComObject(vol);
        }
        catch { }
    }

    /// <summary>
    /// Sets the mute state of the audio session matching
    /// <paramref name="aumid"/>. No-op on failure.
    /// </summary>
    public void SetMute(string? aumid, bool mute)
    {
        if (_disposed || _enumerator is null || string.IsNullOrEmpty(aumid)) return;
        try
        {
            var vol = FindSessionVolume(aumid);
            if (vol is null) return;
            vol.SetMute(mute, Guid.Empty);
            Marshal.ReleaseComObject(vol);
        }
        catch { }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks every audio session on the default render endpoint and
    /// returns the <see cref="ISimpleAudioVolume"/> for the first
    /// session whose process name matches <paramref name="aumid"/>.
    /// The caller is responsible for releasing the returned COM object.
    /// </summary>
    private ISimpleAudioVolume? FindSessionVolume(string aumid)
    {
        if (_enumerator is null) return null;

        // Get the default audio render endpoint (eRender=0, eMultimedia=1)
        int hr = _enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device);
        if (hr != 0) return null;

        try
        {
            // Activate IAudioSessionManager2 on the endpoint
            Guid mgr2Id = IidAudioSessionManager2;
            hr = device.Activate(ref mgr2Id, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out object mgr2Obj);
            if (hr != 0) return null;
            var mgr2 = (IAudioSessionManager2)mgr2Obj;

            mgr2.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
            sessionEnum.GetCount(out int count);

            for (int i = 0; i < count; i++)
            {
                sessionEnum.GetSession(i, out IAudioSessionControl ctrl);
                try
                {
                    // QI to IAudioSessionControl2 to get the PID
                    var ctrl2 = (IAudioSessionControl2)ctrl;
                    ctrl2.GetProcessId(out uint pid);
                    if (pid == 0) continue;

                    // Read the OS-set session display name (slot 5, inherited
                    // from IAudioSessionControl). May be null when the
                    // session has no display name — AumidMatchesProcess
                    // treats that as "no secondary signal".
                    string? displayName = null;
                    try { ctrl2.GetDisplayName(out displayName); }
                    catch { /* non-fatal — fall through with null */ }

                    try
                    {
                        using var proc = Process.GetProcessById((int)pid);
                        if (AumidMatchesProcess(aumid, proc.ProcessName, displayName))
                        {
                            // QI the same session object for ISimpleAudioVolume
                            return (ISimpleAudioVolume)ctrl;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Process exited between GetProcessId and GetProcessById — skip.
                    }
                }
                catch
                {
                    // Session does not implement IAudioSessionControl2 (e.g. system sounds)
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the process identified by
    /// <paramref name="processName"/> (and optionally the OS-set session
    /// <paramref name="displayName"/>) is a plausible match for the SMTC
    /// AUMID <paramref name="aumid"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three strategies, in priority order. The first match wins:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <b>.exe suffix — process name.</b> Strip the extension and
    ///     compare directly. "chrome.exe" matches process "chrome".
    ///   </item>
    ///   <item>
    ///     <b>Reverse-DNS / package family name — process name.</b> Split
    ///     on common delimiters and do a substring match on each segment
    ///     that is at least 4 characters. "com.spotify.client" → segment
    ///     "spotify" → matches process "Spotify".
    ///   </item>
    ///   <item>
    ///     <b>Reverse-DNS — session display name (secondary).</b> Same
    ///     segment-substring rules applied to the OS-set session display
    ///     name. Covers players whose audio is produced by a separate
    ///     renderer process whose name has no overlap with the AUMID
    ///     (e.g. Spotify's <c>SpotifyRenderer.exe</c> vs AUMID
    ///     <c>com.spotify.client</c>, where the renderer name fails
    ///     stages 1–2 but the OS-set display name "Spotify" matches
    ///     the "spotify" segment in stage 3). A <see langword="null"/>
    ///     or whitespace-only display name is treated as "no signal"
    ///     — stage 3 is skipped, leaving the primary stages' verdict.
    ///   </item>
    /// </list>
    /// </remarks>
    internal static bool AumidMatchesProcess(string aumid, string processName, string? displayName)
    {
        // Stage 1+2 — process-name heuristics (existing behaviour, unchanged).
        if (aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var stem = Path.GetFileNameWithoutExtension(aumid);
            if (processName.Equals(stem, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        else
        {
            foreach (var seg in aumid.Split(['.', '!', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (seg.Length >= 4 &&
                    processName.Contains(seg, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Stage 3 — display-name secondary match.
        // Skip when there is no signal. Null/whitespace display name =
        // "no display name set by the OS" — leave the primary verdict.
        if (string.IsNullOrWhiteSpace(displayName)) return false;

        // For the .exe form, stage 3 is intentionally NOT applied: a
        // stem-equality primary rule paired with a permissive substring
        // secondary rule would let "chrome.exe" AUMID match a session
        // whose display name happens to be "Google Chrome" — too risky
        // for cross-app collisions given how short common .exe stems
        // are. We accept that .exe AUMIDs whose audio renderer ships
        // under a totally different name will fail to match (no
        // production player observed today exhibits this shape — the
        // renderer name is always the same stem).
        if (aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;

        // Non-.exe AUMIDs — segment-substring against the display name.
        foreach (var seg in aumid.Split(['.', '!', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg.Length >= 4 &&
                displayName.Contains(seg, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_enumerator is not null)
        {
            try { Marshal.ReleaseComObject(_enumerator); } catch { }
            _enumerator = null;
        }
    }
}
