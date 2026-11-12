# Universal Emulator rebuild: status

Goal: rebuild the Store app "Universal Emulator" 1.1.3.0 (NESBox, `MBox.App`) exactly, as a plain Windows exe and an Apple-silicon Mac app.
Source of truth: the user's installed package dump `~/Downloads/50446Nesbox.16735B73F29AD_8y3dwps6jawp4/TempState/DUMP`
(plus their LocalState library/saves, usable for testing).

## Done
- `original/`: every resource decoded from `resources.pri` (`strings.tsv`), every XBF screen decompiled to XAML (`original/xaml`).
  `original/port_xaml.py` applies the mechanical clean-up used for the app copies.
- `native/`: libnesbox core (same engine the original's NesboxPanels.dll embedded) + 7z + sound (miniaudio) + pads
  behind a C API (`mboxpanels.cpp`). `make` -> `libmboxpanels.dylib` (arm64), `make TARGET=windows ZIG=...` -> `mboxpanels.dll`.
  `make test` passes on both (Windows under wine). Core fixes: conffile.h const comparators, FCEUFILE::mode init.
- `app/` contains the Uno desktop app, including the library, emulator, settings, and virtual gamepad pages.
  The Windows installer publishes a self-contained x64 executable and its original emulator dependencies.

## Original binaries on Windows
- `original/bin/`: NesboxPanels.dll, GameController.dll + .winmd, extracted unchanged from the Store package.
- `app/MBoxPanels.Original` (net10.0-windows, UseUwp, CsWinRT projection of those .winmd) hosts them in a plain desktop
  process: activation via DllGetActivationFactory (`ActivationFactory.ActivationHandler`), the original EmulatorPanel
  (a SwapChainPanel) in a XAML Island inside a host HWND that Uno embeds with `Win32NativeWindow`. Needs the exe
  manifest's `maxversiontested` and the UWP C++ runtime (`*_app.dll`, copied from the installed Microsoft.VCLibs.140.00).
- Built into `Original/` next to the exe and loaded at runtime in its own AssemblyLoadContext (`MBoxPanels/Originals.cs`).
  EmulatorPanel and SevenZipWrapper use the original DLL when available. macOS and `UE_REBUILT_CORE=1` use the rebuilt core.
  The Windows build copies the original GameController.dll with its XInput import changed to desktop XInput 1.4.
  The untouched controller DLL ran games but crashed in Windows.Gaming.Input.dll on shutdown in the unpackaged app.
  Explicit WinRT cleanup on the main thread and around controller polling did not prevent that crash.
- Not reusable: Nesbox.dll (.NET Native machine code, no IL), clrcompression.dll (.NET Native runtime piece);
  Microsoft.Graphics.Canvas.dll (Win2D) loads but was only used by Nesbox.dll's code.

## Decoded behaviour (Nesbox.dll addresses; disassembly in scratchpad, regenerate with `xcrun llvm-objdump -d`)
- Hamburger items (0x180861d80): Open U+E109 / My Games U+E7FC (selected) / My Saves U+E159 / Settings U+E115.
  My Games = AllRoms.Where(name filter).OrderBy(Name); My Saves = Saves->Rom->SavedRom, filtered, OrderBy(Name).
  Filter is a framework (str,str,StringComparison 1)->bool call: StartsWith assumed, unconfirmed.
- Item template: IsSelect -> Grid.Background (HamburgerBackgroundConverter), FontIcon/TextBlock Foreground (HamburgerForegroundConverter).
- Names: file name up to first '.'. library.json via DataContractJsonSerializer; .bak kept; default from Assets/Roms/library.json.
- EmulatorPage.OnKeyDown (0x18088ce90): Alt+Enter toggles full screen. Bar hidden: F2 save, F3 load,
  Back/Esc/GoBack/GamepadMenu/View -> show bar; other keys -> VM.ProcessKey(panel, key, true) (dictionary key->ControlKeys,
  panel.SetControl each). Bar shown: Back/GoBack/GamepadB -> go back; Esc/Menu/View -> hide bar; else base.
  KeyUp (CoreWindow, 0x18088cb70) -> ProcessKey(..., false). OnKeyUp: GamepadView toggles settings pane, GamepadX focuses.
- Show bar (0x18088d160): PageAppBar Visible, focus, ProcessGameController=false. Hide (0x18088d110): Collapsed, =true.
- OnNavigatedTo (0x18088de80): param Rom; panel.StateChanged(Error) -> "FailedToLoadRomError" dialog; gamepad overlay
  events -> panel.SetControls(int) / IsSetupGamepadMode=(status==Editing); BackRequested: bar hidden -> show bar, else go back;
  back button visible; full screen per setting (icons E1D8 windowed / E1D9 full); Sound icon E15D on / E198 off.
- Settings keys: IsSoundEnabled (default true), IsBlurEnabled, IsFullScreenEnabled, IsVirtualGamePadEnabled,
  ControllerSensivity, LaunchCount, IsShowRateNeed, KeyBindings, padA/padB/padC/padX/padY/padStart.
  Xbox detection hides keyboard settings + full screen. VideoModeName "PAL"/"NTCS" (sic).
- Panel API (MBoxPanels.winmd): LoadRom(name, buf[, state]), UnloadRom, SaveSnapshotToStream(PNG), SetControl, SetControls,
  SaveState, LoadState, AddController, StateChanged; props IsSoundEnabled/IsBlurEnabled/IsEmulationPaused/VideoMode/LoadedEmulator.
  Original renders with D3D11/D2D, XAudio2, WIC.

## Windows verification

A Debug publish accepts `UE_SMOKE_LOG` with an absolute output path. It opens the built-in NES and SNES games and
Top Gear if present in the library. It checks ROM loading, save-state and screenshot creation, command-bar input modes,
and navigation back to the library. It loads the generated save state in memory. Run with `UE_REBUILT_CORE=1` to check
the fallback renderer. The smoke test uses panel input methods. Physical controller input, sound output, and a visual
comparison with the original UWP still need interactive checks.

## Original UWP launch on Windows

The 1.1.3.0 x64 app installed, but its startup failed before the window opened. A package debugger
captured `Nesbox.exe` exiting with `0xC0000135` (DLL not found). The loader trace showed
`mrt100_app.dll` loading, with no load of `SharedLibrary.dll`, even though Windows had registered
the required .NET Native Framework 1.3 package.

The framework copy of `SharedLibrary.dll` was in
`C:\Program Files\WindowsApps\Microsoft.NET.Native.Framework.1.3_1.3.24211.0_x64__8wekyb3d8bbwe`.
Its permissions lacked the AppContainer read and execute grants present on the runtime DLL that
loaded successfully. This suggests an access issue. The trace captured the loader failure without
an explicit access-denied event.

Copying that Microsoft DLL beside `Nesbox.exe` let the app launch. The working copy uses the original
app binaries and display name, `Universal Emulator`. It is registered from
`%LOCALAPPDATA%\UniversalEmulator-UWP-Repaired` under the local package identity
`Nesbox.UniversalEmulator.Repair`, which has its own app data folder.

The loose extraction left URI escapes in the filenames of six bundled SNES ROMs and their
thumbnails. Their filenames now match the paths in the original library, and all nine bundled
ROMs match the original library's MD5 checksums. The imported Top Gear dump has a valid SNES
header. Its file and library path now use `.smc` so the original engine selects the SNES core.
