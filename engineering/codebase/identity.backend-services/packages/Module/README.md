# WoW2.Sdk.Beta.Be.Identity.Module

Form 1 of the Identity capability: `IGreetingsBroker` in the consumer's process, over the consumer's database. The
consumer owns the tables and applies the embedded `Migrations/NNN-name/{Apply,Rollback}.sql` with the be sdk migrator.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Module --prerelease
```

```csharp
services.AddIdentity().Configure(options => options.ConnectionString = "Data Source=app.db");
services.AddDatabaseBespokeMigrations(typeof(IdentityModuleAssembly).Assembly, migrations => { /* provider, version */ });
```
