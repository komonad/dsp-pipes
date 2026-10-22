# Development

## Local build

Copy `Local.props.example` to `Local.props`. Set `Managed` to the installed game's managed assembly directory and `BepInExCore` to the dependency profile's BepInEx core directory. Native runtime fixtures additionally use the publicized assembly selected by `GameAssembly`; the production plugin references the unmodified game assembly.

The plugin targets .NET Framework 4.7.2 and C# 7.3. A .NET SDK, framework reference assemblies and PowerShell 7 are required. The standalone core checks target .NET 8.

```powershell
dotnet build FluidPipes.csproj -c Release
dotnet build examples/PipeConsumer/PipeConsumer.csproj -c Release
dotnet run --project tests/CoreChecks.csproj -c Release -- tests/core-results.txt
pwsh -NoProfile -File tests/run.ps1
dotnet build tests/SceneCapture.csproj -c Release
```

`bin/Release/FluidPipes.dll` is the runtime plugin. The adjacent `FluidPipes.xml` provides IDE documentation for API consumers. The example project builds as `PipeConsumer.dll`.

## Validation

The core suite includes one-million-node inventory benchmarks, randomized connectivity/conservation checks and concurrent graph operations. Runtime checks launch an isolated hidden game process, verify native Harmony integration and exit after writing `tests/runtime-results.txt`. They also require XML summaries and parameter documentation for every exported API member, and reject implementation types exposed outside the API namespace.

The scene harness creates an isolated sandbox and renders game cameras to offscreen textures. Production fixtures can export a native save and companion mod data. The validation environment supports one active game instance, so exit the current game before launching a harness process.

```powershell
pwsh -NoProfile -File tests/capture-scene.ps1 -ParallelLine
pwsh -NoProfile -File tests/capture-scene.ps1 -ParallelLine -InspectSave samples/parallel-fluid-line/parallel-fluid-line.dsv
```

The parallel fixture contains three plastic chemical plants, sandbox source/sink towers and parallel pipe/belt lanes. It checks stable production, count conservation, pipe-to-belt inserter transfer and native construction-bar selection. Other harness modes cover geometry, connection patterns, cube networks, valve interaction and inserter appearance.

Performance reports identify the runtime, workload and measured CPU stages for each result. Interactive frame-rate assessment requires a complete loaded factory and frame-level measurements. The validation environment's allocation counter reports zero for a known allocation, so current checks classify it as unavailable and verify physical cargo counts separately. See [measurements](../tests/PERFORMANCE.md).

## API maintenance

Public types belong in `FluidPipes.API`, except the BepInEx plugin entry point. Use PascalCase for public types, methods, properties and enum values; camelCase for parameters and private fields. Public API members carry XML summaries, parameter meanings, units, failure behavior and applicable lifetime/thread constraints. Internal comments document invariants and game API constraints.

Formatting uses the pinned local CSharpier tool and `.csharpierrc.json`. Restore it with `dotnet tool restore`, run `dotnet csharpier format .` to format, and `dotnet csharpier check .` to verify. Use explicit braces and one declaration per statement. Name gameplay variables by their domain roles; geometry may use conventional coordinate notation. Harmony hook argument names follow the native signatures because Harmony binds those names.

Keep the external facade independent of Harmony patch classes. New item insertion paths enforce admission at the shared inventory boundary. Cargo hooks return control to native methods for ordinary belt targets. Storage and topology changes retain save compatibility and conservation checks.

## Packaging

```powershell
pwsh -NoProfile -File package.ps1
```

The archive contains only `BepInEx/plugins/FluidPipes/FluidPipes.dll`. Dependencies, example plugins, saves, screenshots and verification output remain separate. `play.ps1 -PrepareOnly` prepares a local profile; `-ProfilePath` selects its destination.
