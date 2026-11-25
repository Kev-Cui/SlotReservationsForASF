# Slot Reservations for Adaptive Storage

A RimWorld mod that reserves stack slots in a storage building for specific items.

Example: a shelf with 8 slots can keep 2 slots for meat and 2 for vegetables. The other 4 slots follow the building's normal storage filter. Other items can't take the reserved slots, so the food you care about always has room.

Works with [Adaptive Storage Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3033901359) buildings and with vanilla `Building_Storage` (shelves and similar). ASF is optional.

## Features

- **Slot rules gizmo** on every storage building. Each rule is a thing filter plus a slot count.
- **Copy / paste** rules between buildings.
- **Inspect pane** shows reserved slots and how many stacks still have to move out.
- **Eviction.** Stacks that break the rules are hauled to other storage. If no other storage accepts a stack, or it stays stuck for about 2 in-game hours (forbidden, unreachable, no haulers), it is dropped next to the building.
- **Hauler-aware.** Items already on their way to the building count toward its slots, so several haulers can't overfill the same free slot. Supports vanilla hauling and Pick Up and Haul.
- Translations: English, Simplified Chinese, Traditional Chinese.

## How slots are assigned

1. A stack that matches a rule goes to the most specific matching rule (the one with the fewest allowed defs) that still has room.
2. All other stacks share the unreserved pool (capacity minus the sum of rule counts). Older stacks keep their place, and the newest stacks overflow first.
3. Topping up an existing stack is always allowed, because it doesn't use a new slot.

## Requirements

- RimWorld 1.6 or 1.7
- [Harmony](https://steamcommunity.com/workshop/filedetails/?id=2009463077)
- Adaptive Storage Framework (optional, load it before this mod)

## Installation

Clone or download this repository into `RimWorld/Mods/` and enable **Slot Reservations for Adaptive Storage** in the mod list. Prebuilt assemblies are included in `1.6/Assemblies` and `1.7/Assemblies`.

## Building from source

The project targets `net48` and needs the .NET SDK.

```powershell
cd Source
dotnet build -c Release -p:RWVersion=1.7   # -> 1.7/Assemblies (default)
dotnet build -c Release -p:RWVersion=1.6   # -> 1.6/Assemblies
```

- **1.7** compiles against the installed game's `Assembly-CSharp.dll` and `UnityStubs.dll`, because the 1.7 test branch runs on a .NET 10 based runtime.
- **1.6** compiles against the `Krafs.Rimworld.Ref` 1.6 reference assemblies from NuGet.
- Both versions reference ASF's 1.6 `AdaptiveStorageFramework.dll` and `Lib.Harmony` (compile-time only).

Set `RimWorldDir` and `AsfDir` in [Source/SlotReservations.csproj](Source/SlotReservations.csproj) to match your install paths.

## How it works

- `Startup` adds `CompSlotRules` to every `ThingDef` whose `thingClass` is a `Building_Storage`, and applies two Harmony postfixes:
  - `Building_Storage.Accepts`, the building-level storage check (ASF's `ThingClass` calls the base method, so this covers ASF too).
  - `StoreUtility.IsGoodStoreCell`, the per-cell check haulers and Pick Up and Haul use.

  Both postfixes can only change `true` to `false`, so they work alongside ASF's own patches.
- `SlotRuleSolver` handles capacity lookup, slot assignment and overflow detection. All direct ASF calls live in one class, so the mod still loads without ASF.
- `EvictionDropper` drops overflow stacks on the ground when haulers can't move them.
- `Dialog_SlotRules` is the rule editor window.

## Source layout

```
About/                 Mod metadata
1.6/Assemblies/        Built DLL for RimWorld 1.6
1.7/Assemblies/        Built DLL for RimWorld 1.7
Languages/             Keyed translations
Source/                C# source and project file
```
