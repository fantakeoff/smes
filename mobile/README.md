# SMES Mobile

Avalonia 12 / .NET 10 Android companion app. It has a host-side Core library and a smoke-test executable so the offline queue can be exercised without a device or a live SMES API.

## Server and authorization

- Sign-in calls the existing `POST /api/login`, then `GET /api/mobile/bootstrap`.
- The bootstrap controller returns only mobile pages whose exact `SysMenu` row is directly granted through the authenticated tenant user's `SysUserRole` → `SysRoleMenu` links. It does not grant pages based on `IsAdmin` or role-name guesses.
- Menu visibility is client-side navigation only. Existing API endpoints continue to enforce authentication and tenant scoping.
- The Debug URL defaults to `http://127.0.0.1:8088/api`; Release accepts HTTPS only. The Debug Android manifest is the only one that enables cleartext for local development.

## Build

Use the installed .NET 10 SDK with the Android workload, Android SDK, and Java 17. Example environment for this workstation:

```sh
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export JAVA_HOME="$HOME/.local/share/java"
export ANDROID_HOME="$HOME/Android/Sdk"
export ANDROID_SDK_ROOT="$ANDROID_HOME"

cd mobile
dotnet workload list
dotnet restore SmesMobile.slnx
dotnet build SmesMobile.Android/SmesMobile.Android.csproj -c Debug
```

The Debug APK targets the connected arm64 phone; Release ABI targets are configured separately. Debug embeds managed assemblies and stores native libraries uncompressed so the APK works without Fast Deployment.

The host/Core smoke checks use a fake HTTP handler and a temporary SQLite database; they do not need SMES credentials or create server business records:

```sh
dotnet run --project SmesMobile.Smoke/SmesMobile.Smoke.csproj -c Debug
```

## Install and launch on an Android device

Start the API on the development host at port `8088`, connect and authorize the device with ADB, then use the Debug local-loopback bridge:

```sh
adb devices -l
adb reverse tcp:8088 tcp:8088
adb install --no-streaming -r SmesMobile.Android/bin/Debug/net10.0-android/com.smes.mobile-Signed.apk
adb shell monkey -p com.smes.mobile 1
```

If the generated APK filename differs, install the APK emitted under `SmesMobile.Android/bin/Debug/net10.0-android/`. The login screen opens without logging in; no credentials are bundled. Use a test account only when verifying API integration.

## Mobile pages and offline behavior

Authorized pages map to the existing production work-order, operation-report, material-issue, quality-inspection, maintenance-task, and repair-order routes. Report, material issue, and QC submissions persist to the SQLite outbox before network I/O and reuse the same local UUID `Idempotency-Key` across retries. Maintenance execution and repair creation/handling persist the request, use stable operation keys where supported, and verify the existing GET-by-ID state before local removal.

Forms load named choices from authenticated `/api/lookup`; selecting a work order hydrates its materials and routing steps, and choosing a warehouse/material hydrates available batch/location combinations from inventory. Inspection, maintenance task, repair order, equipment, defect item, judgment, severity, and repair-state fields are selections rather than manually entered IDs. Long lists use searchable, bounded `AutoCompleteBox` popups: typing is only a search, so operators must select a matching backend row before the submit button becomes available. Fixed categories use labeled `ComboBox` controls. The role menu is a single horizontal strip with a next-page control; connection settings and offline details are collapsed, while errors and the pending/attention count remain visible. Operators still enter measurements, quantities, costs, and observations that cannot be derived from server data; report qualified quantity is computed from total minus defects.

The bearer token remains in process memory and is not written to SQLite. Commands retain their payload, stable UUID, tenant/user owner, attempt count, server job ID/status, and last error. Async rows are deleted only after their existing job endpoint reports `completed`. Explicit failures, server DLQ outcomes, and unresolved unknown results remain visible; failed/dead-lettered/unknown commands are never automatically replayed. Unknown synchronous commands expose read-only reconciliation.
