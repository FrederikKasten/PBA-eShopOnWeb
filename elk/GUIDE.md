# ELK Stack Setup Guide

A local Elasticsearch + Logstash + Kibana stack running in Docker.

## Why Kibana instead of Grafana

Both can show Elasticsearch data, but **Kibana is the easier choice here**:

| | Kibana | Grafana |
|---|---|---|
| Made for Elasticsearch | Yes, same vendor, same version | No, ES is one of many data sources |
| Browse/search raw logs | Excellent (*Discover*) | Basic (*Explore*) |
| Setup with ES | Zero config, just point it at ES | Add and configure a data source |
| Metrics dashboards from many sources | OK | Excellent |

The stack is about logs, so Kibana is the right fit. If you later want metrics
from Prometheus and others in one place, you can add Grafana next to Kibana.

## How the pieces fit together

```
your app ──JSON/TCP:5000──┐
                          ├──> Logstash ──> Elasticsearch ──> Kibana (browser :5601)
Filebeat ──Beats:5044─────┘   (parse/enrich)  (store/search)    (view/dashboards)
```

- **Elasticsearch** stores the log events and makes them searchable.
- **Logstash** receives events, cleans/enriches them, and writes them to Elasticsearch.
- **Kibana** is the web UI for searching logs and building dashboards.

## Files

Paths are relative to the project root. Run all `docker compose` commands from the project root.

| File | What it does |
|---|---|
| `.env` | Stack version, memory and ports in one place |
| `docker-compose.yml` | Defines the ELK containers (next to the eShop services) and how they connect |
| `logstash/pipeline/logstash.conf` | Logstash pipeline: inputs, filters, output |
| `elk/send-test-log.ps1` | Sends sample log events so you can test end to end |

---

## Step 1 - Prerequisites

You need **Docker Desktop** (with the WSL 2 backend on Windows) running.
Check it with:

```bash
docker compose version
```

Give Docker at least **4 GB of RAM** (Docker Desktop > Settings > Resources).
Elasticsearch uses 1 GB of heap plus overhead, Logstash ~512 MB, Kibana ~1 GB.

## Step 2 - Review the configuration

Open `.env`. All three components **must use the same version**, which is why
there is a single `STACK_VERSION`. Change ports here if any are already in use.

Open `docker-compose.yml`. Key points:

- `discovery.type=single-node`: run Elasticsearch as one node, no cluster.
- `xpack.security.enabled=false`: no passwords or TLS. This keeps local
  development simple, but **never use it on a server reachable from the
  network** (see Step 8).
- `es-data` volume: your indexed data survives `docker compose down`.
- `healthcheck` + `depends_on`: Logstash and Kibana wait until Elasticsearch
  is actually ready instead of crashing on startup.

## Step 3 - Start the stack

From the project root, start only the ELK services:

```bash
docker compose up -d elasticsearch logstash kibana
```

(`docker compose up -d` without names also builds and starts the eShop apps and SQL Server.)

The first run downloads ~2-3 GB of images. Watch progress with:

```bash
docker compose ps
```

Wait until `elasticsearch` shows `healthy` and all three show `running`
(about 1-2 minutes).

## Step 4 - Check each component

**Elasticsearch** - should return JSON with the cluster name and version:

```bash
curl http://localhost:9200
```

**Logstash** - look for `Pipelines running` in the logs:

```bash
docker compose logs logstash --tail 20
```

**Kibana** - open http://localhost:5601 in your browser. If it says
"Kibana server is not ready yet", wait another minute.

## Step 5 - Send test logs

```powershell
.\elk\send-test-log.ps1
```

This sends 5 JSON events to Logstash on port 5000. Confirm they arrived in
Elasticsearch:

```bash
curl "http://localhost:9200/app-logs-*/_count"
```

## Step 6 - View logs in Kibana

1. Open http://localhost:5601.
2. Go to **Menu (☰) > Discover**.
3. Click **Create data view**:
   - Name: `App logs`
   - Index pattern: `app-logs-*`
   - Timestamp field: `@timestamp`
4. Save. You now see your test events. Try searching `level : "ERROR"`.

A **data view** tells Kibana which indices to read; `app-logs-*` matches the
daily indices Logstash creates (`app-logs-2026.10.20`, etc.).

## Step 7 - Send logs from your own project

For eShopOnWeb specifically (Serilog setup, DI, and how to write log
messages), follow [LOGGING.md](LOGGING.md). The general options are:

**Option A - app sends directly to Logstash (simplest).**
Configure your logging library to write JSON lines to TCP `localhost:5000`.
Common choices:
- .NET: Serilog with `Serilog.Sinks.Network` (TCP + JSON formatter)
- Java: Logback with `logstash-logback-encoder` (`LogstashTcpSocketAppender`)
- Node.js: `pino` / `winston` with a TCP transport
- Python: `python-logstash-async`

Include fields like `timestamp`, `level`, `service`, `message`. The pipeline
already uses `timestamp` as the event time if present.

**Option B - app writes log files, Filebeat ships them (more robust).**
The app keeps logging to files; Filebeat tails them and sends them to
Logstash on port 5044. This survives Logstash restarts without losing logs
and needs no code changes. Ask and I can add a Filebeat service to the
compose file.

## Step 8 - Before going beyond local dev

- Turn security on (`xpack.security.enabled=true`), set passwords, and enable TLS.
- Size the heap (`ES_HEAP`) to about half the container's memory.
- Use more than one Elasticsearch node for redundancy.
- Add an **index lifecycle policy** (Kibana > Stack Management > Index
  Lifecycle Policies) so old daily indices get deleted automatically.

---

## Everyday commands

| Task | Command |
|---|---|
| Start | `docker compose up -d` |
| Stop (keep data) | `docker compose down` |
| Stop and **delete all data** | `docker compose down -v` |
| Follow logs | `docker compose logs -f logstash` |
| Reload pipeline after editing `logstash.conf` | `docker compose restart logstash` |

## Troubleshooting

- **Elasticsearch exits with `vm.max_map_count` error**: run
  `wsl -d docker-desktop sysctl -w vm.max_map_count=262144` and start again.
- **Port already in use**: change the port in `.env`.
- **No data in Kibana**: check the time picker (top right) covers "Last 15
  minutes", and check `docker compose logs logstash` for errors.
- **Containers keep restarting**: usually not enough memory; raise Docker's RAM.
