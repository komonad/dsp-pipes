# Architecture

## Module boundaries

| Directory | Responsibility |
| --- | --- |
| `Core/` | Game-independent connectivity, single-item inventories, deterministic split/merge accounting and immutable item-admission sets |
| `src/API/` | Supported external contract, validated options and detached snapshots |
| `src/Configuration/` | BepInEx configuration parsing and startup options |
| `src/Runtime/` | Factory ownership, effective item admission, shared native reservoirs and valve simulation |
| `src/Integration/Belts/` | Native cargo-path adaptation, Harmony method hooks and belt lifecycle hooks |
| `src/Integration/Reservoirs/` | Native tank/station port suppression, shared extraction and reservoir lifecycle hooks |
| `src/Geometry/` | Procedural pipe and inserter mesh generation |
| Other `src/` files | Prototype registration, construction interaction, topology metadata, inspection UI and rendering |

The external API delegates to runtime services. Harmony cargo hooks resolve native targets and call `PipeCargoAdapter`; the adapter implements native transfer-size and partial-acceptance conventions. Admission is enforced at the shared inventory insertion boundary, so direct API callers, inserters, native cargo calls and the inspection panel use the same rule.

## Inventory invariants

`PipeGraph` owns each pure pipe component's item count and proliferator points. `PipePools` reads and writes the authoritative native tank and station fields. A shared space may contain multiple graph components joined by native reservoirs. External callers receive detached inventory and settings values.

Every space has one item identity, including any fixed station-slot filter. Connections reject incompatible identities. Reservoir conflicts suspend balancing and inventory operations while preserving stock. Native tanks enforce their fluid-only storage type after the global item-admission check.

Item admission is an immutable set published after prototype initialization and registration changes. Explicit configuration exclusions take precedence over all grants. Registration ownership is independent: releasing one grant preserves overlapping grants. Pool caches track both graph topology and policy versions. Revocation blocks insertion; extraction remains available for retained stock.

Storage arithmetic preserves integer counts and total proliferator points. Insertion validates capacity before committing the full transfer. Shared-reservoir insertion records modified stores; a partial write triggers restoration of those stores and rejection of the batch. Native cargo adaptation supports partial acceptance with proportional point accounting.

## Lifetime and threading

Factory graphs and native owner bindings are cleared on save changes. Entity IDs are native pool indices and may be reused. Each external API call resolves its target through the supplied factory/entity binding, which the caller refreshes after save or entity changes. Additional undirected links are persisted independently of the four native belt connection slots.

The core graph synchronizes topology and per-component inventory. Shared-reservoir operations synchronize their state and use native station storage locks where required. Game-object access follows the native simulation lifecycle: external factory operations run in the main thread or an exclusive factory simulation context. Registration changes run on the main thread.

## Cost model

Stable pure-pipe I/O accesses inventory through the component index. Shared I/O visits storage units as needed; reconciliation visits reservoirs and graph components. Topology/policy changes rebuild reservoir bindings lazily. Node removal or an interior edge cut may traverse the affected graph component. API inventory queries use cached totals; full snapshots additionally aggregate capacity contributors.

Rendering caches geometry independently of inventory. Fluid presence and item identity select materials, while connection changes invalidate geometry caches. Native construction geometry, connection slots and inserter logic form the compatibility boundaries tested inside the Unity runtime.

## Verification

`tests/CoreSuite.cs` exercises connectivity, conservation, randomized operations, concurrent core I/O and admission-set rules. `tests/ApiChecks.cs` exercises the public contract and verifies XML documentation for every exported API type, member and parameter. Native runtime fixtures test cargo hooks, containers, valves, placement and inserters. The example consumer builds as its own assembly.
