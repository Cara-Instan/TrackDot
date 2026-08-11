namespace TrackDot.Models;

/// <summary>
/// Lightweight DTO representing one SMTC session in the session-picker list.
/// Produced by <see cref="TrackDot.Services.IMediaControllerService.AvailableSessions"/>
/// and consumed exclusively by the session-picker UI in the popover.
/// </summary>
/// <param name="SourceAppUserModelId">
/// The raw AUMID of the source application as returned by SMTC.
/// Passed back as the <c>CommandParameter</c> to
/// <see cref="TrackDot.ViewModels.MainViewModel.SelectSessionCommand"/>.
/// </param>
/// <param name="DisplayName">
/// Human-readable application name formatted via
/// <c>MainViewModelHelpers.FormatAppName</c> (e.g. "Spotify", "Google Chrome").
/// </param>
/// <param name="IsCurrent">
/// <see langword="true"/> when this session is currently the one whose
/// metadata the popover is showing.
/// </param>
public sealed record MediaSessionInfo(
    string SourceAppUserModelId,
    string DisplayName,
    bool IsCurrent);
