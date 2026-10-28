# Architecture

> This document is also available in [Chinese](ARCHITECTURE.md).

## Module boundaries

| Directory | Responsibility |
| --- | --- |
| `Core/` | Game-independent connectivity, single-item inventories, deterministic split/merge accounting and immutable item-admission sets |
| `src/API/` | Supported external contract, validated options and detached snapshots |
| `src/Configuration/` | BepInEx configuration parsing and startup options |
| `src/Runtime/` | Factory ownership, effective item admission, shared native reservoirs and valve simulation |
| `src/Integration/Belts/` | Native cargo-path adaptation, Harmony method hooks and belt lifecycle hooks |
| `src/Integration/Reservoirs/` | Native tank/station port suppression, shared extraction and reservoir lifecycle hooks |
| `src/Integration/Machines/` | Optional production-machine slot exposure and native connection lifecycle hooks |
| `src/Geometry/` | Procedural pipe and inserter mesh generation |
| Other `src/` files | Prototype registration, construction interaction, topology metadata, inspection UI and rendering |

The external API delegates to runtime services. Harmony cargo hooks resolve native targets and call `PipeCargoAdapter`; the adapter implements native transfer-size and partial-acceptance conventions. Admission is enforced at the shared inventory insertion boundary, so direct API callers, inserters, native cargo calls and the inspection panel use the same rule.

Public type registration, appearance settings, geometry profiles and value snapshots live in separate API files. Registry ownership and connection notification queues belong to internal runtime services. `CopperPipes.dll` consumes the public contract as an independently loaded plugin, registering an opaque material variant while retaining the default glass type and shared-inventory family.

The library owns generic transport, connection topology, local cross-sections and path appearance. Extensions own domain rules for cycle completion, planetary coordinates, facility-slot layouts, building attachments and operating conditions. Appearance callbacks read extension state and set batch rendering properties; domain rules remain outside the inventory and graph models.

## Inventory invariants

`PipeGraph` owns each pure pipe component's item count and proliferator points. Each completed pipe node contributes 10 units of capacity. `PipePools` reads and writes the authoritative native tank and station fields. A shared space may contain multiple graph components joined by native reservoirs. External callers receive detached inventory and settings values.

Every space has one item identity, including any fixed station-slot filter. Connections reject incompatible identities. Reservoir conflicts suspend balancing and inventory operations while preserving stock. Native tanks enforce their fluid-only storage type after the global item-admission check.

Item admission is an immutable set published after prototype initialization and registration changes. Explicit configuration exclusions take precedence over all grants. Registration ownership is independent: releasing one grant preserves overlapping grants. Pool caches track both graph topology and policy versions. Revocation blocks insertion; extraction remains available for retained stock.

Storage arithmetic preserves integer counts and total proliferator points. Insertion validates capacity before committing the full transfer. Shared-reservoir insertion records modified stores; a partial write triggers restoration of those stores and rejection of the batch. Native cargo adaptation supports partial acceptance with proportional point accounting.

Imported inventories above capacity retain their item counts and proliferator points and remain available for extraction. Shared balancing waits until the combined capacity can accommodate the stock; insertion resumes as withdrawals create space. Inventory replacement can only preserve or reduce the surplus.

OrbitalRing infinite tanks own their inventory and activation sentinel independently. The optional compatibility module resolves the prototype and transfer method from the loaded plugin, then keeps these tanks outside shared-reservoir reconciliation. Native input and activation remain authoritative. Pipe output follows a two-tick cadence for 30 items/s; blocked output accrues no transfer credit. Cadence state follows the cargo-path lifetime through weak references. Ordinary belt calls retain native behavior.

## Lifetime and threading

Direct machine connections bind an existing pipe slot to a native inserter slot. Machine input/output buffers retain native ownership. Each endpoint carries an immutable item/direction selection and enable state. Transfers use the same typed factory I/O methods as inserters; input writes follow native demand thresholds, and product reads follow pipe capacity. A bounded per-tick batch limits endpoint work. Rejected deliveries retain staged items until delivery or dismantling, and staged stock participates in endpoint serialization.

Enabled recipe-compatible filters reserve the identity of the entire shared storage space. Filter indexes follow connection, recipe, admission and reservoir-binding revisions. Stable transfer checks use cached storage-group identities; inspection and entity migration visit bounded native slots. Global disablement suspends transfer and reservations while preserving native connections and saved settings.

