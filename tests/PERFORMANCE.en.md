# Performance measurement

> This document is also available in [Chinese](PERFORMANCE.md).

Performance checks measure the core graph, native I/O, inserters and rendering separately. Comparisons use the same workload, build configuration and environment, with the code revision, plugin and game versions, CPU/GPU, resolution and raw report recorded together.

## Running the checks

Configure `Local.props` using the [development guide](../docs/DEVELOPMENT.en.md), then run from the repository root:

```powershell
dotnet run --project tests/CoreChecks.csproj -c Release -- tests/core-results.txt
pwsh -NoProfile -File tests/run.ps1
```

Core checks use .NET 8; runtime checks use Unity/Mono in a separate background game process. Runtime checks require the installed game and dependencies, with the current game closed. They load the CopperPipes example by default; `-WithoutExtensions` loads only the library.

| Workload | Implementation and output |
| --- | --- |
| Core graph I/O, splits, endpoint removal and lock contention | [CoreSuite.cs](CoreSuite.cs); `core-results.txt` |
| Native factory I/O, shared containers and inserters | [RuntimeChecks.cs](RuntimeChecks.cs); `runtime-results.txt`, or `runtime-standalone-results.txt` with only the library |
| Orbital structure rendering | [Seven-ring scene and report index](performance/seven-rings/README.md) |

## Core graph

Standalone checks cover chains of 10, 1,000, 100,000 and 1,000,000 nodes. Each workload warms up with 10,000 insert/take pairs, then collects five samples of 200,000 pairs. One operation means either an insert or a take. `median_ns_per_op`, `min` and `max` describe the median and range of per-operation times across those five samples; `build_ms` records construction separately.

Topology workloads cut the middle edge of a 100,000-node chain and remove 10,000 consecutive endpoints. Steady I/O checks that the topology visit counter remains unchanged. Split timing includes traversal and repartitioning of the affected component, excluding game entities, collision and mesh updates.

The contention workload uses 1, 4 and 8 parallel workers scheduled by `Parallel.For` on one shared 100,000-node inventory and reports `aggregate_Mops_s`. It checks graph-lock throughput and conservation; external API calls involving game objects still follow the [threading contract](../docs/API.md#线程性能和存档).

## Native I/O and inserters

The native factory workload calls patched `PlanetFactory.InsertInto` and `PickFrom` methods. It uses the same warm-up and five-sample sizes as the core graph on 10, 1,000 and 100,000 nodes, varying endpoints throughout the component.

The shared-container workload connects 100,000 pipe segments to one 10,000-unit tank, giving 1,010,000 units of total capacity. Three samples each perform 200,000 insert/take pairs and check counts, proliferator points and topology visits. `shared_large_native_io` reports the median time per insert or take.

The inserter workload advances 10,000 native inserters on one 100,000-node network for 240 ticks. Each tick checks total inventory and proliferator points across the network and held items, reporting `average_ms_tick`, `p95_ms_tick` and `max_ms_tick`. Timing includes inserter state machines and storage calls; conservation assertions, power simulation, other factory systems and frame rendering are outside the timed section.

## Rendering and interaction

Rendering measurements fix the save, camera, resolution, graphics settings and warm-up conditions, recording mesh rebuilds, submitted batches, triangles, CPU submission time and complete frame time. Steady drawing is sampled separately from first visibility and rebuilds after cuts, reconnection or dismantling.

The [seven-ring index](performance/seven-rings/README.md) describes the structure-rendering scene, sampling method and raw reports. `interaction-results.txt` contains CPU samples for hover, path previews and dismantling; each group must be interpreted with its scene and build identifiers.

`capture-scene.ps1` and `run-copper-interactions.ps1` check geometry, native construction and screenshots. Screenshot correctness does not establish complete-frame performance; offscreen synchronous readback, background processes and interactive gameplay also use different frame-time conditions.

## Interpretation limits

- Compare standalone .NET 8 graph timings separately from in-game Unity/Mono timings.
- Steady I/O does not traverse topology; large disconnections can traverse the affected component, with mesh rebuilds adding separate costs.
- The allocation counter is calibrated with a known 32 KiB managed allocation. Failed calibration produces `allocated_bytes=unavailable` and cannot establish zero allocation. Physical cargo counts are checked separately.
- Report CPU phase timings, GPU synchronization timings and complete frame times separately. Inventory benchmarks do not establish game frame rates.
- `Failures=0` at the end of `runtime-results.txt` confirms that run passed; an existing report describes the build and environment that produced it.

See [testing instructions](../TESTING.md) for in-game correctness and manual checks.
