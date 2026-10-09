# Logs and reading status

Choose long CSV when you want a spreadsheet-friendly record of all channels.
Choose JSONL when another program needs the complete snapshot, including diagnostics.
Choose wide CSV when you want one row per poll for charting.

## Long CSV: the default

```bash
hwmon log run.csv --gpu --interval 1s --duration 30s
```

There is one row per sensor per poll. The file is UTF-8 with invariant decimal
points and normal CSV quoting. The columns are:

```text
timestamp,deviceId,device,metric,unit,value,minimum,maximum,average,samples,status,rawValue,source,id,note,sampledAt
```

Both CSV layouts include a UTF-8 byte order mark (BOM), so Windows PowerShell 5.1
recognizes characters such as `°C` when reading the file with `Get-Content`.
When opening a CSV in Python, use `encoding='utf-8-sig'` to keep the BOM out of
the first column name. For older logs without a BOM, use `Get-Content -Encoding UTF8`.

`timestamp` is when the complete snapshot was assembled. `sampledAt` records the
backend's collection time, or the original time for a host/cached reading. These
can differ when several backends are polled sequentially.

Long CSV can accommodate channels appearing or disappearing. It is not a
byte-compatible replacement for HWiNFO's CSV format.

## Wide CSV

```bash
hwmon log chart.csv --gpu --wide --samples 20
```

This writes one row per poll with value, status and raw-value columns for each
initial sensor ID. The initial inventory becomes the header. If a new channel
appears, logging stops with an explanation instead of silently dropping it.
Use long CSV or JSONL when device inventories may change.

## JSON and JSONL

```bash
hwmon export snapshot.json
hwmon log run.jsonl --gpu --format jsonl --duration 1m
hwmon watch --gpu --json --samples 5
```

`export` writes one formatted JSON document. JSONL logs and JSON watch output
write one complete snapshot per line. Keys use camelCase. Each snapshot contains
devices, sensors, diagnostics and backend initialization status. Logging progress
goes to stderr, so it does not contaminate JSON output.

JSON and JSONL files use UTF-8 without a BOM.

## Understand `status`

| Status | Meaning |
|---|---|
| `ok` | The reading was accepted |
| `unsupported` | The driver/interface does not expose this query |
| `unavailable` | No usable measurement is available right now |
| `error` | The query failed; check its note |
| `permission` | Additional access is needed |
| `invalid` | A quality rule rejected the value; raw data and explanation remain |
| `cached` | An older result is being reused; check its sample time |

A missing or rejected current value is `null` in JSON and blank in CSV. That is
different from a valid zero. Boolean sensor values are numeric 0/1 in exported data.

## Statistics and raw values

Minimum, maximum and average are calculated per sensor ID, from accepted samples
in the current process. The average is a sample mean, not a time-weighted mean.
The warmup poll is discarded. Failed, invalid and cached readings do not add to
the accepted sample count. Previous good statistics can remain visible when a
current query fails; they do not make the current value valid.

Sources remain separate. For example, WSL native NVML and Windows-host NVML can
report the same physical quantity at different times; their values are not merged.

`rawValue` is backend-specific. Private rail integers use mW/mA/µV; NVAPI clock
integers use kHz; normalized memory-clock raw values use MHz. When a quality rule
rejects a scaled reading, it stores that rejected value in the displayed unit.
Read `source`, `unit` and `note` together rather than assuming one raw unit.

The power rule flags readings above 150% of the reported maximum, addressing an
observed driver anomaly. It is a plausibility check, not electrical calibration.
Non-finite values are represented as null with an explanation.
