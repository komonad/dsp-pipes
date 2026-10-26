# Development

## Local build

Copy `Local.props.example` to `Local.props`. Set `Managed` to the installed game's managed assembly directory and `BepInExCore` to the dependency profile's BepInEx core directory. Native runtime fixtures additionally use the publicized assembly selected by `GameAssembly`; the production plugin references the unmodified game assembly.

The plugin targets .NET Framework 4.7.2 and C# 7.3. A .NET SDK, framework reference assemblies and PowerShell 7 are required. The standalone core checks target .NET 8.

```powershell
dotnet build FluidPipes.csproj -c Release
dotnet build examples/PipeConsumer/PipeConsumer.csproj -c Release
dotnet build examples/CopperPipes/CopperPipes.csproj -c Release
dotnet run --project tests/CoreChecks.csproj -c Release -- tests/core-results.txt
pwsh -NoProfile -File tests/run.ps1
dotnet build tests/SceneCapture.csproj -c Release
```

`bin/Release/FluidPipes.dll` is the runtime plugin. The adjacent `FluidPipes.xml` provides IDE documentation for API consumers. The inventory consumer builds as `PipeConsumer.dll`. The material extension builds as `CopperPipes.dll`; it registers an independent item and recipe through the public API. Set its `FluidPipesAssemblyPath` property to build against a published DLL.

## Validation

The core suite includes one-million-node inventory benchmarks, randomized connectivity/conservation checks and concurrent graph operations. Runtime checks launch an isolated hidden game process, verify native Harmony integration and exit after writing `tests/runtime-results.txt`. They also require XML summaries and parameter documentation for every exported API member, and reject implementation types exposed outside the API namespace.

The default runtime run builds and loads `CopperPipes.dll` as a separate BepInEx plugin. Its tests check independent prototypes and materials, mixed copper/glass networks, native insertion and extraction, persistence and dismantling. `pwsh -NoProfile -File tests/run.ps1 -WithoutExtensions` runs with that plugin absent and writes `tests/runtime-standalone-results.txt`. The material comparison in `screenshots/pipe-copper-consumer.png` uses the live pipe renderer in a Unity runtime fixture.

`pwsh -NoProfile -File tests/run-copper-interactions.ps1` exercises native path and inserter build tools in a separate sandbox with full foundation. Matching glass and copper routes cover different headings, elevation changes and drag directions. The fixture verifies preview and completed poses, pending grid marks, dismantle hover signs, and pipe-side inserter attachment frames. Results and endpoint captures are copied to `screenshots/copper-interactions/`.

The scene harness creates an isolated sandbox, covers the planet with native foundation and renders game cameras to offscreen textures. Production fixtures can export a native save and companion mod data. The validation environment supports one active game instance, so exit the current game before launching a harness process.

```powershell
pwsh -NoProfile -File tests/capture-scene.ps1 -ParallelLine
pwsh -NoProfile -File tests/capture-scene.ps1 -ParallelLine -InspectSave samples/parallel-fluid-line/parallel-fluid-line.dsv
```

The parallel fixture contains three plastic chemical plants, sandbox source/sink towers and parallel pipe/belt lanes. It checks stable production, count conservation, pipe-to-belt inserter transfer and native construction-bar selection. Other harness modes cover geometry, connection patterns, cube networks, valve interaction and inserter appearance.

Performance reports identify the runtime, workload and measured CPU stages for each result. Interactive frame-rate assessment requires a complete loaded factory and frame-level measurements. The validation environment's allocation counter reports zero for a known allocation, so current checks classify it as unavailable and verify physical cargo counts separately. See [measurements](../tests/PERFORMANCE.md).

The OrbitalRing compatibility harness copies core files, preloaders and the required plugins from an installed OrbitalRing profile into a fresh directory under `obj/orbitalring-checks/`. It launches a hidden sandbox with an isolated document/save directory. Select the source profile explicitly:

