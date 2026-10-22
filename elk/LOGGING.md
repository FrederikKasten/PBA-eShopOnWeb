# Logging from eShopOnWeb to the ELK Stack

How to get log messages from the **Web** and **PublicApi** apps into
Elasticsearch, and how to write good log messages in the code.

The ELK containers themselves are covered in [GUIDE.md](GUIDE.md). This guide
picks up once `docker compose up -d elasticsearch logstash kibana` works.

## The big picture

```
Your code                         ASP.NET Core             Serilog                 Docker
─────────────────────────         ────────────             ───────                 ──────
_logger.LogInformation(...) ───>  ILogger<T>  ───────────> Console sink  ────────> terminal / VS output
(ILogger<T> or IAppLogger<T>)     (already in DI)          TCP sink (JSON) ──────> Logstash :5000
                                                                                     │
                                                                                     v
                                                                               Elasticsearch
                                                                                     │
                                                                                     v
                                                                               Kibana :5601
```

- **Your code doesn't change** how it logs. It keeps calling `ILogger<T>` /
  `IAppLogger<T>`, which it already does today.
- **Serilog** is a logging library that plugs in *behind* `ILogger<T>`. It
  decides where messages go: the console and Logstash.
- **Logstash, Elasticsearch and Kibana** are separate programs in their own
  containers. The app only knows Logstash's address.

## Do I need to register anything in IoC?

| Thing | Register in DI? | Why |
|---|---|---|
| Elasticsearch | **No** | Separate server. The app never talks to it directly. |
| Logstash | **No** | Separate server. The app only needs its address, which goes in `appsettings.*.json`. |
| Kibana | **No** | A web UI you open in the browser. |
| Serilog | **Yes, once per app**, with `builder.Host.UseSerilog(...)` | Replaces the default logging providers behind `ILogger<T>`. |
| `ILogger<T>` | **No**, already done | ASP.NET Core registers it automatically. |
| `IAppLogger<T>` | **No**, already done | Already registered in [ConfigureCoreServices.cs:25](../src/Web/Configuration/ConfigureCoreServices.cs) and [PublicApi/Program.cs:45](../src/PublicApi/Program.cs). |

So the only DI change is one `UseSerilog` call in each app's `Program.cs`
(Step 5).

---

## Step 1: Elasticsearch, no changes needed

The `docker-compose.yml` setup is already enough for development:
single-node, security off, data kept in the `es-data` volume.
Elasticsearch creates the `app-logs-YYYY.MM.dd` indices automatically the
first time Logstash writes to them.

Check it's running:

```bash
curl http://localhost:9200
```

## Step 2: Logstash, no changes needed

[logstash/pipeline/logstash.conf](../logstash/pipeline/logstash.conf) already
matches what the app will send:

- **Input:** `tcp { port => 5000 codec => json_lines }` takes one JSON object
  per line. This is exactly what Serilog's TCP sink writes.
- **Filter:** the sink sends a `timestamp` field, and the `date` filter turns
  it into `@timestamp`, so Kibana shows the time the event *happened*, not
  when Logstash received it.
- **Output:** writes to Elasticsearch in daily `app-logs-*` indices.

The JSON each log message becomes looks like this. Every property in the
message template becomes its own field:

```json
{
  "timestamp": "2026-10-22T10:15:03.123+02:00",
  "level": "Information",
  "message": "Updating quantity of item 7 to 3",
  "itemId": 7,
  "quantity": 3,
  "sourceContext": "Microsoft.eShopWeb.ApplicationCore.Services.BasketService",
  "application": "Web"
}
```

> **Field names are camelCased.** The sink lowercases the first letter of
> every property, so `{ItemId}` in your code becomes the field `itemId`,
> and `Application` becomes `application`. Use the camelCase names when
> searching in Kibana.

If you change the pipeline, reload it with:

```bash
docker compose restart logstash
```

## Step 3: Kibana, one-time setup in the browser

Kibana needs no config files. Once logs are arriving (after Step 7):

1. Open http://localhost:5601 → **☰ Menu → Discover**.
2. **Create data view**: name `App logs`, index pattern `app-logs-*`,
   timestamp field `@timestamp`.
