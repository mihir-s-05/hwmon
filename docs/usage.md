# Commands and examples

Examples use `hwmon` as though it is on PATH. Inside an extracted package use
`./hwmon` on Linux/WSL or `.\hwmon.exe` in Windows PowerShell instead.

## Look at readings

| Command | What it shows |
|---|---|
| `hwmon` | A quick overview of the machine |
| `hwmon gpu` | GPU overview |
| `hwmon cpu` | CPU overview |
| `hwmon sensors` | All currently readable channels |
| `hwmon sensors --all` | Also show unavailable, unsupported and failed channels |
| `hwmon devices` | Device IDs and available driver/firmware metadata |
| `hwmon diag` | Active diagnostic findings |
| `hwmon doctor` | Platform, library and access checks |

Use `--gpu`, `--cpu`, `--device storage`, or an exact ID from `devices` to filter.
For example:

```bash
hwmon sensors --gpu --all
hwmon devices --device storage
hwmon sensors --device nvidia/0 --json
```

Use `--json` when another program needs to consume the result. `--help` lists the
available commands without opening the hardware backends.

## Watch or record

```bash
hwmon watch --gpu --interval 1s
hwmon log gpu.csv --gpu --interval 1s --duration 30s
hwmon log gpu.jsonl --gpu --format jsonl --samples 20
hwmon export snapshot.json
```

`watch` prints repeated readings. `log` writes repeated readings to a file.
`export` saves one JSON snapshot. Ctrl+C stops a running watch or log; logs are
flushed after each sample.

Without `--duration` or `--samples`, a log keeps running. There is no licensing
timer. If you give both limits, collection stops when the first one is reached.

Intervals and durations accept `ms`, `s`, `m`, and `h`; the minimum interval is
100ms. Actual polling can take longer, especially with the WSL helper. An ongoing
query can finish after the requested duration. See `sampledAt` in JSON or CSV.

Output directories must already exist. Existing files are not overwritten unless
you add `--force`. See [logging formats](logging.md) for CSV layouts and JSON fields.

## Select a backend option

| Option | Use it when |
|---|---|
| `--native` | You want native Linux/WSL providers without the automatic Windows helper |
| `--windows-helper PATH` | You want to use a particular Windows release from WSL |
| `--no-rails` | You want to skip private NVIDIA rail and crossbar queries in the Windows backend/helper |

`--native` and `--windows-helper` are alternatives; do not combine them. The
Windows helper is available only in WSL. `--no-rails` also removes crossbar data
because that query lives in the same isolated worker.

## Compare with a HWiNFO log

```bash
hwmon coverage --reference HWiNFO.csv --json
```

This reads the NVIDIA column names and hardware footer from a HWiNFO CSV and
reports the matching channels. It does not use the CSV as a live sensor backend.
Mappings were verified for the RTX 4070 Laptop reference machine; they are not a
general accuracy guarantee for other hardware.

`hwmon self-test` runs the offline checks without needing a GPU or sensor driver.
