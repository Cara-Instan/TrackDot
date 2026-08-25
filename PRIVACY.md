# Privacy Policy

**Last Updated:** August 25, 2026

TrackDot ("the Software", "the Application", "we", "us", or "our") is committed to protecting your privacy. This Privacy Policy explains our practices regarding the collection, use, and disclosure of information when you use TrackDot.

---

## 1. Core Principle: Local-First & Zero Telemetry

TrackDot is built with a **strict privacy-first architecture**:

- **No User Accounts:** You do not need to create an account, register, or provide any personal information (such as name, email address, or payment details) to use TrackDot.
- **No Analytics or Telemetry:** TrackDot contains no telemetry SDKs, analytics tracking (e.g., Google Analytics, Mixpanel), behavioral trackers, or background "phone-home" services.
- **No Advertising:** TrackDot does not display advertisements and does not collect information for ad-targeting purposes.

---

## 2. What Data Stays on Your Computer

All application configurations and states are stored **locally on your device**:

| Data Type | Storage Location | Purpose |
|---|---|---|
| **Preferences & Settings** | `HKCU\Software\TrackDot` (Registry) or `settings.json` (Portable Mode) | Stores UI theme, window opacity, hotkey bindings, lyrics translation toggles, and per-app Discord RPC preferences. |
| **Startup Preference** | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Allows optional automatic startup when you sign into Windows. |
| **Crash Logs** | `%LocalAppData%\TrackDot\crash.log` | Written locally only if an unhandled exception occurs to assist with local troubleshooting. Crash logs are **never** uploaded automatically. |

---

## 3. Network Communication & External APIs

TrackDot does not send any personal information across the network. Network requests initiated by TrackDot are strictly limited to the following optional features:

### 3.1 Synced Lyrics Retrieval
When the Lyrics Window or Floating Lyrics HUD is opened, TrackDot queries public lyrics APIs to fetch time-synced lyrics:
- **Primary Endpoint:** [Unison](https://unison.boidu.dev)
- **Fallback Endpoint:** [LRCLIB](https://lrclib.net)

**What is transmitted:**
- Track title, artist name, album name, and duration.
- Standard HTTP request headers sent by the .NET `HttpClient` (such as User-Agent).

### 3.2 Discord Rich Presence & Album Artwork Lookup
When Discord Rich Presence (RPC) is enabled in TrackDot's settings for an allowed media player:
1. **Local IPC:** TrackDot communicates over a **local Windows Named Pipe** (`\\.\pipe\discord-ipc-0`) directly to your local Discord desktop client.
2. **Artwork Resolution:** To display high-resolution album artwork on your Discord status (instead of a static placeholder), TrackDot queries public song metadata endpoints:
   - **iTunes Search API** (`itunes.apple.com`)
   - **Deezer Search API** (`api.deezer.com` fallback)

**What is transmitted during artwork lookup:**
- Track title and artist name.
- No personal user identifiers, account credentials, or device IDs are transmitted.
- Artwork lookup results are cached locally in memory to minimize network requests.
- If Discord Rich Presence is disabled or if a specific media player is unchecked in settings, zero Discord activity and zero artwork lookup requests are performed for that application.

---

## 4. Third-Party Services and Privacy Policies

Your use of third-party platforms that integrate with TrackDot or provide services to TrackDot may be subject to their respective privacy policies:

- **Microsoft Windows:** [Microsoft Privacy Statement](https://privacy.microsoft.com/en-us/privacystatement)
- **Discord:** [Discord Privacy Policy](https://discord.com/privacy)
- **Apple / iTunes API:** [Apple Privacy Policy](https://www.apple.com/legal/privacy/)
- **Deezer:** [Deezer Privacy Policy](https://www.deezer.com/legal/cgu)
- **LRCLIB:** [LRCLIB Documentation & Privacy](https://lrclib.net/docs)
- **Unison:** [Unison Privacy & Terms](https://unison.boidu.dev)

---

## 5. Children's Privacy

TrackDot is not directed to children under the age of 13, and we do not knowingly collect or solicit any personal information from children.

---

## 6. Changes to This Privacy Policy

We may update this Privacy Policy from time to time. Any changes will be reflected in the project repository with an updated "Last Updated" date. We encourage you to review this document periodically.

---

## 7. Contact & Open Source Inquiries

TrackDot is open-source software maintained by the community. If you have questions, concerns, or feedback regarding this Privacy Policy:

- **Repository:** [https://github.com/Cara-Instan/TrackDot](https://github.com/Cara-Instan/TrackDot)
- **Maintainer:** [https://github.com/herlandroando](https://github.com/herlandroando)
- **Issues & Discussions:** [https://github.com/Cara-Instan/TrackDot/issues](https://github.com/Cara-Instan/TrackDot/issues)

