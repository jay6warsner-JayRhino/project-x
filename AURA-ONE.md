# Aura One native Android voice assistant

Aura One combines the inspected Aura Your Pal and Aura Voice Connect projects with the supplied My Baby/Aura One sources in a native Android application. Spoken conversation defaults to Gemini 3.8 Live using the owner's device-stored Gemini key. Android speech recognition, Gemini 3.8 Flash and Android text-to-speech provide a fallback. Neither direct mode uses Lovable AI credits. Google API quota and billing still apply.

Version 1.2 is an installable preview. Real provider audio and Samsung A56 hardware behavior still need validation with the owner's key and phone. Complete equivalence to the Gemini consumer application is not claimed.

## Install

Install the separately delivered signed APK, open Settings, save a Gemini API key on the phone and leave optional server relay disabled. Check Gemini Live setup, then start voice and grant microphone permission. The conversational screen has no typed chat or captions; settings, history and saved data have native text controls.

Keys and account tokens are encrypted with Android Keystore and excluded from backups and this repository. Optional account adapters support read-only Gmail, Calendar, Drive metadata, GitHub repositories and issues after authorization in this APK. Lovable's connected accounts do not transfer credentials. Automatic renewable Google/GitHub sign-in still needs a registered OAuth client and authorized callback; the preview supports explicit temporary access-token entry.

## Included

- Live microphone/audio playback, interruption, mute, bounded session resumption and voice session controls.
- Native camera and screen sharing, selected image/plain-text attachments.
- Local memory, notes, reminders, routines, roles, history and JSON migration/backup.
- Weather, calculator, encyclopedia lookup, time and Google Search grounding in primary Live mode.
- Forty fixed device operations, user-enabled Accessibility, notification source summaries, default-assistant service and optional Shizuku UserService integration. Device changes require Allow once.
- Six account-reading tools with fixed HTTPS endpoints, bounded responses and safe authorization errors.

The supplied WebView/Termux/server implementations were used as references. This APK uses native Android views and does not run a local Termux web server. Enterprise avatar integration, always-on hotword detection, arbitrary shell execution, PDF/Office parsing and offline Gemini inference are not implemented. The extra source chat ending 1c3a6ff8 remained inaccessible. Files only named in transcripts cannot be reconstructed without their contents.

## Build and check

Use .NET SDK 10.0.401 (or compatible later 10.0.4xx patch), Android workload, Android SDK platform 36/build tools 36.0.0, and JDK 21.

    dotnet workload install android
    dotnet run --project Checks/Checks.csproj
    dotnet publish AuraOne/AuraOne.csproj -c Release -p:AndroidSdkDirectory="YOUR_SDK" -p:JavaSdkDirectory="YOUR_JDK_21"

The Windows SDK/JDK defaults apply only when those paths exist. Sign builds with your own release key. CI uses an ephemeral signing key and cannot update the separately delivered APK in place.

GitHub Actions runs 54 data/protocol/account checks, builds ARM64 Android and runs an x86_64 Android emulator smoke test. The emulator test uses only a dummy credential, verifies encrypted credential persistence/removal and native bridge/service registration, and refuses physical phones. Provider audio is not tested without a real key.

This public export contains generated native source and checks only. Original Lovable snapshots, supplied private chat transcripts/reference archives, personal backups, API keys and signing private keys are excluded.
