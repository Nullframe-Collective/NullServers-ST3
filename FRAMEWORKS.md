# Optional addons

NullServers 0.0.25 runs on its own. CustomWeaponsFramework and CustomPlayerCustomization are independent optional MelonLoader mods. Integration binds to API version 1; addon release numbers are informational and do not determine compatibility. Addons exposing `Enabled` use their native content controls. Existing 1.1.0 addons without that property use NullServers compatibility hooks for content registration and inventory access, so their installed DLLs remain manageable.

Install only `Mods/NullServers.dll` from the base package to play and host without addon content. Install either framework separately to enable its content on your own rooms. CustomWeaponsFramework's package includes the Phys Gun and Tool Gun definitions, bundles, and icons. CustomPlayerCustomization loads the user's cosmetic packs.

The main menu contains an Addons button between Options and Credits. Its management panel lists installed official addons with their versions and individual Enabled/Disabled controls. Missing official addons appear under Get More Addons with Download, which opens https://discord.gg/CjWXxyS2U4. There is no Update button or installed-release comparison. Install downloaded addons manually and restart the game.

The selection is saved in `UserData/NullFrame/addons.json` and applies to the next game. Disabled addons are omitted from hosted room requirements and content fingerprints. A server requiring a disabled installed addon cannot be joined until it is enabled in Addons. Selection changes are refused while in a room. These switches control content use; they do not unload MelonLoader assemblies from the current process.

## Joining rooms

The host publishes enabled official addon IDs, informational release labels, the content fingerprint and a compressed pack catalog in relay room metadata. Required Addons lists missing or different packs required by the host, with display names, stable IDs and relative bundle paths. Extra local packs are automatically disabled for the room and do not produce a warning or Download button. Download opens the official Discord for both DLLs and packs. No installed-release comparison is made.

Installed DLL presence, enable state, content initialization and pack matching are checked separately. A broken pack never turns an installed framework into a missing DLL or disables the other framework's controls. The client selects the host's content IDs before native registration and uses the same order as the host. Original registry objects and loader error/readiness fields are preserved. Readiness in the room validates only the selected packs and their required prefabs (including the legacy Animation component for weapons). A broken unused local pack cannot block the room; a broken required pack still produces an explicit initialization error. No pack-name exceptions or file deletion are used. The original catalogs and local hat choice are restored on exit, failed joins, return to the main menu or application quit. A changed room hat is mapped back by stable content identity. The main-menu addon enable settings are retained.

The content fingerprint covers API versions and ordered stable IDs with SHA-256 hashes of bundle bytes and JSON definitions. Icons, display metadata, local folder names and addon release labels do not affect this fingerprint. Matching remains strict because native item indices depend on the registry order.

The relay room data limit is 1024 bytes. If the full compressed catalog cannot fit alongside room settings, only the fingerprint is advertised; the full catalog is checked in the existing reliable handshake before avatar spawning. Until that catalog is received, addon registration is held disabled. The selected weapon content is then loaded for the active scene and existing cosmetic components receive the selected catalog before the spawn barrier opens. A missing or different required pack opens the detailed Required Addons panel and disconnects. Catalog decompression and reliable messages are bounded. Small catalogs, including the bundled Phys Gun and Tool Gun, are checked immediately on Join. Old hosts can still provide stable IDs through their handshake, but both players should update for names and paths.

Weapon bundles and definitions live in `UserData/CustomWeapons`; cosmetic bundles and definitions live in `UserData/CustomCustomization`. Copying only the mod DLLs does not install these packs. `research/tools/package-content.ps1 -GamePath <game-folder>` creates a separate snapshot of the host's content with file hashes, without mod DLLs or player settings.

A client with both addons can join a room using neither or only one of them. Framework content that the host does not use is disabled for that room and excluded from the compatibility manifest. Only the host's weapon and cosmetic packs must match. The actual runtime lists are temporarily replaced with the host's ordered subset so native game indices remain consistent. Existing API 1 framework DLLs, including 1.1.0, support this selection through their managed registry fields and temporary readiness hooks; the underlying files, bundles and original error fields are retained.

The room-scoped reliable handshake remains in place before avatar spawning. It checks the current room-object/revision identifiers and the advertised fingerprint again. Scene changes start a new exchange. The host still checks the exact selected registry rather than accepting a client's unfiltered extra content. The round readiness barrier remains independent. This verifies compatibility, not client integrity.

## Development

The three source projects are listed in `NullServers.slnx`. NullServers has no compile-time assembly or project references to either framework. Its optional API adapters bind only when a compatible addon is already loaded. Missing or unsupported addons do not stop the base mod from initializing.

```powershell
dotnet build NullServers.slnx -c Release -p:SkipModInstall=true -m:1
dotnet run --project tests/NullServers.Checks.csproj -c Release
```

Build `NullServers.csproj` alone to build only the base mod. `GamePath` can be overridden with `-p:GamePath=...`. Use `SkipModInstall=true` to keep installed game files unchanged. The base project's installation target copies only NullServers.dll.

`research/tools/package.ps1` produces separate ZIPs for NullServers 0.0.25, CustomWeaponsFramework 1.2.0, and CustomPlayerCustomization 1.2.0. Each archive contains only its own mod and assets.

