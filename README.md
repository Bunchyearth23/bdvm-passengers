# BDVM - Passengers

`BDVM.Passengers` adds passenger demand and passenger-service contracts to the shared BDVM operations model.

## Status

| Property | Value |
| --- | --- |
| Module kind | Optional operations feature |
| Target framework | .NET Framework 4.8 (`net48`) |
| Required modules | `BDVM.Common`, `BDVM.Operations` |
| Standalone install | Not yet |
| Current runtime host | `BDVM.Full` |

## Responsibilities

- Represent route demand, service windows, capacity and economic value.
- Generate bounded passenger-service offers from demand rather than unlimited rolling-stock spawns.
- Reserve, activate, complete and cancel passenger contracts deterministically.
- Hand active work to the shared mission-assignment layer in Operations.
- Preserve explicit completion-pending states while the game or host confirms delivery.
- Validate demand and contract transitions before mutating authoritative state.

## Key surfaces

`PassengerEconomyEngine` operates on `PassengerRouteDemand` and `PassengerServiceContract`. `PassengerEconomyValidation` protects invariants, while `PassengerContractState` makes the lifecycle explicit from offer through completion or cancellation.

## Boundaries

Passengers does not spawn coaches, own railway assets, calculate company balances or complete a mission by itself. Fleet handles equipment, Companies handles money, and Operations handles assignments and completion ports. The module is optional: freight operations can exist without it.

## Dependencies and composition

The project references `BDVM.Common` and `BDVM.Operations`. Its `Domain/` files remain in this repository but are currently linked into `BDVM.Full`; the standalone DLL only carries the module marker until independent packaging is completed.

## Build

Keep Common and Operations beside this repository under `src/`, then run:

```powershell
dotnet build .\BDVM.Passengers.csproj -c Release
```

Build `BDVM.Full` to include the passenger domain in the present game runtime.

## Testing and installation

The domain validation suite exercises demand limits, reservation conflicts, lifecycle transitions and integration with mission assignments. There is no standalone Unity Mod Manager package yet. In-game testing uses the matching `BDVM.Full` build.

## Compatibility

Persisted route and contract identifiers must remain stable. Completion must be host-authoritative and retry-safe. A missing route, asset or completion adapter causes refusal or a pending state rather than invented success.

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE).
