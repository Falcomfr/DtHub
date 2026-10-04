# Modifications made to SDL

**Status as of 2026-10-04: one patch, on SDL release-3.4.12, the version
scrcpy 4.1 ships.**

## The need

In a tabbed frame, é, à, ? and every other non-letter key typed nothing,
while letters went through. scrcpy sends letters as key events and the rest
as text. SDL sends text only to the window it holds as keyboard focus
(`SDL_SendKeyboardText`), and `WIN_UpdateFocus` grants that focus only to
the foreground window. A window docked in a tab is a child of the frame and
is never the foreground window, so once it loses focus it never gets it
back.

No other path is suitable: scrcpy has no option for it, `--keyboard=uhid`
stays QWERTY on HyperOS, and keeping the game windows out of the frame
would undo the tabbed mode.

The patch also counts a window as focused when it holds the keyboard focus
and its top-level ancestor is the foreground window. A top-level window is
its own ancestor, so nothing changes for it. Upstream issue on the same
check: https://github.com/libsdl-org/SDL/issues/13777.

## Files

- `0001-focus-embedded-child-window.patch`: the patch, the only altered
  source being `src/video/windows/SDL_windowsevents.c`.
- `SDL3.dll`: the build tested on 2026-10-04, embedded in
  `DtHub.Infrastructure`. `SdlFocusFix` writes it over scrcpy's own
  `SDL3.dll`, and only over the one of scrcpy 4.1, recognised by its
  SHA-256. It then runs `scrcpy --version`, which loads SDL, and puts the
  original back if scrcpy no longer starts.
- `LICENSE.txt`: SDL's zlib licence.

## Rebuilding

Source: https://github.com/libsdl-org/SDL/archive/refs/tags/release-3.4.12.tar.gz,
SHA-256 `b68381f06a7580e63400b3b6eb547ec57d8c3ebde70f9f40e0aba530ba05da27`,
the same archive and options as scrcpy's `app/deps/sdl.sh`. Built with
llvm-mingw 20260922 (UCRT), CMake and Ninja:

```bash
tar xzf release-3.4.12.tar.gz
cd SDL-release-3.4.12
patch -p1 < 0001-focus-embedded-child-window.patch
CFLAGS=-O2 cmake -S . -B build -G Ninja -DCMAKE_SYSTEM_NAME=Windows \
  -DCMAKE_C_COMPILER=x86_64-w64-mingw32-clang \
  -DCMAKE_CXX_COMPILER=x86_64-w64-mingw32-clang++ \
  -DCMAKE_RC_COMPILER=x86_64-w64-mingw32-windres \
  -DCMAKE_BUILD_TYPE=Release -DSDL_TESTS=OFF -DBUILD_SHARED_LIBS=ON
cmake --build build
```

The build is not byte-reproducible: two clean builds differ. That is why
the tested DLL is versioned rather than rebuilt by the CI.

## When scrcpy updates

A new scrcpy carries its own `SDL3.dll`, whose digest no longer matches:
`SdlFocusFix` then leaves it alone and the fault comes back in tabs. Either
the new SDL contains an upstream fix and this folder goes, or the patch is
applied to the new SDL tag, rebuilt, tested in a tab, and
`SdlFocusFix.OriginalSha256` takes the digest of the new upstream DLL.
