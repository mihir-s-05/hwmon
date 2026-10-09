# Third-party notices

hwmon uses the following separately distributed, unmodified libraries. Their
licenses govern those libraries; the application source is provided under MIT.
Library DLLs remain replaceable. You may modify or replace them and rebuild the
application using the included project and dependency lockfile. The application
imposes no restriction on reverse engineering to debug such modifications.

## NvAPIWrapper.Net 0.8.1.101

Copyright © Soroush Falahati 2018 (falahati.net). NvAPIWrapper and its use in this
application are covered by GNU LGPL version 3. Full LGPL-3.0 and GPL-3.0 texts are
in `licenses/`. [Upstream source](https://github.com/falahati/NvAPIWrapper/tree/v0.8.1.101)
and a matching source archive are included in `licenses/`. The library is linked
as a separate DLL; no modifications were made to that DLL or its source.

## LibreHardwareMonitorLib 0.9.6

LibreHardwareMonitor and contributors. MPL-2.0. Full license text and source archive
are in `licenses/`. [Matching source commit](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701).
The sensor library is unmodified; the GPL Windows Forms frontend is not included.

## Other managed dependencies

| Library | License | Source |
|---|---|---|
| BlackSharp.Core 1.0.7 | MPL-2.0 | [BlackSharp](https://github.com/Blacktempel/BlackSharp/tree/c70b735c6cec123ee8a046ac4a0bc6c606f52cf0) |
| DiskInfoToolkit 1.1.2 | MPL-2.0 | [DiskInfoToolkit](https://github.com/Blacktempel/DiskInfoToolkit/tree/25319eae5781e75bcf141e844ceab2afe94d40ea) |
| RAMSPDToolkit-NDD 1.4.2 | MPL-2.0 | [RAMSPDToolkit](https://github.com/Blacktempel/RAMSPDToolkit/tree/3b47b960e0830fef344624ad5e389675d5f0a1ce) |
| HidSharp 2.6.4 | Its packaged permissive license | [HidSharp](https://software.seekye.com/hidsharp); full `HidSharp-LICENSE.txt` included |
| Mono.Posix.NETStandard 1.0.0 | Mono license | [Mono](https://github.com/mono/mono); full license collection included |
| Microsoft System.* packages and .NET runtime | MIT and upstream third-party notices | [dotnet](https://github.com/dotnet/runtime); packaged license/notices included |

Exact dependency versions, repository commits, and NuGet license metadata are in
`licenses/dependency-metadata.json` and `packages.lock.json`. Refer to each project's
source and embedded notices for component-specific exceptions. No PawnIO installer
or NVIDIA driver DLL is distributed by this application; it uses locally installed
driver interfaces. LibreHardwareMonitor's embedded driver modules are supplied as
part of the unmodified upstream library and its corresponding source archive.

## Private NVAPI ABI declarations adapted from pynvraw

[pynvraw source](https://github.com/JustAMan/pynvraw/blob/9a860c547256cfc6f527bf4be3aeeed27d95fd85/src/pynvraw/nvapi_api.py).

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
