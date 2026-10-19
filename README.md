# dataverse-demo

A small volunteer-roster backend on Dataverse: a C# plugin that validates shifts as they're saved, and an Azure Function that sends shift reminders. I built it to learn the pro-code side of Power Platform, coming from Rails and React.

## What's here

| Project | Target | What it does |
|---|---|---|
| `src/Roster.Plugins` | .NET Framework 4.6.2 | `ValidateShiftPlugin`: rejects shifts that end before they start, run over 8 hours, or double-book a volunteer, and sets the shift's duration. |
| `src/Roster.Functions` | .NET 10, isolated worker | `ShiftReminders`: a daily timer that emails volunteers about shifts in the next 24 hours, then flags each shift so it isn't reminded twice. |
| `tests/*` | xUnit + NSubstitute | Unit tests for both, with no live Dataverse needed. |

## Design notes

- **Pre-operation, synchronous plugin.** Validation errors roll back the save and show the user a message. The duration is set on the `Target`, so no extra update call is needed and there's no risk of the plugin re-triggering itself.
- **Pre-image on Update.** On Update, `Target` only holds the changed columns. The plugin fills the rest from a pre-image, so moving only the end time still validates against the saved start.
- **Filtering attributes.** The plugin only runs when the start, end or volunteer changes. If it runs anyway (e.g. registered without filters), it returns early without querying.
- **Overlap check excludes the shift itself.** Otherwise moving a shift would clash with its own saved row.
- **Idempotent reminders.** `cr_remindersent` stops duplicate sends between runs. One failed send doesn't block the rest. If sending works but the flag update fails, the next run resends: at-least-once delivery, by choice.
- **No secrets.** The function authenticates to Dataverse with `DefaultAzureCredential`: managed identity in Azure, your `az login` locally.
- **Dependencies.** `Microsoft.PowerPlatform.Dataverse.Client` pulls in vulnerable `System.Security.Cryptography.*` versions transitively. These are pinned to patched versions.

### Rails parallels

| Dataverse / Azure | Rails equivalent |
|---|---|
| Plugin on PreOperation | `before_save` callback + validation |
| `InvalidPluginExecutionException` | `errors.add` / `ActiveRecord::RecordInvalid` |
| Pre-image | `attribute_was` / dirty tracking |
| Timer-triggered Function | Scheduled Sidekiq job |
| `QueryExpression` with `LinkEntity` | ActiveRecord `joins` + `where` |

## Running it

```sh
dotnet test
```

Running the tests needs Windows, because the plugin tests run on .NET Framework 4.8.1. CI runs on `windows-latest`.

### Dataverse setup

1. Get a free [Power Apps Developer Plan](https://www.microsoft.com/power-platform/products/power-apps/free) environment.
2. Create a solution and a **Shift** table (`cr_shift`). Replace the `cr_` prefix in `Shift.cs` with your publisher's prefix.

   | Column | Type |
   |---|---|
   | `cr_name` | Text (primary) |
   | `cr_start`, `cr_end` | Date and time |
   | `cr_volunteer` | Lookup → Contact |
   | `cr_durationhours` | Decimal |
   | `cr_remindersent` | Yes/No, default No |

3. Build in Release, then register `Roster.Plugins.dll` with the Plugin Registration Tool (`pac tool prt`):
   - Steps: **Create** and **Update** of `cr_shift`, PreOperation, synchronous.
   - Update step: filtering attributes `cr_start, cr_end, cr_volunteer`, plus a pre-image named `PreImage` with those columns.

The `.snk` signing key is committed on purpose. Dataverse requires strong-named plugin assemblies, but strong naming is about identity, not security.

### Function

1. Copy `src/Roster.Functions/local.settings.sample.json` to `local.settings.json` and set `DataverseUrl`.
2. Run `az login` with an account that has access to the environment.
3. Run it with `func start`; you'll need Azure Functions Core Tools and Azurite.

When deploying to Azure, create an application user in the environment for the Function App's managed identity, with a security role that can read shifts and contacts and update shifts.

## Next steps

- Register the plugin and Function in a real environment, then add screenshots here.
- Package as a solution and deploy through Power Platform pipelines or the `pac` CLI in GitHub Actions.
- Replace `LoggingNotifier` with Azure Communication Services email.
- A Power Automate flow or PCF control for comparison with the pro-code approach.