The public framework APIs are WeaponCatalog, WeaponInventory, and CustomizationCatalog. Catalogs expose stable IDs, readiness, content hashes, and registration events. Catalog `Enabled` controls in-game content registration; it defaults to true for standalone use. NullServers sets it from the room's requirements before loading the room scene. For older API 1 catalogs without `Enabled`, declared content callbacks are gated instead: weapon scene registration, player setup, shop and sandbox options, inventory operations, and in-game cosmetic registration. Menu cosmetic previews remain available; a saved custom hat falls back to the default hat in rooms without customization content.

Definitions accept explicit IDs such as `author:exampleweapon`. Existing packs without IDs receive `legacy:<lowercase folder name>`. IDs use lowercase ASCII letters, numbers, `:`, `.`, `-`, and `_`. Catalogs register in ordinal ID order and reject duplicate IDs and display names.

## Validation

Pure checks cover addon-free rooms, independent addons, presence-based matching across different release labels, saved enable states, metadata parsing and limits, client registry projection, content mismatches, malformed handshake messages, and the existing protocol checks. They do not execute a two-client Unity/FishNet connection. Verify first join, respawn, same-map restart, and rejected joins with two clients before release.

`dotnet run --project research/tools/AddonChecks/AddonChecks.csproj -c Release` verifies real Harmony gating of legacy registration and inventory methods and runs the production addon discovery, manifest and pre-join code against controlled loader fixtures. Regressions cover all four host addon combinations, late readiness, the reported SuperPumpkin failure, preservation of loader errors and registries, automatic removal of unused items from actual registration lists, empty host catalogs with broken local cosmetics, required-pack prefab validation, weapon load completion, hat-index remapping and restoration, atomic rollback, large/invalid unused collections, missing weapon files, and distinct missing/disabled/initialization/content results. Separate smoke checks also execute selection against the installed original 1.1.0 DLLs without loading a Unity scene.

Authored weapon bundles, meshes, animations, and poses are preserved.


## Inline game mode settings (0.0.26)

CustomGameModesFramework 1.1.1 can place every mode's declared options directly in Game Settings. NullServers allocates these rows after the native mode options and before Mute Guests and uses one scrollbar for native, mode and room options. Update both DLLs from the combined release archive. This UI integration does not yet add CustomGameModesFramework to the official addon catalog.

Other UI extensions can use `NullServers.UI.GameSettingsExtensions.Register(id, measure, draw)` to reserve and draw their own section. Registration returns an IDisposable handle. Drawing coordinates belong to the shared scroll content; extensions should not open a second scroll view.

## Custom NPCs (0.0.27)

CustomNPCFramework 1.1.0 is an optional official addon. Its NPCCatalog API 1 exposes Enabled, IsReady, Items and UseContent(string[] ids). NullServers discovers the DLL without a compile-time reference and lists it in Addons. Missing NPC packs and different npc.json/bundle hashes appear in Required Addons with paths under UserData/CustomNPCs. Extra local NPCs are temporarily excluded from the room and restored on exit. NPC addon release labels do not determine compatibility.

NPC manifest fields are omitted when the room does not use NPC content, preserving the previous addon-free fingerprint. The existing reliable handshake and room metadata include the selected NPC catalog. Both peers need NullServers 0.0.27 and CustomNPCFramework 1.1.0 for custom NPC rooms.

The native FishNet spawner and spawn payload carry NPC identity and pack hash. CustomNPCFramework preserves the native AI, ownership, observer and spawn limits. It uses a native callback with the Windows x64 Nullable<int> ABI for payload reading; a Harmony patch of the generated boxed Nullable wrapper caused a reproducible CLR access violation.

Validation: 80 addon integration checks include NPC discovery, requirements, metadata, pack mismatch, selection/restoration and disabled-addon cases. Runtime host spawn and both native payload reader forms were exercised in Unity. A connection between two separate computers remains to be verified. See the NPC release validation notes for coverage and limitations.
## Custom mode browser labels (0.0.28)

CustomGameModesFramework 1.1.2 advertises CustomMode.Name from the mode descriptor. The native browser mode column displays this name using the existing font size and row style, independently of locally installed modes. RoomProperties.GameMode remains the native base rules. Older metadata falls back to CustomMode.Id; native or malformed metadata leaves the native mode label unchanged. Names are rendered as literal text. This does not register game mode DLLs in the official addon catalog.

## Official game mode addon (0.0.29)

CustomGameModesFramework 1.1.3 exposes GameModeCatalog API 1 with native Enabled control. It appears in Addons, keeps the saved selection, and is required for rooms with CustomMode metadata. Native rooms do not require this unused framework. During a deferred pack handshake, required mode control remains enabled while asset registration waits. Missing and disabled framework notices use the existing addon UI. The catalog scrolls when necessary and reserves a separate lower area for the Discord hint.

## Customization selection and corpse appearance

CustomPlayerCustomization 1.3.0 exposes SelectionApi 1 and SlotCount 4. NullServers uses content IDs for extra slots, never raw custom array positions. The feature is enabled only in NullFrame rooms which use the customization addon. Native appearance RPCs retain their original shape; a separate reliable, bounded packet carries the three extra slots, validates the sender against its connection, and supplies snapshots to late joiners. Leaving a room clears confirmed remote selections while retaining local preferences.

Corpse snapshots contain the appearance at death, native hat names and selected custom pack IDs. The source is matched to the dying player's network object. Corpse visuals retain their owned materials and selected hats after native initialization, alter only the fur/skin material, and create missing custom TPS hat models from the selected catalog. Skin URLs use the existing image cache and validation.
