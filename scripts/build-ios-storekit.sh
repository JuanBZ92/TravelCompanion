#!/usr/bin/env bash
set -euo pipefail

usage() {
    printf 'Usage: bash scripts/build-ios-storekit.sh\nBuild on macOS, then extract the resulting ZIP into artifacts/storekit on Windows.\n'
}

parse_args() {
    if [[ $# -eq 1 && $1 == --help ]]; then
        usage
        exit 0
    fi
    if [[ $# -ne 0 ]]; then
        usage >&2
        exit 2
    fi
}

main() {
    parse_args "$@"
    if [[ $(uname -s) != Darwin ]]; then
        printf 'Run this script on the paired Mac with Xcode installed.\n' >&2
        exit 1
    fi
    local tool
    for tool in xcodebuild xcrun ditto mktemp; do
        command -v "$tool" >/dev/null || { printf 'Missing tool: %s\n' "$tool" >&2; exit 1; }
    done

    local repo_root project output_dir build_dir platform archive symbols symbol
    repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
    project="$repo_root/src/TravelCompanion.Mobile/Platforms/iOS/StoreKitBridge/StoreKitBridge.xcodeproj"
    [[ -d $project ]] || { printf 'Missing Xcode project: %s\n' "$project" >&2; exit 1; }
    output_dir="$repo_root/artifacts/storekit"
    mkdir -p "$output_dir"
    # Keep build products for diagnostics; a fresh directory also makes reruns safe.
    build_dir=$(mktemp -d "$output_dir/build.XXXXXX")
    printf 'Build output: %s\n' "$build_dir" >&2
    for platform in 'iOS' 'iOS Simulator'; do
        archive=device
        [[ $platform != 'iOS Simulator' ]] || archive=simulator
        xcodebuild archive -project "$project" -scheme StoreKitBridge \
            -configuration Release -destination "generic/platform=$platform" \
            -archivePath "$build_dir/$archive.xcarchive" \
            SKIP_INSTALL=NO BUILD_LIBRARY_FOR_DISTRIBUTION=YES CODE_SIGNING_ALLOWED=NO >&2
    done
    symbols=$(xcrun nm -arch arm64 -gU "$build_dir/device.xcarchive/Products/Library/Frameworks/StoreKitBridge.framework/StoreKitBridge")
    for symbol in query_product purchase restore finish; do
        if [[ $symbols != *"_yuku_store_$symbol"* ]]; then
            printf 'Missing export: yuku_store_%s\n' "$symbol" >&2
            exit 1
        fi
    done
    xcodebuild -create-xcframework \
        -framework "$build_dir/device.xcarchive/Products/Library/Frameworks/StoreKitBridge.framework" \
        -framework "$build_dir/simulator.xcarchive/Products/Library/Frameworks/StoreKitBridge.framework" \
        -output "$build_dir/StoreKitBridge.xcframework" >&2
    ditto -c -k --keepParent "$build_dir/StoreKitBridge.xcframework" "$build_dir/StoreKitBridge.zip"
    printf '%s\n' "$build_dir/StoreKitBridge.zip"
}

main "$@"
