# OAuth creation handover for the independent Aura One APK

The native APK, native source branch, draft PR, build workflow and public registration configuration have been created. Actual OAuth registrations remain uncreated because the connected capabilities do not provide Google Cloud or GitHub developer-console registration access. Existing Lovable, Replit, Render and Git credentials do not grant that capability through the tools exposed to this session.

The complete verified public APK identity and requested scopes are in OAUTH-PUBLIC-CONFIG.json. There are no secret values in that file. No client ID, registered redirect URI or token-broker URL has been invented.

For a Google Android OAuth client, the registration values are:

- Application: Aura One
- Package: `za.co.auraone.assistant`
- Signing SHA-1: `A8:49:F7:A3:D3:C8:43:69:18:F8:8A:0A:81:DD:5A:42:BA:69:3F:7D`
- Required user-data access: Gmail read-only, Calendar read-only, Drive metadata read-only
- Keep any new backend separate from the excluded `baby-28d6e` project.

Create the client in [Google Auth Platform clients](https://console.cloud.google.com/auth/clients), with the matching Android package/signature. A broker-based authorization design also needs a web client and the real deployed HTTPS callback. No broker is deployed by this delivery, and there is no authorized callback to report. The native preview supports separately issued temporary access tokens while renewable authorization is outstanding.

For GitHub, [create an OAuth application](https://github.com/settings/applications/new) owned by the intended account. Use the real application name and homepage, enable device flow if implementing the native device authorization route, and use an actual callback only for a supported web flow. A GitHub App is another registration option with selected repository permissions. Both require provider registration and a consent flow; repository creation alone does not create an OAuth application. The current APK uses a separately issued user-authorized token with its read-only adapter.

Once registrations exist, the resulting client IDs are public configuration. Client secrets, refresh tokens and provider keys belong in protected storage and should not be posted in this conversation or committed to the public repository. Direct Gemini Live voice uses the owner's device-encrypted API key independently of these account registrations.

References: [Google credential creation](https://developers.google.com/workspace/guides/create-credentials), [Android data authorization](https://developer.android.com/identity/authorization), [GitHub OAuth registration](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/creating-an-oauth-app), [GitHub device authorization](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps#device-flow).
