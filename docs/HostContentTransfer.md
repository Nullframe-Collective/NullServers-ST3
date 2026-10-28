# Host content downloads

## Implemented (v1)

- **Scope:** weapon, cosmetic and NPC packs. Maps (scene bundles), game modes and every DLL still go through Discord.
- **Consent:** Download in Required Addons is offered only when every missing item is such a pack and the matching framework (with the new `Mount` API) is installed and enabled in ADDONS. Consent covers the exact pack id + hash pairs shown, for that room, for 10 minutes.
- **Join:** the client rejoins through the game's own `NetBootstrap` join. Registration stays held, exactly as in the deferred-catalog path. When the host's Offer arrives, missing consented packs are requested instead of failing.
- **Transport:** FishNet reliable broadcasts through the relay (`ContentMessage`, id 0x4E4C). Client pulls ranges of ≤128 KiB (4 in flight); host answers with ≤32 KiB chunks.
- **Host limits:** 2 MiB/s per player and 8 MiB/s total, at most 8 queued ranges. Only packs in the room's advertised manifest (matching id and hash) and only fixed file names per pack type are served. Every request extends the player's handshake deadline.
- **Client storage:** `UserData/NullFrame/Downloads/.partial-<kind>-<hash>`, outside framework discovery. File list limits: bundle 256 MiB, JSON 256 KiB, images 4 MiB, total 1 GiB per join.
- **Checks before Unity sees any byte** (`PackFiles.Check`, background thread):
  - no extra files;
  - JSON object;
  - the framework's own content hash must equal the advertised one;
  - PNG/JPEG ≤ 2048²;
  - `BundleInspector`: UnityFS v6–8, LZ4 only (LZMA rejected), every block and serialized object decoded through its type tree; class allowlist per pack type; no MonoScript/MonoBehaviour, no UnityEvent calls, no animation events, no external file dependencies; texture ≤ 4096², mesh ≤ 500k vertices.
- **Load and cleanup:** after the checks the folder becomes `Downloads/<kind>-<hash>` (reused and re-checked on later joins) and is mounted through `WeaponCatalog`/`CustomizationCatalog`/`NPCCatalog.Mount`. The normal handshake then selects it. Leaving the room unmounts and restores replaced local packs.
- **UI:** in the room, a full-screen panel in the Required Addons style shows speed, MB done / total, a percentage above the progress bar, and per-pack status. Cancel leaves the room.

Not done yet: map scene bundles, shader bytecode restrictions, cache size quota, two-client field test.

## Original design notes

Status: design agreed with the user; transfer and scanners are not implemented or enabled in 0.0.33.

Players may accept packs from any host. A NullFrame signature is not required. The client must explicitly accept the specific pack hashes, and every downloaded pack must pass mandatory checks before a framework can load it. A warning alone must never bypass a failed or unavailable scanner.

## Player flow

1. Join reads the required content manifest, without loading the destination scene or host assets.
2. The existing Required Addons view lists missing framework DLLs separately from missing content packs. DLLs retain manual installation from Discord. Extra local packs remain disabled for this room.
3. For downloadable content the view shows the host, pack name, kind, byte size, and download total. The warning explains that host content is untrusted and checks reduce risk without guaranteeing safety. The actions are Close and Download from host.
4. After consent, progress distinguishes receiving, verifying, scanning, and preparing content. Cancel stops the transfer and leaves the room.
5. Failed verification shows a concrete reason. There is no Run anyway action. Network errors may be retried without changing the accepted hashes.
6. A complete, verified set is mounted for this room. The normal framework compatibility handshake checks the final manifest again before spawning the player. If registry reload is unsupported, show Restart required and preserve the verified cache for the next join.

Do not label a pack Safe or Virus free. Use Checks passed, accompanied by the warning about residual risk. Consent for one hash does not authorize a replacement uploaded under the same pack name.

## Transport

NullServers currently contains the game client/mod, not a deployable relay file service. Implementing downloads before room admission requires an explicit transport extension; no public host HTTP listener or arbitrary download URL is assumed.

The preferred relay extension is a room-scoped transfer channel available before scene loading. A content session has a random session ID, expiry, manifest digest, host connection, and requesting client connection. Only the room's current host may publish the manifest and send chunks. The relay routes only requested, consented file IDs to that client. On host change, disconnect, manifest change, or scene transition, outstanding sessions are cancelled.

If implemented over the existing FishNet connection instead, introduce a quarantine stage before the current scene queue, player spawn, and framework Ready state. Preserve the same bounds and host binding, and extend handshake deadlines only while bounded transfer progress occurs. Do not pretend that the existing short compatibility handshake already supports large files.

Messages are bounded Manifest, Request, Chunk, Complete, Cancel, and Failure records. A chunk carries session ID, manifest digest, file index, exact offset, and bounded data. No network record contains a filesystem path, executable command, arbitrary URL, archive extraction instruction, or type to instantiate. Use authenticated encrypted transport; do not add plaintext TCP or trust a caller-supplied URL. Relay-side per-host and per-client limits are required in addition to client checks.

Initial conservative limits, to be validated against real packs before release:

| Resource | Limit |
| --- | --- |
| Manifest | 256 KiB, at most 128 packs and 1024 files |
| Descriptor JSON | 256 KiB, bounded depth and collection lengths |
| Bundle file | 256 MiB |
| Download total per join | 1 GiB with explicit displayed total |
| Chunk | 32 KiB, one active file, at most four in-flight chunks |
| Chunk inactivity | 30 seconds; bounded total transfer duration |
| Decoded bundle data | 1 GiB per file, plus object/type-specific limits |
| Scanner | Fixed memory/CPU/wall-clock budgets; terminate on excess |

