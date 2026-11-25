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

The mod is the `SlotReservationsForASF/` folder inside this repository. Copy that folder into `RimWorld/Mods/` and enable **Slot Reservations for Adaptive Storage** in the mod list. Prebuilt assemblies are included in its `1.6/Assemblies` and `1.7/Assemblies`.

For development, keep the repository anywhere and link the mod folder into the game instead of copying it:

```powershell
New-Item -ItemType Junction -Path "<RimWorld>\Mods\SlotReservationsForASF" -Target "<repo>\SlotReservationsForASF"
```

## Building from source

The project targets `net48` and needs the .NET SDK.

```powershell
cd SlotReservationsForASF\Source
dotnet build -c Release -p:RWVersion=1.7   # -> SlotReservationsForASF/1.7/Assemblies (default)
dotnet build -c Release -p:RWVersion=1.6   # -> SlotReservationsForASF/1.6/Assemblies
```

- **1.7** compiles against the installed game's `Assembly-CSharp.dll` and `UnityStubs.dll`, because the 1.7 test branch runs on a .NET 10 based runtime.
- **1.6** compiles against the `Krafs.Rimworld.Ref` 1.6 reference assemblies from NuGet.
- Both versions reference ASF's 1.6 `AdaptiveStorageFramework.dll` and `Lib.Harmony` (compile-time only).

Set `RimWorldDir` and `AsfDir` in [SlotReservations.csproj](SlotReservationsForASF/Source/SlotReservations.csproj) to match your install paths.

## Publishing to the Steam Workshop

RimWorld uploads the whole mod folder with nothing excluded. Only `SlotReservationsForASF/` is uploaded (including its `Source/`). The README, `.git` and `Resources/` stay outside it. `Resources/` holds screenshots for the Workshop page, which you add on Steam by hand. Before uploading:

```powershell
.\SlotReservationsForASF\Source\prepare-upload.ps1
```

This rebuilds both DLLs, deletes `Source/obj` and `Source/bin`, checks `About.xml` and `Preview.png`, and lists the files that will be uploaded. Then enable Dev Mode, open the mod list, select this mod and click **Upload to Steam Workshop**. The game has to load the mod from `SlotReservationsForASF/` (for example through the junction above) for this. After the first upload the game writes `About/PublishedFileId.txt`. Commit that file so later uploads update the same Workshop item.

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
SlotReservationsForASF/   The mod itself (this folder is what gets uploaded)
  About/                 Metadata, preview image, mod icon
  1.6/Assemblies/        Built DLL for RimWorld 1.6
  1.7/Assemblies/        Built DLL for RimWorld 1.7
  Languages/             Keyed translations
  Source/                C# source, project file, upload helper script
Resources/               Screenshots for the Workshop page (not uploaded)
README.md
```