3. In the field list on the left, click **+** next to `level`, `application`,
   `sourceContext` and `message` to show them as columns.

## Step 4: Add the Serilog packages

The solution uses **central package management**, so versions go in
[Directory.Packages.props](../Directory.Packages.props) and the `.csproj`
files only name the package.

**4a.** In `Directory.Packages.props`, add these inside the `<ItemGroup>`
with the other `<PackageVersion>` lines:

```xml
<PackageVersion Include="Serilog.AspNetCore" Version="8.0.3" />
<PackageVersion Include="Serilog.Sinks.Network" Version="3.0.0" />
```

> Use **8.0.3**, not the newest 10.x. Serilog.AspNetCore 10.x requires
> Microsoft.Extensions 10.x packages, while the project pins them to 8.0
> (`SystemExtensionVersion`), which can lead to version conflicts. 8.0.3
> needs only the 8.0 packages the project already uses.

**4b.** In **both** `src/Web/Web.csproj` and `src/PublicApi/PublicApi.csproj`,
add to the `<ItemGroup>` with the other `<PackageReference>` lines:

```xml
<PackageReference Include="Serilog.AspNetCore" />
<PackageReference Include="Serilog.Sinks.Network" />
```

- `Serilog.AspNetCore` contains Serilog itself, the console sink, the
  `UseSerilog()` hook, request logging, and reading settings from
  `appsettings.json`.
- `Serilog.Sinks.Network` adds the TCP sink that sends JSON to Logstash.

Don't add Serilog to `ApplicationCore` or `Infrastructure`. They log
through `IAppLogger<T>` / `ILogger<T>` and don't need to know Serilog exists.

## Step 5: Register Serilog in `Program.cs` (the IoC step)

Do this in **both** `src/Web/Program.cs` and `src/PublicApi/Program.cs`.

**5a.** Add the using at the top:

```csharp
using Serilog;
```

**5b.** Replace the existing `builder.Logging.AddConsole();` line with:

```csharp
// Send all ILogger<T> output through Serilog, configured from the "Serilog"
// section of appsettings.*.json (console + Logstash).
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());
```

Remove `AddConsole()`: once Serilog is in charge it replaces the built-in
providers, and the console output comes from Serilog's console sink instead.

**5c.** Log one summary line per HTTP request (method, path, status code,
duration). This is optional but very useful. Add it after `builder.Build()`:

- **Web:** directly after `app.UseStaticFiles();`, so CSS/JS/image requests
  aren't logged:
  ```csharp
  app.UseStaticFiles();
  app.UseSerilogRequestLogging();
  app.UseRouting();
  ```
- **PublicApi:** directly after `app.UseHttpsRedirection();`:
  ```csharp
  app.UseHttpsRedirection();
  app.UseSerilogRequestLogging();
  app.UseRouting();
  ```

## Step 6: Configure where logs go in `appsettings.*.json`

Serilog reads the `"Serilog"` section. Once Serilog is in charge, the old
`"Logging"` section is **ignored**, so you can delete it or leave it.

The only thing that differs between environments is **Logstash's address**:

| How the app runs | `ASPNETCORE_ENVIRONMENT` | Settings file | Logstash address |
|---|---|---|---|
| From Visual Studio / `dotnet run` | `Development` | `appsettings.Development.json` | `tcp://localhost:5000` |
| In Docker Compose | `Docker` (set in `docker-compose.override.yml`) | `appsettings.Docker.json` | `tcp://logstash:5000` |

Inside Docker, `localhost` means *the container itself*. Containers reach
each other by **service name**, which is why it's `logstash` there.

**6a.** In `appsettings.json` of **both** apps, add the shared settings. Set
`Application` to `"Web"` or `"PublicApi"` so you can tell the two apart in
Kibana:

```json
"Serilog": {
  "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.Network" ],
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "System": "Warning"
    }
  },
  "WriteTo": [
    { "Name": "Console" }
  ],
  "Properties": {
    "Application": "Web"
  }
}
```

**6b.** In `appsettings.Development.json` of both apps, add the Logstash sink
for local runs:

```json
"Serilog": {
  "WriteTo": [
    { "Name": "Console" },
    { "Name": "TCPSink", "Args": { "uri": "tcp://localhost:5000" } }
  ]
}
```

