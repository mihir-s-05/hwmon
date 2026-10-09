# NVIDIA rails and diagnostic reconstruction

Scope: read-only API/immutable-file research for HWiNFO64 8.16 GPU sensor parity.
No HWiNFO process injection, patching, licensing changes, driver installation,
GPU-setting changes, or GUI automation occurred. The implemented worker queries
the installed NVIDIA driver directly. Its parent must invoke it as an isolated
child process and quarantine it after a crash or timeout.

## Implemented and tested

`../hwmon-source/Backends/NvidiaRailsWorker.cs` contains `NvidiaRailsWorker.Run()`.
It writes one JSON `List<SensorReading>` to stdout. Test project
`RailsProbe.csproj` links the exact production module and `Models.cs`.

Five independent process invocations on the installed 617.14 driver exited 0
within the 8 second timeout. The final build had 0 errors/0 warnings. The final
capture is `probe-final.json`, and stderr is empty. Example final measurements:

| Channel | Rail | Power W | Notes |
|---|---|---:|---|
| 0 | Total board input (245) | 14.294 | Driver-provided summation |
| 1 | NVVDD input (246) | 4.724 | Driver-provided summation |
| 3 | FBVDD input (247) | 6.570 | Client-aligned sensor |
| 5 | 16-pin HVPWR (218) | 11.294 | Name established from HWiNFO code |
| 10 | PCIe 3.3V input (254) | 3.000 | P-state estimation LUT, not direct measured power |
| 12 | PP source input (241) | 9.570 | Driver-provided summation |
| 18 | NVVDD output (1) | 3.582 | Separate channel identity retained |
| 19 | NVVDD output (1) | 0 | Separate channel identity retained |

Crossbar clock returned 1845 MHz from raw 1845000 kHz. PCI metadata: bus 1,
slot 0. Identity: NVIDIA GeForce RTX 4070 Laptop GPU.

Important data quality observations:

- First and fourth invocations returned exactly 590.006 W total power, the
  same implausible value present in the prior HWiNFO log. Other input rails
  simultaneously returned high values (NVVDD 436.757 W, FBVDD 150.249 W,
  HVPWR 587.006 W). These are raw driver observations, not valid power
  measurements. Parent quality checks must preserve raw values and mark
  suspicious readings; API success alone is not measurement validity.
- Channel 18 current sometimes returned 0xFFFFFFFF. Worker preserves raw
  4294967295 and exposes null/unavailable rather than 4,294,967 A.
- Values at zero, including channel 19 power/voltage/current, are preserved.
  They may mean no activity or inaccessible telemetry; no new interpretation
  has been established. No load test was performed.
- This bounded test validates successful ABI use on this host, not every
  driver/GPU or long-term stability. The optional child boundary remains needed.

## Source-backed ABI