Native prebuild migration updates reciprocal object slots in separate writes. Endpoint observation remains suspended across that mutation so settings migrate after both sides become consistent. Pipe-only construction port poses retain native slot indices and place a terminal outside the machine housing before joining the grid route.

Inventory inspection reuses the native tank window and pointer gestures. A private UI projection supplies shared totals to the stock control; the resulting gesture delta commits through shared storage, with exact hand counts and proliferator points. Rejected gestures restore the hand state. Current-tank clearing removes only the selected tank's physical stock; whole-network clearing visits every storage unit in its shared space. Subsequent simulation updates redistribute remaining fluid by capacity.

Inspection properties and controls clone native tank UI elements. Action captions retain the native button font, material, size and style. All adjacent connections appear in a two-column list whose height follows its row count; cached button instances track their current connection index. The current pipe contributes a green model silhouette to the native outline pass. A subsequent cyan pass draws directly adjacent nodes using the same temporary buffers and separately owned composite materials. Per-node outline meshes share the renderer's geometry cache identity and rebuild when that geometry changes. Closing inspection releases outline meshes; screen labels yield to the panel bounds.

Sandbox infinity valves regulate one endpoint's entire shared space. At-least mode supplies a deficit, at-most mode removes a surplus, and exact mode performs either operation toward the integer target count. Both operations use shared inventory boundaries and preserve item identity, capacity, reservations and proliferator accounting. Infinity regulation depends on sandbox mode and the valve enable state; normal valves use electrical pump simulation. Saved infinity settings resume with their sandbox gate after loading.

Factory graphs and native owner bindings are cleared on save changes. Entity IDs are native pool indices and may be reused. Each external API call resolves its target through the supplied factory/entity binding, which the caller refreshes after save or entity changes. Additional undirected links are persisted independently of the four native belt connection slots.

Native object-connection rows use the stride of the allocated entity or prebuild pool. Prebuild migration and stale-record cleanup derive that stride from the matching arrays, including OrbitalRing's expanded connection layout. Migration preserves reciprocal slot identities before retiring the source row.

The core graph synchronizes topology and per-component inventory. Shared-reservoir operations synchronize their state and use native station storage locks where required. Game-object access follows the native simulation lifecycle: external factory operations run in the main thread or an exclusive factory simulation context. Registration changes run on the main thread.

## Cost model

Each registered pipe type defines simulation behavior, direction policy, connection family, geometry dimensions and appearance independently. Shared-inventory types use undirected connectivity. Structural types expose graph queries and connection notifications for an external simulation; directed structural types retain native belt input/output slots and cargo paths. Compatibility requires matching behavior, direction policy and connection family. Registration seals against the final item database before factory initialization.

Connection notifications accumulate in a synchronized queue and dispatch on the main thread. Each dispatch deduplicates factory/entity pairs, invokes subscribers outside the queue lock and defers reentrant mutations to the next batch. Save changes clear pending notifications. Consumers resolve entity identity again when processing events because native entity pool indices are reusable.

Array topology queries return detached results; `Copy*` queries clear and reuse a caller-owned list. Sufficient list capacity and a current graph cache avoid result-array allocations, while connected-component queries still traverse the component. `TryGetTopologyVersion` reads a factory's cached revision in O(1) time to coalesce repeated recomputation. Revisions are comparable only within the same factory's runtime graph lifetime and do not track inventory or external operating state.

Stable pure-pipe I/O accesses inventory through the component index. Shared I/O visits storage units as needed; reconciliation visits reservoirs and graph components. Topology/policy changes rebuild reservoir bindings lazily. Node removal or an interior edge cut may traverse the affected graph component. API inventory queries use cached totals; full snapshots additionally aggregate capacity contributors.

Rendering caches geometry independently of inventory. Fluid presence and item identity select materials, while connection changes invalidate geometry caches. Native construction geometry, connection slots and inserter logic form the compatibility boundaries tested inside the Unity runtime.

## Verification

`tests/CoreSuite.cs` exercises connectivity, conservation, randomized operations, concurrent core I/O and admission-set rules. `tests/ApiChecks.cs` exercises the public contract and verifies XML documentation for every exported API type, member and parameter. Native runtime fixtures test cargo hooks, containers, valves, placement and inserters. The example consumer builds as its own assembly.
