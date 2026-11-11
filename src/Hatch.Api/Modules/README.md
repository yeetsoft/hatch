# Modules

[`Modules/Hatch`](Hatch) is the API's one module: its own folder, its own
Postgres schema (`hatch`), its own migration history. It rides the existing pod,
deploy, backup and log pipeline. What it does is in
[`docs/hatch.md`](../../../docs/hatch.md).

## How it is wired

- **One DI entry point.** [`HatchModule`](Hatch/HatchModule.cs) registers the
  context, the services and the options (`HatchOptions`, section `Hatch`,
  validated on start), so `Program.cs` carries one line for it.
- **The context** is registered through
  [`AddModuleContext`](ModuleRegistration.cs), which pins its migration history
  table into the module's schema. `HatchContext` declares its `Schema` once, as a
  `const`, and the registration and
  [`HatchDesignTimeFactory`](Hatch/HatchDesignTimeFactory.cs) both read it.
  Three copies of the string is how a module ends up with its history table in
  the wrong schema.
- **Migrations** live in `Modules/Hatch/Migrations`, scaffolded with:

  ```sh
  dotnet ef migrations add <Name> --context HatchContext \
    --project ./src/Hatch.Api/Hatch.Api.csproj -o Modules/Hatch/Migrations
  ```

  Startup migrates every registered `IModuleContext` automatically.
- **Controllers** are found anywhere in the assembly by `AddControllers`. Each
  one in Hatch carries [`RequireRole`](../Common/RequireRoleAttribute.cs), and
  setting `AcceptKey` on it (`[RequireRole(PersonRole.User, AcceptKey = true)]`)
  is what opens a route to an API key — see
  [`docs/hatch.md`](../../../docs/hatch.md), "The wall, the admin gate, and API
  keys".

## Rules

- **The `hatch` schema is the module's.** `Ef/AppDbContext.cs` and its `public`
  schema belong to the core. A Hatch migration must never appear in
  `Migrations/` or in `public.__EFMigrationsHistory`.
- **Ask who is calling through
  [`ICallerIdentity`](../Services/Auth/CallerIdentity.cs)** — one constructor
  parameter, resolved once per request — and never by reading the cookie for
  yourself. A person's role is read in one place,
  `Services/Auth/RoleGate.cs`, and every controller carries a role attribute
  for the gate to answer.
- **Nothing operator-specific in module code** — domains, hostnames and paths
  come from config, per [`docs/ethos.md`](../../../docs/ethos.md). The install's
  own public URL is already a setting:
  [`HatchOptions.PublicBaseUrl`](Hatch/HatchOptions.cs) (`Hatch:PublicBaseUrl`).
  Read it from there rather than adding a second setting for the same fact.
