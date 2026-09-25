# iOS StoreKit bridge and remote archives

The four `yuku_store_*` symbols are exported by `StoreKitBridge.swift`.
The installed .NET iOS SDK runs its `XcodeProject` targets only on macOS;
Windows Pair to Mac builds must supply a precompiled native reference.

## Publish directly on the Mac

Use the current checkout on the Mac with the matching .NET iOS workload,
Xcode, distribution certificate and provisioning profile installed:

```sh
dotnet publish src/TravelCompanion.Mobile/TravelCompanion.Mobile.csproj -f net10.0-ios -c Release -p:RuntimeIdentifier=ios-arm64 -p:ArchiveOnBuild=true
```

The project builds the bridge automatically in this mode.

## Publish from Windows using Pair to Mac

### Framework load path (ITMS-90863)

Both Xcode configurations explicitly use
`@rpath/StoreKitBridge.framework/StoreKitBridge` as the framework install name.
An absolute `/Library/Frameworks/StoreKitBridge.framework/StoreKitBridge` load
path makes the app depend on a framework outside its bundle. Changing the
project does not repair an already compiled XCFramework or uploaded IPA.

After this setting changes, rebuild the bridge using the script below and
replace the complete precompiled XCFramework on Windows. Clean and rebuild the
iOS application, then upload with an unused `ApplicationVersion` greater than
the last uploaded build number (the reported delivery was build 63).

On the Mac, check the resulting archive before uploading (replace the path):

```sh
APP="/path/to/App.xcarchive/Products/Applications/TravelCompanion.Mobile.app"
xcrun otool -D "$APP/Frameworks/StoreKitBridge.framework/StoreKitBridge"
EXECUTABLE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$APP/Info.plist")
xcrun otool -L "$APP/$EXECUTABLE"
codesign --verify --deep --strict "$APP"
```

The embedded framework must exist. Its install name and the application's
StoreKitBridge dependency must both be
`@rpath/StoreKitBridge.framework/StoreKitBridge`, never `/Library/Frameworks/...`.
Inspect `otool -l "$APP/$EXECUTABLE"` to confirm that an `LC_RPATH` resolves to
the app's Frameworks directory. Test startup and purchases on a device and,
if distributing the iOS app on Apple silicon Macs, on a Mac as well.
These binary checks require Xcode/macOS; source inspection alone does not
validate the uploaded app.

On the Mac, use a copy of this repository containing the current Swift source
and run from its root:

```sh
bash scripts/build-ios-storekit.sh
```

The script builds device and simulator slices, checks all four device exports,
and prints the path to `StoreKitBridge.zip`. Copy that ZIP to Windows and extract
it into the repository's `artifacts/storekit` directory. The resulting layout must be
`artifacts/storekit/StoreKitBridge.xcframework/Info.plist` (without an extra ZIP folder).
Reload the project in Visual Studio and publish again with Pair to Mac connected.
The project detects this location automatically; no publish-profile property is needed.
Rebuild and replace the framework after Swift changes. Keep the entire framework,
not just the arm64 binary. Generated archives remain under `artifacts/storekit/build.*`
on the Mac for troubleshooting.

### Manual equivalent / custom location

On the Mac, from the repository root, build the framework for device and simulator:

```sh
xcodebuild archive -project src/TravelCompanion.Mobile/Platforms/iOS/StoreKitBridge/StoreKitBridge.xcodeproj -scheme StoreKitBridge -configuration Release -destination 'generic/platform=iOS' -archivePath artifacts/storekit/device.xcarchive SKIP_INSTALL=NO BUILD_LIBRARY_FOR_DISTRIBUTION=YES CODE_SIGNING_ALLOWED=NO
xcodebuild archive -project src/TravelCompanion.Mobile/Platforms/iOS/StoreKitBridge/StoreKitBridge.xcodeproj -scheme StoreKitBridge -configuration Release -destination 'generic/platform=iOS Simulator' -archivePath artifacts/storekit/simulator.xcarchive SKIP_INSTALL=NO BUILD_LIBRARY_FOR_DISTRIBUTION=YES CODE_SIGNING_ALLOWED=NO
xcodebuild -create-xcframework -framework artifacts/storekit/device.xcarchive/Products/Library/Frameworks/StoreKitBridge.framework -framework artifacts/storekit/simulator.xcarchive/Products/Library/Frameworks/StoreKitBridge.framework -output artifacts/storekit/StoreKitBridge.xcframework
nm -gU artifacts/storekit/device.xcarchive/Products/Library/Frameworks/StoreKitBridge.framework/StoreKitBridge
```

Confirm that `nm` lists all four `yuku_store_*` exports. Copy the complete
`StoreKitBridge.xcframework` directory to the Windows checkout, under
`artifacts/storekit/`. Set `StoreKitBridgeFramework` in the local publish profile
to that directory's absolute Windows path, or pass
`-p:StoreKitBridgeFramework=C:/path/to/StoreKitBridge.xcframework` to MSBuild.
The SDK handles it as a `NativeReference` for the paired Mac build.

Rebuild the framework whenever its Swift source changes. Use a fresh output
directory if `xcodebuild -create-xcframework` reports that it already exists.
Do not suppress unresolved native symbols: doing so would leave purchases broken.

### Build helper requirements

| ID | Requirement | Implementation |
| --- | --- | --- |
| REQ-001 | Produce the native bridge for remote Windows publishing | Device and simulator archives, then XCFramework and ZIP |
| REQ-002 | Fail before packaging a missing purchase export | Check all four arm64 symbols with `nm` |
| REQ-003 | Allow reruns without deleting previous diagnostics | Unique build directory; fail-fast Bash script |

The helper targets macOS Bash with Xcode installed. It uses a custom minimal
script because it has no configurable inputs. Generation reference:
[Ref: docs/generation-best-practices.md -> Core Principles] in the
`bash-script-generator` skill (strict mode, quoted paths, prerequisite checks).
