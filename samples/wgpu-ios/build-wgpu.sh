#!/bin/sh
# Builds wgpu-native for iOS and generates the symbol-export list the app
# link needs. Run once before the first build; native/ and WgpuSymbols.items
# stay out of git.
#
# Why build from source: Silk.NET 2.23's bindings and shipped desktop
# binaries come from wgpu-native commit 33133da (the Silk.NET 2.22 mobile
# update), which sits between the v0.19.4.1 and v22.1.0.5 releases. No
# release artifact matches that ABI, and the iOS artifacts only start at
# v22.1.0.5. Building the pinned commit matches by construction.
#
# Needs a Rust toolchain (https://rustup.rs).
set -eu
cd "$(dirname "$0")"

commit=33133da4ec5a0174cb21539ef2d3346f75200411
src=${WGPU_NATIVE_SRC:-"${TMPDIR:-/tmp}/wgpu-native-src"}

if [ ! -d "$src" ]; then
  git clone --quiet https://github.com/gfx-rs/wgpu-native "$src"
fi
git -C "$src" fetch --quiet origin "$commit"
git -C "$src" checkout --quiet "$commit"
git -C "$src" submodule update --init --quiet

rustup target add --quiet aarch64-apple-ios aarch64-apple-ios-sim

build() {
  rid=$1
  target=$2
  (cd "$src" && cargo build --release --target "$target")
  mkdir -p "native/$rid"
  cp "$src/target/$target/release/libwgpu_native.a" "native/$rid/"
}

build iossimulator-arm64 aarch64-apple-ios-sim
build ios-arm64 aarch64-apple-ios

# The app resolves wgpu symbols from its own executable with dlsym, and the
# iOS link only exports the symbols it is told to keep (dotnet/macios #25008).
{
  echo '<Project>'
  echo '  <ItemGroup>'
  nm -gUj native/iossimulator-arm64/libwgpu_native.a 2>/dev/null \
    | grep '^_wgpu' | sed 's/^_//' | sort -u \
    | sed 's/.*/    <ReferenceNativeSymbol Include="&" SymbolType="Function" \/>/'
  echo '  </ItemGroup>'
  echo '</Project>'
} > WgpuSymbols.items

count=$(grep -c ReferenceNativeSymbol WgpuSymbols.items)
echo "Built wgpu-native @ ${commit%????????????????????????????????}; WgpuSymbols.items lists $count symbols"
