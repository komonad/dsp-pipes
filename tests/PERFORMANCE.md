# Fluid Pipes transport measurements

The allocation counter in this Unity runtime returns zero for a known 32 KiB managed allocation. Current checks use this calibration to classify the counter as unavailable. Historical zero-byte readings below come from that counter. Allocation measurements require a working counter; physical cargo counts are checked separately.

## API and configurable transport, 0.4.0

The native runtime regression suite completes with zero failures, including exported API documentation, overlapping item grants, configuration exclusions, retained inventory after revocation, native solid insertion/extraction, partial acceptance, tower sharing and native tank restrictions.

| Workload | Result |
| --- | ---: |
| Native factory I/O, 10 nodes | 148.9 ns/operation median |
| Native factory I/O, 1,000 nodes | 152.1 ns/operation median |
| Native factory I/O, 100,000 nodes | 196.8 ns/operation median |
| Native shared I/O, 100,000 nodes and one tank | 319.3 ns/operation median |
| 10,000 native inserters, 100,000 pipe nodes, 240 ticks | 0.952 ms/tick average; 2.170 ms p95 |
| Standalone .NET 8 graph I/O, 1,000,000 nodes | 123.3 ns/operation median |

The native rows measure patched factory storage calls and inserter simulation; the standalone row measures graph operations on .NET 8. Frame rendering and public API facade overhead require their own workloads. Stable I/O performs zero topology visits. The Unity allocation counter remains unavailable. The native sandbox reload fixture also preserves production at 60 plastic/minute with zero output stock delta.

## Shared fluid spaces and construction, 0.3.0

Version 0.3.6 also exercises native power-grid integration using a real wind generator and electricity coverage. A 1667 J/tick valve demand transfers 0, 30 and 60 units per second at generation capacities of 0, 834 and 3334 J/tick, respectively. Closed valves request zero energy. Restoration adds the valve's missing native consumer and preserves its electrical coverage. Valve ticking visits a cached per-factory valve list, including valves with detached pipes.

On a copied 233-node factory save, the original hover/preview handlers and production renderer give these CPU measurements. Each mode restores its starting connections and mod inventory and warms its meshes. These measurements exclude GPU execution and hardware mouse raycasting.

| Operation | 0.2.6 median / maximum | 0.3.0 median / maximum |
| --- | ---: | ---: |
| Build hover | 0.599 / 2.793 ms | 0.022 / 2.380 ms |
| Moving build path preview | 0.705 / 3.801 ms | 0.032 / 3.251 ms |
| Dismantle hover | 0.007 / 3.348 ms | 0.008 / 3.570 ms |

Two native nine-node prebuild placements take 0.081 and 0.274 ms, with zero generation-2 collections during the calls. Pipe construction uses native point previews and leaves garbage collection scheduling to the runtime. Actual dismantling with drawing on the copied 233-node scene has a median of 5.178 ms and a maximum of 83.801 ms. Junction and chunk mesh rebuilds produce occasional dismantling spikes.

Native pipe insert/extract operations on a 100,000-node network connected to a 10,000-unit storage tank have a median cost of 302.1 ns per operation. The test performs three samples of 200,000 insert/extract pairs and verifies inventory/proliferation conservation with zero topology visits. The shared capacity is 10,010,000 units. The measured workload consists of storage access calls. A separate 10,000-inserter fixture on 100,000 pipe nodes averages 1.037 ms per tick, with a 2.479 ms 95th percentile and 4.324 ms maximum over 240 ticks.

Coverage includes equal-percentage tank stacks and tower slots, one-time capacity accounting for duplicate ports, storage bridges between separate pipe components, filtered tower input binding, extraction beyond the local pipe/tower share, ordinary tank output from a locally empty share, isolated clearing, container removal/refunds, save restoration, two-sided valve isolation, fractional rate limits, reverse-flow rejection, both level targets and incompatible-fluid rejection. The loaded gameplay fixture invokes native inspection and uGUI button callbacks. The native placement fixture accepts an eight-degree branch with active connected-pipe colliders while retaining solid-building rejection.

