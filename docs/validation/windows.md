# Windows validation

Tested on October 9, 2026 with an ASUS GU604VI laptop, Intel i9-13900H,
Intel Iris Xe and NVIDIA RTX 4070 Laptop GPU, driver 617.14.

The original comparison used HWiNFO 8.16 logging and direct CLI samples during
idle/light desktop activity. Of 43 NVIDIA log columns, 41 had mapped channels.
Two aggregate summaries were comparable rather than verified identical.
The individual mapping is in [hwinfo-coverage.csv](hwinfo-coverage.csv).

The session lacked PawnIO and elevation, so CPU register readings and disk
SMART/reliability data were permission-limited. GPU readings worked through the
installed driver APIs. The later v0.2 regression export contained 144 GPU channels
across separate sources, with per-reading timestamps and all four GPU backends
initialized. The 20 offline checks passed on Windows.

Observed power spikes near 590W exceeded the GPU's reported 140W maximum. They
were preserved as raw data and excluded from accepted statistics. Earlier live
tests also verified JSONL, long/wide CSV, timed watch and private-worker fallback.

This validates the tested behavior and units; it does not calibrate sensor
accuracy under load or establish HWiNFO-wide CPU/embedded-controller coverage.
