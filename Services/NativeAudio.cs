using System.Runtime.InteropServices;

namespace TrackDot.Services;

// ─────────────────────────────────────────────────────────────────────────────
// CoreAudio COM interface declarations — used exclusively by AudioVolumeService.
//
// Guidelines followed:
//  • InterfaceIsIUnknown — IUnknown methods (QueryInterface / AddRef / Release)
//    are implicit; declared methods start at vtable slot 3.
//  • Methods not called by production code are declared as [PreserveSig] int
//    with no parameters (placeholder stubs). Their parameter lists do not
//    affect the vtable-slot assignment — only the METHOD COUNT matters for
//    correct slot mapping. We never invoke a stub, so the type mismatch is safe.
//  • For COM interfaces that inherit (e.g. IAudioSessionControl2 ⊃
//    IAudioSessionControl), the derived interface must redeclare ALL parent
//    methods first, in order, before its own new methods.
// ─────────────────────────────────────────────────────────────────────────────

// IMMDeviceEnumerator — IID: A95664D2-9614-4F35-A746-DE8DB63617E6
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    // slot 3 — EnumAudioEndpoints (not used)
    [PreserveSig] int Stub_EnumAudioEndpoints();
    // slot 4 — GetDefaultAudioEndpoint ← needed
    [PreserveSig] int GetDefaultAudioEndpoint(
        int dataFlow, int role,
        [MarshalAs(UnmanagedType.Interface)] out IMMDevice ppEndpoint);
    // slot 5 — GetDevice (not used)
    [PreserveSig] int Stub_GetDevice();
    // slot 6 — RegisterEndpointNotificationCallback (not used)
    [PreserveSig] int Stub_RegisterEndpointNotificationCallback();
    // slot 7 — UnregisterEndpointNotificationCallback (not used)
    [PreserveSig] int Stub_UnregisterEndpointNotificationCallback();
}

// IMMDevice — IID: D666063F-1587-4E43-81F1-B948E807363F
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    // slot 3 — Activate ← needed
    [PreserveSig] int Activate(
        ref Guid iid,
        uint dwClsCtx,
        IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    // slot 4 — OpenPropertyStore (not used)
    [PreserveSig] int Stub_OpenPropertyStore();
    // slot 5 — GetId (not used)
    [PreserveSig] int Stub_GetId();
    // slot 6 — GetState (not used)
    [PreserveSig] int Stub_GetState();
}

// IAudioSessionManager2 — IID: 77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F
// Vtable: 2 IAudioSessionManager methods first, then 5 own methods.
[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // slot 3 — GetAudioSessionControl (IAudioSessionManager, not used)
    [PreserveSig] int Stub_GetAudioSessionControl();
    // slot 4 — GetSimpleAudioVolume (IAudioSessionManager, not used)
    [PreserveSig] int Stub_GetSimpleAudioVolume();
    // slot 5 — GetSessionEnumerator ← needed
    [PreserveSig] int GetSessionEnumerator(
        [MarshalAs(UnmanagedType.Interface)] out IAudioSessionEnumerator ppSessionList);
    // slot 6 — RegisterSessionNotification (not used)
    [PreserveSig] int Stub_RegisterSessionNotification();
    // slot 7 — UnregisterSessionNotification (not used)
    [PreserveSig] int Stub_UnregisterSessionNotification();
    // slot 8 — RegisterDuckNotification (not used)
    [PreserveSig] int Stub_RegisterDuckNotification();
    // slot 9 — UnregisterDuckNotification (not used)
    [PreserveSig] int Stub_UnregisterDuckNotification();
}

// IAudioSessionEnumerator — IID: E2F5BB11-0570-40CA-ACDD-3AA01277DEE8
[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    // slot 3 — GetCount
    [PreserveSig] int GetCount(out int SessionCount);
    // slot 4 — GetSession
    [PreserveSig] int GetSession(
        int SessionIndex,
        [MarshalAs(UnmanagedType.Interface)] out IAudioSessionControl Session);
}

// IAudioSessionControl — IID: F4B1A599-7266-4319-A8CA-E70ACB11E8CD
// 9 methods (slots 3–11). We QI to IAudioSessionControl2 for the PID,
// and to ISimpleAudioVolume for volume/mute, so this interface's
// methods are never called — stubs only.
[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig] int Stub_GetState();
    [PreserveSig] int Stub_GetDisplayName();
    [PreserveSig] int Stub_SetDisplayName();
    [PreserveSig] int Stub_GetIconPath();
    [PreserveSig] int Stub_SetIconPath();
    [PreserveSig] int Stub_GetGroupingParam();
    [PreserveSig] int Stub_SetGroupingParam();
    [PreserveSig] int Stub_RegisterAudioSessionNotification();
    [PreserveSig] int Stub_UnregisterAudioSessionNotification();
}

// IAudioSessionControl2 — IID: BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D
// Vtable = 9 IAudioSessionControl slots (3–11) + 5 own slots (12–16).
[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // Inherited from IAudioSessionControl (slots 3–11, all stubs)
    [PreserveSig] int Stub_GetState();
    [PreserveSig] int Stub_GetDisplayName();
    [PreserveSig] int Stub_SetDisplayName();
    [PreserveSig] int Stub_GetIconPath();
    [PreserveSig] int Stub_SetIconPath();
    [PreserveSig] int Stub_GetGroupingParam();
    [PreserveSig] int Stub_SetGroupingParam();
    [PreserveSig] int Stub_RegisterAudioSessionNotification();
    [PreserveSig] int Stub_UnregisterAudioSessionNotification();
    // Own methods
    // slot 12 — GetSessionIdentifier (not used)
    [PreserveSig] int Stub_GetSessionIdentifier();
    // slot 13 — GetSessionInstanceIdentifier (not used)
    [PreserveSig] int Stub_GetSessionInstanceIdentifier();
    // slot 14 — GetProcessId ← needed
    [PreserveSig] int GetProcessId(out uint pRetVal);
    // slot 15 — IsSystemSoundsSession (not used)
    [PreserveSig] int Stub_IsSystemSoundsSession();
    // slot 16 — SetDuckingPreference (not used)
    [PreserveSig] int Stub_SetDuckingPreference();
}

// ISimpleAudioVolume — IID: 87CE5498-68D6-44E5-9215-6DA47EF883D8
// 4 methods (slots 3–6).
[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    // slot 3
    [PreserveSig]
    int SetMasterVolume(
        float fLevel,
        [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
    // slot 4
    [PreserveSig]
    int GetMasterVolume(out float pfLevel);
    // slot 5
    [PreserveSig]
    int SetMute(
        [MarshalAs(UnmanagedType.Bool)] bool bMute,
        [MarshalAs(UnmanagedType.LPStruct)] Guid EventContext);
    // slot 6
    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}