These are policy defaults, not current runtime capabilities. All arithmetic uses checked operations. Validate headers and declared lengths before allocation, file creation, or decompression.

## Export and cache boundary

The host exporter accepts registered framework packs only. It builds a manifest from known descriptor fields and local files, not from a request-supplied path. It must reject directory traversal, absolute/UNC/device paths, alternate data streams, duplicate/case-colliding names, symlinks, junctions, other reparse points, and files outside the registered pack root. Resolve and open files without following a changed reparse target. Snapshot bytes or verify the open file's size/hash throughout export so a host file change cannot silently alter a transfer.

Allow only the framework's descriptor JSON, declared UnityFS bundles, and explicitly supported image/audio sidecars needed by that pack. Check formats by structure, not just extension. Never transfer DLL, EXE, C# source, scripts, plugins, batch files, archives, arbitrary game configuration, or unrelated files from UserData. No game or framework assembly can be requested as a dependency.

Use a dedicated NullFrame download cache with generated hash-based names, outside every framework's startup discovery directory. Receive into newly created partial files with disk quotas; reject gaps, overlaps, duplicate offsets, unexpected files, and size overruns. Compute SHA-256 over the received bytes and compare with the consented manifest. A host-supplied SHA-256 checks byte identity, not trustworthiness.

After validation, atomically publish an immutable cache entry with a local scan receipt keyed by file hash, parser/scanner policy version, framework schema version, and supported engine format. Do not overwrite Mods, existing installed packs, or game files. Frameworks need explicit cache-mount/registry-reload APIs; copying untrusted bytes into an auto-discovered folder before validation is prohibited. Invalidate receipts when the bytes or policy change. Re-check the actual file hash on use and prevent modifications between verification and loading.

## Mandatory verification before Unity loading

Use a separate, low-privilege, network-denied worker for parsing and scanning, with read access only to the transferred snapshot and a small writable temporary directory. A normal child process running with the game's full permissions is not isolation. Do not load Unity assemblies, instantiate prefabs, invoke framework loaders, or call AssetBundle.LoadFromFile/LoadAllAssets to decide whether a bundle is safe.

The worker must:

- Validate the UnityFS header, supported serialized format, block directory, offsets, decompression sizes, object counts, references, and external dependencies. Reject encrypted/unknown formats, inconsistent structures, trailing embedded payloads, and resource exhaustion inputs.
- Parse all serialized objects using a versioned, bounded parser and approved schemas. If type information is missing or an object cannot be validated, reject it. Do not skip unknown objects because they are not the pack's main prefab.
- Allow only required Unity types and specifically approved game/framework component classes. Script identity alone is insufficient: validate each approved component's serialized fields. Reject reflection targets, arbitrary file/URL destinations, arbitrary method names, unmanaged plugins, and unknown behaviours.
- Reject serialized persistent UnityEvent calls and unsupported AnimationEvents. If a framework genuinely needs events, allow only reviewed, exact target-type/method/argument combinations. Sanitizing after instantiation is too late because Awake/OnEnable may already have executed.
- Validate descriptor commands/triggers with the framework's fixed action schema and bounds. Host JSON must not become arbitrary reflection, resource paths, or scripts.
- Bound textures, meshes, audio, animation, materials, and physics objects. Treat custom shaders and other complex native-parser inputs as a separate compatibility/security profile; reject unsupported ones rather than silently allowing them.
- Run a supported local malware scanner over the complete files. Detection, unavailable protection, scan error, timeout, worker crash, or incomplete coverage prevents loading. Scanner success is an additional signal, not proof that Unity serialization is harmless. Do not upload user/host packs to third-party scanning services without separate consent.

The loaded graph must be the graph that was scanned: validate all dependencies, disallow unresolved external files, and verify bytes again when mounting. Audited code that loads verified data must not resolve missing dependencies from an unrelated host cache entry.

## Required tests before enabling downloads

Exercise traversal/UNC/ADS/reparse attacks; conflicting filenames; malformed/truncated frames; integer overflows; oversized manifests; compressed bombs; malformed type trees; hidden executable files; unknown components; event callbacks; invalid dependencies; duplicate/out-of-order chunks; disk exhaustion; cancellation; host migration; replacement under an unchanged name; scanner absence/timeouts/crashes; mutation after scanning; and rejoin after a policy upgrade.

Integration tests must cover a clean client downloading each supported framework pack, a missing framework DLL, extra local packs, different pack hashes, interrupted transfer, restart/rejoin, and scene changes with two real clients. Loading must remain blocked on every validation failure, including when the host says the pack is approved.

## Residual risk

Arbitrary host content cannot receive a complete safety guarantee inside this unmodified Unity player. Engine vulnerabilities and unsafe existing component code remain possible even after structural checks and antivirus scanning. Stronger guarantees require restricting supported bundle features, rebuilding uploads through a controlled content pipeline, or sandboxing the game itself. Some existing packs may therefore require rebuilding to pass the automatic-download policy; manual installation must not be silently treated as a clean scan result.

Primary references:

- [Unity: AssetBundle security and mitigations](https://unity.com/blog/engine-platform/upm-dependency-confusion-assetbundle-security-in-the-editor)
- [Unity Manual: AssetBundle integrity](https://docs.unity.com/en-us/engine/6000.7/manual/assets-and-media/assets-managing-runtime/assetbundles-section/asset-bundles-integrity)
