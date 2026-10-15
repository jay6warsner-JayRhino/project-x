# Aura One 1.3 native Android voice assistant

Aura One combines the inspected Aura Your Pal and Aura Voice Connect projects with the supplied My Baby/Aura One references. Conversation uses native Android views and spoken input/output, with no typed chat or captions. Settings, saved data, history and consent use native text controls. The APK does not require a WebView, browser server or Termux.

Gemini 3.8 Live is the default using the owner's device-encrypted Gemini API key. Neither direct Gemini mode nor local offline mode uses Lovable AI credits. Google's API quota and billing still apply. This is an installable preview; real provider audio and Samsung A56 behavior need the owner's key and phone. Complete parity with Google's Gemini consumer application or Enterprise Live Avatar is not claimed.

## Install and use

Install the separately delivered signed APK. Open Settings, save a Gemini key on the phone, leave optional public relay disabled, check Gemini Live setup, then start voice and grant microphone permission. Never put keys into source or chat. Camera, screen sharing, Accessibility, notifications, default assistant and Shizuku each need the corresponding Android consent.

Tools > Offline voice and wake setup provides bundled local English speech recognition, fixed local commands, user-started background wake-word listening and an optional local LiteRT-LM language model. The optional Qwen3 0.6B language model is a separate verified 345 MB download/import; it is not bundled and is not offline Gemini. Local spoken replies need an installed offline English Android TTS voice. Background launch from another app requires Aura as the default assistant; otherwise use the wake notification. Android and Samsung may stop background services; there is no boot auto-start or lock-screen bypass.

## Included features

- Live audio, interruption, mute, bounded session resumption, voice session controls and extended thinking configuration.
- Camera/screen sharing and selected images; PDF text extraction and native page rendering; DOCX, PPTX, XLSX, OpenDocument and plain-text reading. Limits: 25 MB, 100 PDF text pages, 60,000 extracted characters. Scanned PDF pages can be shared individually with Gemini. Legacy binary Office formats need conversion. Macros/formulas are not executed.
- Memory, notes, reminders, routines, roles, history, JSON migration and backup.
- Weather, calculator, encyclopedia, time and Google Search grounding in primary Live mode.
- Forty fixed device operations, optional Accessibility/notification summaries, Android default-assistant service and optional Shizuku. Device changes require Allow once; generated local language-model text is never executed as commands.
- Six read-only account tools for Gmail, Calendar, Drive metadata, GitHub repositories and issues. Authorization tokens stay encrypted on the phone and are excluded from backups and Gemini context.
- Google/GitHub browser consent, PKCE/state validation, refresh and disconnect code. The deployed free Render broker is https://aura-one-oauth-jayrhino.onrender.com. Provider client registrations are still required; its health endpoint reports readiness honestly. Lovable connections do not transfer tokens to this independent app. Manual authorized-token entry remains available.
- Optional native companion face as a local visual alternative; not Google Enterprise Live Avatar.

## Broker registration

Create a Google **Web application** OAuth client for the broker flow, enable Gmail/Calendar/Drive APIs, configure consent/testing users and authorize this exact redirect:

    https://aura-one-oauth-jayrhino.onrender.com/oauth/google/callback

Create a GitHub OAuth app with homepage https://aura-one-oauth-jayrhino.onrender.com and callback:

    https://aura-one-oauth-jayrhino.onrender.com/oauth/github/callback

Set GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET, GITHUB_CLIENT_ID and GITHUB_CLIENT_SECRET only in the Render service's protected environment. Client IDs are public; client secrets are server-only. The broker returns tokens to the phone and does not persist account tokens. Native return URI is za.co.auraone.assistant:/oauth/callback. The HTTPS callbacks exist but are **not yet registered/authorized** in provider developer settings. An Android OAuth client is optional for a future native Google SDK flow, not required by this implemented browser/broker flow. Do not deploy into the excluded baby-28d6e project.

## Build and check

Use .NET 10.0.401, Android workload, SDK platform 36/build-tools 36.0.0 and JDK 21:

    dotnet workload install android
    dotnet run --project Checks/Checks.csproj
    node --test OAuthBroker/broker.test.mjs
    dotnet publish AuraOne/AuraOne.csproj -c Release -p:AndroidSdkDirectory="YOUR_SDK" -p:JavaSdkDirectory="YOUR_JDK_21"

CI runs 79 portable checks, 5 broker tests, ARM64 release compilation and native x86_64 emulator smoke checks, including encrypted credentials, PDF text/rendering and actual bundled Vosk recognition. Test presence does not imply a passing run; use the linked CI results. CI's ephemeral signing certificate cannot update the separately delivered signed APK.

The public export contains generated native/broker source, checks and licensed bundled speech assets. Original private Lovable snapshots, supplied conversation transcripts, reference archives, personal backups and signing secrets are excluded. The private source handover preserves the supplied references/transcripts. The extra source chat ending 1c3a6ff8 and files only named in transcripts remain unavailable; their contents cannot be claimed complete.
