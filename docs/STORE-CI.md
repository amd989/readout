# Releases from GitHub Actions

`.github/workflows/release.yml` runs when a tag like `v1.2.0` is pushed:

1. runs the tests (`ci.yml`);
2. builds every store package with the version from the tag, using the same scripts as a
   local release: `publish-windows.ps1` (x64 + arm64 bundle), `publish-android.sh` (.aab and
   .apk), `publish-ios.sh` (.ipa) and `publish-mac.sh` (.pkg);
3. publishes a GitHub release with notes built from the conventional commit messages since the
   last release, and the .apk attached for sideloading;
4. sends the packages to the stores, each job waiting for approval in its environment.

Store listing text and screenshots stay in each store's console.

## Releasing

```bash
git tag v1.2.0
git push origin v1.2.0
```

Then approve the store jobs in the run (Actions > Release > Review deployments).

| Store | What the workflow does | Default | With `STORE_AUTO_SUBMIT` |
|---|---|---|---|
| Microsoft Store | Uploads the bundle to Partner Center | Draft submission, sent from Partner Center | Submitted for certification |
| Google Play | Uploads the .aab to a track (`PLAY_TRACK`, default `internal`) | Draft release, rolled out from the Play Console | Rolled out (`completed`) |
| App Store | Uploads the .ipa and .pkg to App Store Connect | In TestFlight; submit for review in App Store Connect | Same (review submission stays manual) |
| GitHub | Creates the release with notes and the .apk | Published | Published |

`STORE_AUTO_SUBMIT` and `PLAY_TRACK` are repository variables (Settings > Secrets and
variables > Actions > Variables). Leave `STORE_AUTO_SUBMIT` unset until a few drafts have gone
through cleanly.

**Prereleases** (`v1.2.0-beta.1`): a GitHub prerelease, Google Play internal testing and
TestFlight, never auto submitted. The Microsoft Store is skipped: it would take the final
version's number.

**Re-running:** Actions > Release > Run workflow takes an existing tag and which stores to
send to. A failed job can also be re-run on its own from the run page. Every store refuses a
build number it has already seen, so a store that took the upload needs a new tag.

## Version numbers

The tag is the version; `ApplicationDisplayVersion` in `Readout.csproj` is only used by local
builds.

| | v1.2.3 gives | Must grow with each upload |
|---|---|---|
| Windows | 1.2.3.0 | the whole version |
| Android | versionName 1.2.3, versionCode from the UTC date and time (yyDDDHHMM) | versionCode |
| iOS and Mac | 1.2.3, build from the date and time (yymmdd.HHMM) | build, per version |

The Microsoft Store already has 1.0.yyDDD.0 builds from `publish-windows.ps1`'s date scheme,
so the first tag must be **v1.1.0** or later; v1.0.x would be a lower version. The patch number
can't go above 65535.

## One-time setup

Each store has a GitHub environment (Settings > Environments). Add yourself as a required
reviewer on each so nothing reaches a store without a click. Secrets only used by the upload
can live in the environment; the signing secrets are used by the build jobs, so they must be
repository secrets.

### Microsoft Store (environment `microsoft-store`)

1. **Entra app.** Partner Center > Account settings > User management > Microsoft Entra
   applications > Add, with the **Manager** role. Create a client secret for it (note its
   expiry: the upload fails once it lapses).
2. **Secrets:**
   - `STORE_TENANT_ID`: the Entra tenant ID
   - `STORE_CLIENT_ID`: the app's client ID
   - `STORE_CLIENT_SECRET`: the secret from step 1
   - `STORE_SELLER_ID`: Partner Center > Account settings > Legal info > Seller ID

The package identity in `Readout/Platforms/Windows/Package.appxmanifest` must already match
Partner Center; the Store signs the packages, so there's no certificate to set up.

### Google Play (environment `google-play`)

1. **Upload key.** If you don't have one yet:
   ```bash
   keytool -genkeypair -v -keystore readout-upload.keystore -alias upload -keyalg RSA -keysize 4096 -validity 10000
   ```
   Keep it out of the repository and backed up. With Play App Signing (the default for new
   apps) Google keeps the app signing key, and a lost upload key can be reset.
2. **First upload by hand.** Google Play only accepts API uploads once the app exists and has
   had one .aab uploaded in the Play Console. Build it with `publish-android.sh` (same
   environment variables as below) and upload it to internal testing.
3. **Service account.** Google Cloud console > IAM > Service accounts > Create, then Keys > Add
   key > JSON. In the Play Console, Users and permissions > Invite new users > the service
   account's email, with access to Readout and the release permissions ("Release to production,
   exclude devices, and use Play App Signing" and "Release apps to testing tracks").
4. **Repository secrets** (used by the build):
   - `ANDROID_KEYSTORE_BASE64`: `base64 -i readout-upload.keystore` (on Windows:
     `[Convert]::ToBase64String([IO.File]::ReadAllBytes('readout-upload.keystore'))`)
   - `ANDROID_KEY_ALIAS`: `upload` above
   - `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_PASSWORD` (the same unless you set them apart)
5. **Environment secret:** `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`, the JSON key file's contents.

While the app is still a draft in the Play Console (never published), Google only accepts draft
releases, so leave `STORE_AUTO_SUBMIT` off until then.

The .apk on the GitHub release is signed with the upload key, not Google's app signing key, so
it can't update a copy installed from Google Play (or the other way round).

### App Store and Mac App Store (environment `app-store`)

1. **Certificates.** In Keychain Access on the Mac you release from, select both the
   "Apple Distribution" and the "3rd Party Mac Developer Installer" certificates (with their
   private keys), File > Export Items, as one .p12 with a password.
2. **Provisioning profiles.** developer.apple.com > Profiles: download the App Store profile
   for md.alejandro.readout (iOS) and the Mac App Store profile (Mac Catalyst). The workflow
   reads each profile's name from the file, so they can be called anything. They expire after
   a year: download them again and update the secrets.
3. **API key.** App Store Connect > Users and Access > Integrations > App Store Connect API >
   Team Keys > generate one with the **App Manager** role. Download the .p8 (only possible
   once) and note the key ID and issuer ID.
4. **Repository secrets** (used by the build):
   - `APPLE_CERTIFICATES_P12`: `base64 -i certificates.p12`
   - `APPLE_CERTIFICATES_PASSWORD`: the .p12 password
   - `IOS_PROVISIONING_PROFILE`: `base64 -i "Readout App Store.mobileprovision"`
   - `MAC_PROVISIONING_PROFILE`: `base64 -i "Readout Mac App Store.provisionprofile"`
5. **Environment secrets:**
   - `APP_STORE_CONNECT_KEY_ID`, `APP_STORE_CONNECT_ISSUER_ID`
   - `APP_STORE_CONNECT_KEY`: the .p8 file's contents

The Apple builds run on the `macos-26` runner with its newest stable Xcode, which .NET 10's iOS
SDK needs.

### GitHub release

Nothing to set up: the job uses the workflow's own token. Release notes group commits by type:
`feat` under New, `fix` and `perf` under Fixes, `refactor` and `revert` under Other changes;
`docs`, `chore`, `build`, `ci`, `test` and `style` are left out. A scope becomes a bold prefix
(`fix(android): ...` is "**android:** ...").
