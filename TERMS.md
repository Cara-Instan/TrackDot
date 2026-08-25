# Terms of Service

**Last Updated:** August 25, 2026

Welcome to **TrackDot** ("the Software", "the Application", "we", "us", or "our"). TrackDot is a lightweight, open-source desktop media controller for Windows that interacts with the Windows System Media Transport Controls (SMTC) API.

By downloading, installing, running, or otherwise using TrackDot, you agree to be bound by these Terms of Service ("Terms"). If you do not agree to these Terms, do not install or use the Software.

---

## 1. Open Source License & Software Usage

TrackDot is free, open-source software distributed under the **MIT License**.

- You are granted a worldwide, non-exclusive, royalty-free, and revocable (subject to license terms) right to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software in accordance with the terms set forth in the [LICENSE](LICENSE) file.
- The copyright notice and permission notice shall be included in all copies or substantial portions of the Software.

---

## 2. Permitted and Prohibited Conduct

### 2.1 Permitted Use
You may use TrackDot for personal, educational, or commercial purposes, subject to the MIT License and these Terms.

### 2.2 Prohibited Use
You agree not to:
- Use the Software for any unlawful purpose or in violation of any applicable local, national, or international law or regulation.
- Abuse, disrupt, overload, or send excessive automated requests to third-party APIs used by TrackDot (such as lyrics or artwork providers).
- Distribute modified versions of the Application that introduce malicious code, spyware, or harmful payloads under the TrackDot name.

---

## 3. Third-Party Services and Integrations

TrackDot interacts with various third-party operating system components, local applications, and external web APIs. Your use of these services may be subject to separate terms and privacy policies established by the respective providers:

1. **Windows OS & SMTC:** TrackDot relies on Microsoft Windows System Media Transport Controls and Audio Session APIs to detect and control active media playback.
2. **Discord Rich Presence (RPC):** TrackDot includes an optional feature to broadcast current playback status over a local named pipe to the Discord desktop client. When enabled, public song metadata (title, artist, album) may be queried against public search endpoints (iTunes Search API, Deezer) to provide album cover artwork. Use of Discord is governed by Discord's Terms of Service.
3. **Lyrics Services:** TrackDot provides optional synced lyrics retrieval by querying public community endpoints:
   - **Unison** ([unison.boidu.dev](https://unison.boidu.dev)) — Licensed under ODbL-1.0.
   - **LRCLIB** ([lrclib.net](https://lrclib.net)) — Public community lyrics database.
   
TrackDot does not host, curate, or claim ownership over lyrics, artwork, or musical metadata fetched through these services. All song titles, artist names, album covers, and lyrics remain the intellectual property of their respective copyright holders.

---

## 4. Trademarks & Non-Affiliation

All product names, logos, brands, and registered trademarks mentioned within the Software or its documentation (including, but not limited to, **Microsoft**, **Windows**, **Spotify**, **Discord**, **Apple Music**, **Google Chrome**, **Microsoft Edge**, **TIDAL**, **Amazon Music**, **Deezer**, **SoundCloud**, **VLC**, and **VideoLAN**) are the property of their respective owners.

- Reference to or display of these names, logos, or icons is solely for identification, compatibility, and descriptive purposes.
- TrackDot is an independent open-source project and is **not affiliated with, sponsored by, or endorsed by** Microsoft Corporation, Spotify AB, Discord Inc., Apple Inc., Deezer, Amazon, TIDAL, VideoLAN, or any other third party.

---

## 5. Privacy & Data Collection

TrackDot is built with a **local-first, privacy-by-design** philosophy:

- **No Telemetry / No Tracking:** The Software contains no analytics tracking, telemetry SDKs, or background tracking services.
- **Local Storage:** Your preferences, hotkey bindings, and window positions are stored locally on your device (in the Windows Registry under `HKCU\Software\TrackDot` or in a local `settings.json` file in Portable mode).
- **Network Requests:** Network communication is limited to user-enabled features: looking up track lyrics and resolving album artwork for Discord Rich Presence.
- **Privacy Policy:** For detailed information on data handling, please refer to our [Privacy Policy](PRIVACY.md).

---

## 6. Disclaimer of Warranties

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NONINFRINGEMENT.

IN NO EVENT SHALL THE AUTHORS, MAINTAINERS, OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES, OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT, OR OTHERWISE, ARISING FROM, OUT OF, OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Specifically, we do not warrant that:
- The Software will meet all of your specific requirements.
- The Software will be uninterrupted, bug-free, or error-free.
- External lyrics APIs, artwork lookups, or SMTC media sessions will be permanently available, complete, or accurate.

---

## 7. Limitation of Liability

To the fullest extent permitted by applicable law, in no event shall the authors, contributors, or maintainers of TrackDot be liable for any direct, indirect, incidental, special, exemplary, or consequential damages (including loss of data, system downtime, business interruption, or hardware issues) arising out of the use or inability to use the Software.

---

## 8. Changes to These Terms

We reserve the right to revise or update these Terms of Service at any time. Any changes will be published in the official TrackDot source code repository with an updated "Last Updated" date. Continued use of the Software following the posting of revised Terms constitutes your acceptance of the changes.

---

## 9. Contact & Source Code

If you have questions about these Terms of Service or wish to contribute to the project:

- **Source Code Repository:** [https://github.com/Cara-Instan/TrackDot](https://github.com/Cara-Instan/TrackDot)
- **Maintainer / Author:** [https://github.com/herlandroando](https://github.com/herlandroando)
- **Issue Tracker:** [https://github.com/Cara-Instan/TrackDot/issues](https://github.com/Cara-Instan/TrackDot/issues)

