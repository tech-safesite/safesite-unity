# PingService (template)

**Not a feature.** This is the reference module to copy when you add a new service. Don't use it,
repurpose it or delete it.

## Making a new module from it

1. In the Project window, select `Assets/Modules/PingService/` and **Edit > Duplicate**, then rename
   the copy to `<Name>Service`. (Duplicating inside Unity gives the copies new GUIDs; copying in
   Explorer with the `.meta` files would clash.)
2. Rename everything: `IPingService` → `I<Name>Service`, `PingServiceLocator` →
   `<Name>ServiceLocator`, `PingService` → `<Name>Service`, namespaces `Modules.PingService.*` →
   `Modules.<Name>Service.*`, asmdefs `Modules.PingService.External/Internal` →
   `Modules.<Name>Service.External/Internal` (asmdef `name` field and file name).
3. Replace the ping API with your real contract.
4. Put the `Internal` component on a loaded scene object (for app services: Bootstrap's
   `--Singletons / Persistant --/SafeSite Services`).

## The rules it demonstrates

```
External/                       references nothing
  I<Name>Service.cs             operations, state getters, events only
  <Name>ServiceLocator.cs       FindInScene(): scans loaded MonoBehaviours for the interface
  DTOs (PingResult.cs)
  Modules.<Name>Service.External.asmdef

Internal/                       references its own External + other modules' External only
  <Name>Service.cs              [DisallowMultipleComponent] MonoBehaviour : I<Name>Service
  Modules.<Name>Service.Internal.asmdef
```

- Reference other assemblies **by name**, never by GUID.
- Never reference another module's `Internal` from production code (tests may).
- Parsing, networking, prefabs and Unity wiring stay in `Internal`.
- No `static Instance` singletons. Log lines start with `[<Name>Service]`.
- A service exists only when its component is on a loaded scene object; the script existing on
  disk does nothing.
- App scripts in `Assembly-CSharp` see auto-referenced `External` assemblies automatically.

See [docs/ARCHITECTURE.md § 4](../../../../docs/ARCHITECTURE.md#4-modules).
