# Aura One native Android assistant

Aura One combines the inspected Aura Your Pal and Aura Voice Connect features into a native Android app with spoken conversation. The default model is Gemini 3.8 Live. Android speech recognition plus Gemini 3.8 Flash and Android text-to-speech provide an optional fallback. Neither direct mode requires Lovable AI credits. Google API quota and billing still apply.

This is version 1.1, an installable preview. A successful provider audio conversation and Samsung A56 hardware behavior remain unverified. Do not describe this as a complete Gemini consumer-app replacement.

## Use the APK

Install the signed APK delivered separately, open Settings, enter a Gemini API key on the phone and choose Save device key. Leave Use optional server relay off. Choose Check Gemini Live connection, then Home > Start Gemini Live and grant microphone permission.

The key is encrypted with Android Keystore. It is excluded from JSON backups, source control and the distributed APK. Conversation controls are voice-only; settings and saved-data screens use native text controls.

## Build and check locally

Requirements: .NET SDK 10.0.401 or a later 10.0.4xx patch, the Android workload, Android SDK platform 36/build-tools 36.0.0 and JDK 21.

```sh
dotnet workload install android
dotnet run --project Checks/Checks.csproj
dotnet publish AuraOne/AuraOne.csproj -c Release -p:AndroidSdkDirectory="YOUR_SDK" -p:JavaSdkDirectory="YOUR_JDK_21"
```

The local Windows SDK/JDK defaults apply only when those directories exist. Other build machines can pass their own paths. Sign the resulting APK with your own Android release key. An APK signed with a different key cannot update the separately delivered preview in place.

The prepared GitHub workflow runs 34 data/protocol checks and compiles Android packages without any provider API key or Lovable credits. It has not run on GitHub: the connected integration returned HTTP 403 when creating a branch. Build artifacts would use the runner's generated signing key, not the dedicated key used for the delivered APK.

## Included

- Direct Gemini Live microphone audio, streamed spoken replies, interruption, mute and bounded session resumption.
- Native camera and screen sharing, image/plain-text attachments.
- Local memories, notes, reminders, learned routines, searchable history and JSON backup/import.
- Personality, language, Gemini voices, helper roles and enabled tool settings.
- Weather, time, arithmetic, encyclopedia lookup and Google Search grounding in primary Live mode.
- App shortcuts and dialler preparation; Android recognition wake phrases while the app is visible.

## Open items

- The user's Google/GitHub connections in Lovable are not portable OAuth credentials. An authenticated account gateway is still needed before this APK can read Gmail, Calendar, Drive or GitHub account data.
- The additional source chat ending 1c3a6ff8 could not be accessed; full coverage of its requirements cannot be claimed.
- Physical A56 audio, Bluetooth, camera, sharing, reminder delivery and Samsung battery behavior need device validation.
- Always-on background hotword detection, full phone automation, PDF/Office parsing and offline Gemini intelligence are not implemented.
- The original public relay was observed accepting a WebSocket but timing out during provider setup. It remains optional and unverified. Keep private account data out of unauthenticated relay routes.

This export contains the generated native application and portable checks only. Original Lovable project snapshots, raw chat transcripts, personal backups, provider credentials and signing material are excluded.