Primary implementation source:
[JustAMan/pynvraw nvapi_api.py](https://github.com/JustAMan/pynvraw/blob/9a860c547256cfc6f527bf4be3aeeed27d95fd85/src/pynvraw/nvapi_api.py).
Clone retained at `pynvraw/`, commit `9a860c547256cfc6f527bf4be3aeeed27d95fd85`.

- `NvAPI_GPU_PowerMonitorGetInfo`: QueryInterface ID `0xC12EB19E`.
  Version `0x00030CA8`, size 3240 bytes (v3). `isSupported` is byte +4;
  channelMask is uint32 +16. Channel array begins +40; 32 channels,
  60 bytes each. Per channel type is +12, rail enum +16.
- `NvAPI_GPU_PowerMonitorGetStatus`: ID `0xF40238EF`.
  Version `0x0001059C`, size 1436 bytes (v1). Caller supplies info.channelMask
  at +4. Entries begin +28; 32 entries, 44 bytes each (packed).
  Power average +0 in mW, current +12 in mA, voltage +16 in microvolts.
- `NvAPI_GPU_GetAllClocks`: ID `0x1BD69F49`, `NV_GPU_CLOCKS_INFO` v2,
  uint32 version plus 288 uint32 values, size 1156 / 0x484. Exact crossbar
  interpretation was corroborated with immutable HWiNFO code below.
- Only read-only GPU queries, NVAPI initialization/enumeration/unload occur.
  No power/fan/overclock setter is bound or called.
- Python AST execution of class declarations only, with no NVAPI DLL loading
  or API calls, independently measured the ctypes layouts/offsets and sizes.

[M-Control v1.3.49 release](https://github.com/Sakura-Lhy0409/M-Control-Releases/releases/tag/v1.3.49)
reports private PowerMonitor access violations in nvapi64_impl.dll for NVIDIA
600-series drivers and disables those calls in that project. That upstream
observation motivated isolation; it was not reproduced by this host's probe.

## Immutable HWiNFO binary evidence

Analysis target: `../HWiNFO64-analysis.exe`, an existing unpacked analysis copy.
SHA256: `b8c405dbe5a8753fb06dcf606db701971ad329aff72d6fba7ef05239f63982c7`.
All listed addresses have PE image base 0x140000000. GNU objdump disassembled
bounded PE .pdata procedure ranges; these are reconstructed machine instructions,
not recovered original source, and do not establish execution in a particular run.

REA `open_binary(provider_id=ghidra)` returned `provider_unavailable`:
GHIDRA_INSTALL_DIR unset; matching Ghidra absent; Java is JRE 1.8.0_261 rather
than required full JDK21+. No configuration/install action was taken. Focused
byte/RIP-relative candidate searches and existing objdump supplied bounded evidence.

### Rail 218 is HWiNFO's 16-pin HVPWR

`disassembly-xrefs-1402be5a0.txt`:

- 0x1402be5aa: `mov eax,0xda`.
- 0x1402be5b2: `cmp ecx,eax`.
- 0x1402be5ba: equality branches to 0x1402be85c.
- 0x1402be85c and 0x1402be875 load string 0x1407befe0.
- Original string at file offset0x7bdbe0 is `GPU 16-pin HVPWR`.

Thus mapping numeric rail 218 to the HWiNFO name is directly supported by
its name-dispatch control flow. The label is software nomenclature; it does
not assert the laptop has a desktop-style physical 16-pin connector.

### Performance limiter bit 32 is HWiNFO's SLI GPUBoost Sync

`disassembly-xrefs-1402c4418.txt`:

- 0x1402c4464 sets r14d=1.
- 0x1402c4ec2–0x1402c4ec9 admits sensor index10..15; 0x1402c4f1a subtracts10.
- 0x1402c4f38 points buffer to object+0x3ff0; version0x10550 is set.
- Call0x1402c4f7e uses pointer0x140a34b28. Loader0x14012cc61..0x14012cc68
  binds `NvAPI_GPU_PerfPoliciesGetStatus` to that same pointer.
- 0x1402c5015 reads limits at object+0x4000 = buffer+16.
- 0x1402c501e executes `bt eax,r15d`; 0x1402c5022 converts selected bit to bool.
- Switch branches for indexes0..4; final comparison after four decrements
  selects original bit index5 at0x1402c505e.
- 0x1402c505e/0x1402c506d load string0x1407bfc50 =
  `Performance Limit - SLI GPUBoost Sync`, original file offset0x7be850.

This resolves the wrapper's Unknown32 specifically to HWiNFO8.16's diagnostic
name. It is static mapping evidence; no SLI state was induced/observed.
Other verified bits:1 power,2 thermal,4 reliability voltage,8 max operating
voltage,16 utilization. [nvapi-rs primary definitions](https://github.com/arcnmx/nvapi-rs/blob/a442cf7f325e66212306c1bf5d27d3733275b200/sys/src/gpu/power.rs).

### Crossbar Clock = GetAllClocks v2 clocks[2] /1000

- `disassembly-xrefs-14012bfc8.txt`: loader0x14012cdab loads
  `NvAPI_GPU_GetAllClocks` and stores result to pointer0x140a34af0 at0x14012cdcd.
- `disassembly-allclocks-140325aa4.txt`: zeroes0x484-byte buffer at0x1403276c0;
  writes version0x20484 at0x1403276d4; calls pointer0x140a34af0 at0x1403276ec.
- On success, 0x1403277bf reads uint32 at buffer+0x0C (version+clocks[2]),
  converts to double and divides xmm6 at0x1403277cd; stores to hardware+0xb98
  at0x1403277d1.
- xmm6 is loaded from0x140808658 at0x140327061; decoded original double=1000.0.
- `disassembly-xrefs-1402c5d08.txt`: 0x1402c5dc1 reads hardware+0xb98;
  0x1402c5dd5/0x1402c5de4 load `GPU Crossbar Clock` at0x1407bff48.

The isolated worker implements the same version/byte offset/conversion;
final probe returned1845 MHz, consistent with HWiNFO's earlier captured value.

## Boundaries and remaining unknowns

No guarantee of complete HWiNFO-wide CPU/EC/storage coverage follows from this
GPU work. Parent owns derived averages/statistics and coverage mapping. The
worker does not fabricate channel sums: PP/NVVDD sums are returned as actual
driver channel types. New drivers may change private layouts, expose unknown
rails, or return errors/crash. Stable numeric channel IDs and raw enum/type
values are preserved in notes so unsupported mapping stays visible.

The simple constant-byte scan (`hwinfo-ids.json`) did not find immediate query
IDs; HWiNFO resolves interface names at runtime. That negative scan is limited
to exact byte sequences, not proof no calls exist. `string-xrefs.json` and
bounded loader disassembly supersede it for identified calls.

## License notice to include in the deliverable

`pynvraw/LICENSE` is MIT, Copyright(c)2021 Vasily. The worker adapts substantial
ABI/enum declarations from that source, so ship the following notice (also
stored in `pynvraw-LICENSE.txt`) alongside the CLI/source:

Copyright (c) 2021 Vasily

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