**6c.** In `appsettings.Docker.json` of both apps:

```json
"Serilog": {
  "WriteTo": [
    { "Name": "Console" },
    { "Name": "TCPSink", "Args": { "uri": "tcp://logstash:5000" } }
  ]
}
```

Arrays like `WriteTo` are merged **by position**, not replaced. That's why
`Console` is listed again in 6b and 6c: it keeps entry 0 the same and adds
entry 1.

**What the levels mean:** `MinimumLevel.Default: Information` keeps your own
`Information` and higher. The `Override` entries hide the very chatty
framework logs from `Microsoft.*` / `System.*` unless they're warnings or
errors. To debug something, change `Default` to `Debug` temporarily.

## Step 7: Run it and check the logs arrive

1. Start ELK if it isn't running:
   ```bash
   docker compose up -d elasticsearch logstash kibana
   ```
2. Start the app, either from Visual Studio or with
   `docker compose up -d --build eshopwebmvc eshoppublicapi`.
3. Click around the shop: browse the catalog, add something to the basket.
4. Check that documents arrived:
   ```bash
   curl "http://localhost:9200/app-logs-*/_count"
   ```
5. Open Kibana → **Discover** (create the data view from Step 3 if you
   haven't yet).

If nothing arrives, run `docker compose logs logstash --tail 50` and check
that the address in Step 6 matches how you're running the app.

---

## Step 8: Writing to the logs

### Which logger to inject

| Where the class lives | Inject | Why |
|---|---|---|
| `Web`, `PublicApi` (controllers, pages, endpoints, middleware) | `ILogger<T>` | Standard ASP.NET Core; has every log level, exceptions and scopes. |
| `ApplicationCore` (services like `BasketService`) | `IAppLogger<T>` | ApplicationCore deliberately doesn't depend on Microsoft logging packages. `IAppLogger<T>` is its own interface; `LoggerAdapter<T>` in Infrastructure forwards it to `ILogger<T>`. |

Both end up in the same place: Serilog → console + Logstash. `T` is the class
doing the logging; it appears in Kibana as `sourceContext`.

### Example: a controller or endpoint (`ILogger<T>`)

```csharp
public class OrderController : Controller
{
    private readonly ILogger<OrderController> _logger;

    public OrderController(ILogger<OrderController> logger) // injected by DI
    {
        _logger = logger;
    }

    public async Task<IActionResult> Detail(int orderId)
    {
        _logger.LogInformation("Showing order {OrderId} for {UserName}", orderId, User.Identity?.Name);

        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found", orderId);
            return NotFound();
        }

        return View(order);
    }
}
```

### Example: an ApplicationCore service (`IAppLogger<T>`)

`BasketService` already has `IAppLogger<BasketService> _logger` injected.
Use it the same way:

```csharp
_logger.LogInformation("Updating quantity of item {ItemId} to {Quantity}", item.Id, quantity);
```

### The most important rule: message templates, not string interpolation

```csharp
// ✗ Wrong: interpolation bakes the values into the text
_logger.LogInformation($"Updating quantity of item ID:{item.Id} to {quantity}.");

// ✓ Right: a template with named placeholders, values passed separately
_logger.LogInformation("Updating quantity of item {ItemId} to {Quantity}", item.Id, quantity);
```

Both print the same text, but only the template version sends `itemId` and
`quantity` to Elasticsearch as **separate, searchable fields**. In Kibana
you can then search `itemId : 7`, or chart quantities. The `$"..."`
version only gives you one blob of text.

The first line above is real code in
[BasketService.cs](../src/ApplicationCore/Services/BasketService.cs).
Converting lines like that is an easy win.

Placeholder names should be **PascalCase** and **consistent**: always
`{OrderId}`, never `{orderId}` in one place and `{Id}` in another. They
become field names in Elasticsearch, camelCased (`{OrderId}` → `orderId`).

### Picking a log level

| Level | Use for | Example |
|---|---|---|
| `LogTrace` / `LogDebug` | Detailed info for debugging; hidden by default | `"Cache miss for {CacheKey}"` |
| `LogInformation` | Normal, meaningful events in the business flow | `"Order {OrderId} placed by {BuyerId}"` |
| `LogWarning` | Something unexpected, but the app carries on | `"Payment retry {Attempt} for order {OrderId}"` |
| `LogError` | An operation failed | `"Failed to place order for basket {BasketId}"` |
| `LogCritical` | The app or a key dependency is down | `"Cannot connect to the catalog database"` |

### Logging exceptions

Pass the exception as the **first** argument. Serilog then sends the type,
message and full stack trace as fields:

```csharp
try
{
    await _orderService.CreateOrderAsync(basketId, address);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to create order for basket {BasketId}", basketId);
    throw;
}
```

Don't do `_logger.LogError(ex.Message)`. You lose the stack trace and the
exception type.

### Adding context to several log lines at once (scopes)

Every log line written inside a `BeginScope` block gets the scope's
properties. This works because of `.Enrich.FromLogContext()` in Step 5:

```csharp
using (_logger.BeginScope(new Dictionary<string, object> { ["BasketId"] = basketId }))
{
    _logger.LogInformation("Starting checkout");
    // ... every log line in here, including from other classes, gets BasketId
    _logger.LogInformation("Checkout complete");
}
```

In Kibana, `basketId : 42` then shows the whole checkout story.

### Never log secrets or personal data

No passwords, tokens, connection strings, full card numbers or similar.
Logs are kept and searched by everyone with Kibana access. Log an ID, not
the whole object.

### Optional: add `LogError` to `IAppLogger<T>`

`IAppLogger<T>` only has `LogInformation` and `LogWarning` today, so
ApplicationCore services can't log errors with it. To add that:

[src/ApplicationCore/Interfaces/IAppLogger.cs](../src/ApplicationCore/Interfaces/IAppLogger.cs):

```csharp
void LogError(Exception exception, string message, params object[] args);
```

[src/Infrastructure/Logging/LoggerAdapter.cs](../src/Infrastructure/Logging/LoggerAdapter.cs):

```csharp
public void LogError(Exception exception, string message, params object[] args)
{
    _logger.LogError(exception, message, args);
}
```

No DI change is needed. The existing `IAppLogger<>` registration covers it.

---

## Searching logs in Kibana

In **Discover**, type in the search bar (KQL syntax):

| Find | Query |
|---|---|
| All errors | `level : "Error"` |
| Errors from the API only | `level : "Error" and application : "PublicApi"` |
| Everything about one order | `orderId : 1234` |
| Logs from one class | `sourceContext : *BasketService` |
| Slow requests (from request logging) | `elapsed > 1000` |
| Failed requests | `statusCode >= 500` |
| Text search | `message : "checkout"` |

Set the time range in the top right, and click **Save** to keep a search for
later. **Dashboards** let you build charts from these, e.g. errors per hour
per `Application`.

## Common problems

| Symptom | Likely cause / fix |
|---|---|
| Logs in the console but not in Kibana | Wrong Logstash address for how you're running the app (Step 6 table). Check `docker compose logs logstash`. |
| Nothing in Kibana, `_count` shows documents | Time range in Kibana is too short, or the data view pattern isn't `app-logs-*`. |
| Your `LogInformation` lines don't appear | `MinimumLevel.Default` is `Warning`. Set it to `Information` (Step 6a). |
| Placeholders show up as text, no fields | You used `$"..."` interpolation instead of a template. |
| Build errors/warnings about package versions (e.g. `NU1605` downgrade) | Probably Serilog.AspNetCore 10.x pulling in Microsoft.Extensions 10.x. Use 8.0.3 (Step 4). |
| `Name or service not known` errors at startup | The app started before the Logstash container existed. Fine: the sink retries (after 1s, 3s, 7s, 15s, 31s, …) and sends the queued messages once connected, so the first logs can show up in Kibana up to a minute late. `depends_on: logstash` in `docker-compose.yml` avoids most of this. |
| Searching `Application : "Web"` finds nothing | Field names are camelCased: use `application`. |

BlazorAdmin runs in the **browser** (WebAssembly), so it can't open a TCP
connection to Logstash. Its logs stay in the browser console. If you need
them centrally, send them to an API endpoint that logs them server-side.