Raw measurements: `runtime-results.txt`, `interaction-results.txt` and `screenshots/ingame/gameplay-results.txt`.

## Historical transport measurements, 0.2.3

Measured 2026-10-21 for Fluid Pipes 0.2.3 on an AMD Ryzen 7 9800X3D, Windows x64. Game: Dyson Sphere Program 0.10.35.29151, Unity 2022.3.62, Mono CLR 4.0.30319.42000. Release build, BepInEx 5.4.17, CommonAPI 1.6.7, LDBTool 3.0.3, DSPModSave 1.2.2.

## Native factory I/O

The benchmark runs the patched original `PlanetFactory.InsertInto` and `PickFrom` methods. Each sample performs 200,000 insert/take pairs after 10,000 warm-up pairs. Endpoint IDs vary deterministically throughout the component. The table shows the median of five samples; one operation is either an insert or a take. Allocation counters cover the loop on the calling thread and exclude measurement/report construction.

| Pipe nodes | Median per operation | Observed sample range | Allocated bytes across 2 million operations |
| ---: | ---: | ---: | ---: |
| 10 | 91.3 ns | 90.8–91.8 ns | 0 |
| 1,000 | 93.1 ns | 92.9–93.3 ns | 0 |
| 100,000 | 139.6 ns | 136.2–147.4 ns | 0 |

Every run asserts zero topology visits during steady I/O and an empty final inventory. Larger components increase memory/cache costs, while the operation count remains independent of component size.

## Inserter workload

10,000 original `InserterComponent.InternalUpdate` state machines run against a shared 100,000-node component for 240 ticks. Travel duration is 10,000 internal units, with one item per pickup; half the inserters enable the native stacking insertion path. All inserters start in phase. Every tick checks total inventory and proliferation points across the network and items held by inserters.

| CPU time per simulated tick | Measurement |
| --- | ---: |
| Average | 0.619 ms |
| 95th percentile | 1.429 ms |
| Maximum | 1.626 ms |

The timed section consists of inserter state machines and pipe storage calls. Power simulation, placement, planet rendering, other factory systems and conservation checks run outside that section. Frame-time assessment requires measurements covering the complete frame.

## Topology and contention

These transport reference measurements use version 0.2.3. Version 0.2.5 adds node-geometry caching and constant-topology-visit leaf cuts and removals.

The Unity/Mono core suite rebuilds a 100,000-node chain after removing its middle edge in 20.53 ms. Native geometry/path changes are excluded from this number. Repeated large disconnections and dismantling can exceed a frame budget because they repeatedly traverse and repartition the affected component.

On one shared 100,000-node inventory, the Unity/Mono core benchmark reaches 19.262 million operations/s with one worker, 10.330 million with four, and 7.840 million with eight. Workers contend on the shared inventory lock, reducing throughput as worker count increases in this workload. Counts and proliferation points remain conserved.

The standalone .NET 8 suite additionally tests a 1,000,000-node component: 452.82 ms incremental construction and 134.6 ns median per storage operation, with zero topology visits during I/O. These results characterize .NET 8 execution; the Unity/Mono rows characterize execution inside the game.

## Interaction CPU measurements (0.2.5)

A saved 75-node factory fixture measures the original dismantle-hover, build-hover and active path-preview stages before action confirmation. Each mode restores the same initial connections and inventory, warms the pipe meshes and collects 20 samples. The timed section includes handler, preview, gizmo and production pipe-draw CPU work. GPU execution and complete frame timing require additional measurements.

| Operation | 0.2.4 median / maximum | 0.2.5 median / maximum |
| --- | ---: | ---: |
| Dismantle hover | 361.882 / 670.613 ms | 0.004 / 3.470 ms |
| Build hover | 0.167 / 2.345 ms | 0.661 / 4.990 ms |
| Moving build path preview | 0.237 / 3.768 ms | 0.612 / 3.900 ms |

The fixture also dismantles all 75 copied pipe entities through the original factory API, drawing after each deletion: median 1.164 ms, maximum 29.706 ms. Every remaining pipe retains a native cargo path; removed nodes leave both logical graphs. The fixture operates on in-memory copies of the source save data.

