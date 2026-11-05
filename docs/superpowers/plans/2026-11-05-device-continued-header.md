# Device continued header Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a later run of a split phone shows a slim "↳ name · continued" line instead of a second full header.

**Architecture:** `DeviceHeaders` (Core, pure) gains `Continuations`; the list view model sets two row flags from it; both list views (WPF, Avalonia) show the full header on the first run only and the slim line on later runs.

**Tech Stack:** .NET, WPF, Avalonia, xunit.

**Spec:** `docs/superpowers/specs/2026-11-05-device-continued-header-design.md`

## Global Constraints

- Order, drag rules and tab reordering unchanged.
- Strings in en, fr, es: `DeviceContinued` = "continued" / "suite" / "continuación"; `DeviceContinuedTip` = "This phone has other accounts higher up. Drag them together to regroup them." / "Ce téléphone a d'autres comptes plus haut. Glissez-les ensemble pour les regrouper." / "Este teléfono tiene otras cuentas más arriba. Arrástralas juntas para reagruparlas."

## Review Focus

- A phone split in three runs: runs 2 and 3 both show the slim line (covered by the test `["A","B","A","B","A"]`).
- The problems line must not repeat under a continued run.
- An offline phone split in two: the slim line still reads, with no status dot.

---

### Task 1: `DeviceHeaders.Continuations`

**Files:** Modify `src/DtHub.Core/Devices/DeviceHeaders.cs`; Test `tests/DtHub.Tests/Devices/DeviceHeadersTests.cs`

**Produces:** `public static IReadOnlyList<bool> Continuations(IReadOnlyList<string> deviceIds)`: true where a run starts on a device already seen above.

- [ ] Test `Un_appareil_coupe_en_deux_ne_repete_que_sa_suite`: `Assert.Equal([false, false, true, false, true], DeviceHeaders.Continuations(["A","B","A","A","B"]))`, and `[false,false,false]` for `["A","A","B"]`.
- [ ] Run it, expect a build failure (missing method).
- [ ] Implement as `For[i] && !FirstOccurrences[i]`.
- [ ] Run the full suite, expect green. Commit.

### Task 2: rows and views

**Files:** `src/DtHub.App/ViewModels/InstanceRowViewModel.cs`, `src/DtHub.App/ViewModels/InstanceListViewModel.cs` (`RefreshDeviceHeaders`), `src/DtHub.App/Windows/InstanceListControl.xaml`, `src/DtHub.Mac/Views/InstanceListControl.axaml`, the three `Strings*.resx`, the three changelogs.

- [ ] Row: new `[ObservableProperty] bool _isDeviceContinued`. `RefreshDeviceHeaders` sets `ShowDeviceHeader = firsts[i]` and `IsDeviceContinued = continuations[i]`.
- [ ] Both views: after the full header grid, a muted `FontTiny` line visible on `IsDeviceContinued`: `↳`, `Device.Name`, `·`, `{loc:T DeviceContinued}`, tooltip `{loc:T DeviceContinuedTip}`.
- [ ] Strings and `## [Unreleased]` changelog entries.
- [ ] Build Windows and Mac projects, run the full suite. Commit.
