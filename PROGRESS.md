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
- `app/`: Uno Platform 7 desktop project (`net10.0-desktop`, Skia; one build for Windows exe + macOS).
  Written: App.xaml(.cs), Strings (resw from the original), Assets (original), MSACL helpers + triggers,
  HamburgerMenu (WCT 1.3 port), Model (Rom/SavedRom/HamburgerItem, Library with identical library.json format),
  StartPage.xaml(.cs) + StartPageViewModel. **Not built yet**: StartPage references EmulatorPage and View.GamePicker,
  which don't exist yet. `app/pending/` holds the ported XAML still needing code-behind (Emulator/Roms/Saves pages,
  AboutFlyout, GamePadControl).

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

## Next
1. EmulatorPanel (C# Uno control over the native lib), EmulatorPage + EmulatorPageViewModel, GamePicker (Open flow 0x1807f47e0 /
   0x1807f68f0 incl. .7z), then build and run.
2. Save/Load/thumbnail state machines (0x1807ec550, 0x1807edef0, 0x1807eb700, 0x1807eeac0), BoxSettingsFlyout key binding UI,
   GamePadControl, Roms/Saves pages, About, rate dialog.
3. Optional precision: user's old PC `C:\Program Files\WindowsApps\Microsoft.NET.Native.Framework.1.3_*_x64__8wekyb3d8bbwe\SharedLibrary.dll`
   (+ Microsoft symbol server PDB) would name every framework call.
4. Compare against screenshots from the old PC; publish win-x64 exe and macOS app.
