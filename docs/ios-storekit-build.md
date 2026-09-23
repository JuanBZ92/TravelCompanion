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