```powershell
pwsh -NoProfile -File tests/run-orbitalring.ps1 -SourceProfile C:/path/to/OrbitalRing-profile
pwsh -NoProfile -File tests/run-orbitalring.ps1 -SourceProfile C:/path/to/OrbitalRing-profile -WithBuildBarTool
pwsh -NoProfile -File tests/run-orbitalring.ps1 -SourceProfile C:/path/to/OrbitalRing-profile -Vanilla
```

Each supply case synchronously steps `FactoryStorage.GameTickTank` 600 times, equivalent to ten seconds at 60 ticks per second. The fixture activates an infinite tank through its native inventory window, attaches a real pipe or blue belt, and records extraction totals and reservoir contents. Assertions cover 300-item continuous output, buffered backpressure, prefilled inventory, independent source capacity, activation-state retention, source switches, mixed-fluid rejection, finite count/point conservation, activation through pipe input and independent rates for multiple sources sharing one node. An unrestricted output-port probe clears the blue belt buffer every tick to verify native insertion attempts; native belt throughput requires advancing the belt simulation. `orbitalring-results.txt` records the loaded versions and each measurement; `Completed=1` indicates that all assertions passed.

Construction cases use native path determination, build conditions, prebuild placement and completion. Horizontal and elevated paths complete in opposite orders; each completion checks adjacent reciprocal native slots, and the finished route supports remote inventory extraction. The vanilla mode omits OrbitalRing and runs these same construction cases. BuildBarTool mode additionally verifies the two item bindings in the transport category's upper row.

The direct-machine fixture runs with `Integration.EnableMachinePorts=true` in a fresh hidden sandbox. It covers multi-liquid sulfuric-acid production, typed native I/O, demand limits, backpressure, reservations, recipe changes and persistence. Native construction runs in both directions against rotated fractional slot coordinates. Native tank pointer gestures verify shared insertion/extraction and separate tank/network clearing. Inspector checks cover native typography, padding, current/adjacent node silhouettes and stable outline caching. Sandbox valve cases cover all three level modes, isolated-side selection, fluid conflicts, proportional proliferator removal, sandbox gating and settings serialization. It writes thirteen screenshots and a report beneath `obj/machine-port-checks/`, and copies them to `screenshots/machine-ports/`.

```powershell
pwsh -NoProfile -File tests/run-machine-ports.ps1
pwsh -NoProfile -File tests/run-machine-ports.ps1 -InspectSave C:/path/to/machine-ports.dsv
```

## API maintenance

Public types belong in `FluidPipes.API`, except the BepInEx plugin entry point. Use PascalCase for public types, methods, properties and enum values; camelCase for parameters and private fields. Public API members carry XML summaries, parameter meanings, units, failure behavior and applicable lifetime/thread constraints. Internal comments document invariants and game API constraints.

Formatting uses the pinned local CSharpier tool and `.csharpierrc.json`. Restore it with `dotnet tool restore`, run `dotnet csharpier format .` to format, and `dotnet csharpier check .` to verify. Use explicit braces and one declaration per statement. Name gameplay variables by their domain roles; geometry may use conventional coordinate notation. Harmony hook argument names follow the native signatures because Harmony binds those names.

Keep the external facade independent of Harmony patch classes. New item insertion paths enforce admission at the shared inventory boundary. Cargo hooks return control to native methods for ordinary belt targets. Storage and topology changes retain save compatibility and conservation checks.

## Packaging

```powershell
pwsh -NoProfile -File package.ps1
```

The archive contains only `BepInEx/plugins/FluidPipes/FluidPipes.dll`. Dependencies, example plugins, saves, screenshots and verification output remain separate. `play.ps1 -PrepareOnly` prepares a local profile; `-ProfilePath` selects its destination.

`examples/CopperPipes/package.ps1` packages the material extension separately. Its archive contains only `BepInEx/plugins/CopperPipes/CopperPipes.dll`; FluidPipes supplies the runtime dependency.
