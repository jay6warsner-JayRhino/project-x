# Aura One 1.4 native Android voice assistant

Light Talk, Chats and Routines screens follow the supplied design reference. Talk uses a native blue/lilac/coral animated orb, spoken input and audio replies. Chats displays actual saved conversations; there is no typed conversational input. Settings, history search and consent use native text controls.

**On-device AI is the default.** English Vosk recognition and Qwen3 0.6B language weights are included in the release APK. Local inference needs no API key, subscription, Lovable credits or cloud token balance. An installed offline English Android TTS voice supplies spoken replies. The first local answer prepares verified model files on the phone. Allow about 1 GB of storage for the APK and extracted assets. Local models have finite context, modest reasoning quality and device speed/battery constraints; this is not unlimited Gemini.

## Use

Install the separately signed release APK, allow microphone access and tap the Talk microphone. Use Settings > Offline voice setup to check speech and on-device AI, configure an offline speaking voice, or start the user-controlled background wake service. Local commands include time/date, memory, notes, reminders, a bounded timer, calculator and fixed device tools. Device changes ask for consent. Generated model text is never run as a device command.

Good morning, Bedtime, Focus and Driving are saved starter routines, paused until enabled. Good morning reads today's reminders/notes; Bedtime prepares a 06:30 alarm; Focus offers media volume 20%; Driving reads notification source names/counts. It does not read private notification bodies, set timed Do-Not-Disturb or invent weather. Custom routines provide spoken guidance. Actual conversation and completed-reminder counts replace mock statistics.

Optional Gemini Live 3.8, camera/screen analysis, internet research and read-only Gmail/Calendar/Drive/GitHub adapters remain available after explicitly turning off Use on-device AI. Those services have their own network, authorization and quota requirements. A Gemini key is not bundled. Google/GitHub OAuth provider registrations remain incomplete; Lovable account connections do not transfer to the independent APK. The existing broker is https://aura-one-oauth-jayrhino.onrender.com and requires owner registrations before Connect succeeds.

PDF, DOCX, PPTX, XLSX, OpenDocument and UTF-8 text extraction remain local. File limit 25 MB, PDF text limit 100 pages/60,000 extracted characters. The small local model receives a bounded excerpt, so long-document summaries are partial. Images, camera and scanned-document understanding need optional online AI. Native Accessibility, default-assistant, notification summaries and optional Shizuku controls retain Android permission requirements. Background services can be stopped by Samsung/Android; there is no boot auto-start or lock-screen bypass.

## Rebuild and verify

Use .NET 10.0.401, Android workload, SDK 36/build-tools 36.0.0 and JDK 21. Model preparation downloads the public Apache-2.0 weights without an account token and validates size/SHA-256. The large asset is excluded from Git/source ZIPs and included in the APK.

    python AuraOne/prepare_model.py
    dotnet run --project Checks/Checks.csproj
    node --test OAuthBroker/broker.test.mjs
    dotnet publish AuraOne/AuraOne.csproj -c Release -p:AndroidSdkDirectory="YOUR_SDK" -p:JavaSdkDirectory="YOUR_JDK_21"

Checks cover data migration, protocol behavior, local defaults and cloud gating. Native smoke checks exercise the new screens, bundled speech, real local inference with network disabled, microphone controls, PDF rendering and encrypted credentials. Use the final run/evidence for pass status; test code alone is not a passing run. CI uses an ephemeral signing key and cannot update the separately signed APK.

Public sources contain generated native/broker code and licensed speech assets. Private Lovable snapshots, pasted conversations and archives remain only in the private handover. Unreadable chat contents and files merely named in the supplied transcripts cannot be claimed included. No full equivalence to Google's proprietary Assistant/Gemini application is claimed.

Local model: https://huggingface.co/litert-community/Qwen3-0.6B (Apache-2.0).
Model bytes: 344671744. SHA-256: 03e7da1eb1108b50dffaa9bb52cc7bcbad2eb0c66ca990267f480c1e545d2856.