An elevated 16-node ring is placed in two batches with recycled pending IDs and reverse completion order. Every ring node retains exactly its two adjacent peers and reciprocal native slots. Dismantling with drawing takes a median 1.035 ms and maximum 6.597 ms in this fixture. A separate 100,000-node storage-graph benchmark removes 10,000 consecutive endpoints with zero topology visits; it excludes native factory and rendering work.

Raw interaction measurements: `interaction-results.txt`.

## Correctness coverage and limits

Both suites pass. Coverage includes type/capacity rejection, filtered extraction, empty-network fluid changes, deterministic splits, compatible/incompatible merges, binary save restoration, 20,000 randomized operations checked against an independent connectivity and stock oracle, and concurrent I/O during topology changes. Runtime coverage includes cached/native factory I/O, physical-cargo absence, ordinary belt regression checks, native path concatenation, closed loops, junctions, rerouting, dismantling/refund accounting, belt ID recycling, tank updates, station updates, and splitter rejection.

Runtime checks also cover simultaneous pipe-fluid and ordinary-belt transfers on station ports, either connection order on tank/station ports, a 13-interface arbitrary-angle pipe junction, idempotent reverse connections, pending-node completion and additional-link serialization. Preview-condition checks exercise elevated and acute-angle pipe connections together with physical collision rejection and ordinary belt rules.

Edge-cut fixtures cover native-backed and additional connections, preserved endpoints and total capacity, alternate routes around a ring, bridge splits, fluid and proliferation conservation, repeated reverse cuts and restoration of a disconnected edge. Elevated-junction condition checks retain physical collision rejection.

Geometry checks cover degrees 0, 1, 2, 3, 13 and 100, a convex three-dimensional corner, finite vertices and outward face winding. Collinear segments share a continuous glass surface. Live chunk rendering and inventory-driven fluid appearance changes execute in the game runtime; inventory changes select materials using the cached outer mesh. The gallery captures six procedural shapes with the production materials. Quantifying large-network rendering and junction mesh-generation costs requires dedicated timing fixtures.

The separate scene harness loads a fresh sandbox planet and creates 45 pipe entities, 43 native Mk.I belt entities and two native storage tanks through factory APIs. Nine offscreen game-camera captures show paired straight, elbow, T junction and elevated layouts with real terrain and lighting, empty/water-filled comparisons and isolated rounded-end nodes. A second 69-node scene captures straight, elbow, oblique Y, T, vertical inlet and eight-interface forms with identical empty/filled camera views. Native renderer creation, pipe belt-mesh suppression and production pipe drawing run together. Captures are checked for nonuniform pixels and inspected visually. These fixtures validate material appearance, geometry and native rendering integration on small layouts.

Additional fixtures capture paired serpentine, elevated crossing and single/multiple-cycle structures, plus a 3-unit cube frame. The cube contains 32 nodes and 36 edges, with five independent cycles. Every node exposes the same 1,280-unit inventory immediately after insertion and zero stock after extraction at one node. Cube captures include matched empty/filled views and two additional empty viewing angles.

The loaded cube fixture executes the original `BuildTool_Path.CheckBuildConditions` with native colliders activated: both belts and pipes accept clear terrain and reject a solid storage-box obstruction. Cutting one cube edge retains alternate connectivity; isolating a corner preserves all 32 entities and apportions 1,280 water units as 40 and 1,240. Reconnecting restores the total inventory to 1,280 units. A separate capture shows the visible gap after cutting one edge.

Large runtime fixtures contain native entity/belt pools and 100 segments per cargo path, with synthetic geometry. Small construction fixtures execute original connection and geometry mutations with rendering callbacks suppressed. The parallel production fixture verifies a complete native save reload. Blueprint placement, mouse-driven visual interactions, third-party transport patches and multiplayer require dedicated validation fixtures.

Raw results: `runtime-results.txt` and `core-results.txt`. Reproduction commands are in the project README.
