# Third-party notices

This source snapshot restores NuGet dependencies at build time. The table records package identities and license metadata for the .NET 9 target. License files and notices required for bundled components are included below and in the referenced directories. Recheck the exact dependency set when preparing a binary distribution.

| Package | Version | Metadata license |
| --- | --- | --- |
| AngleSharp.Css | 1.0.2 | MIT |
| AngleSharp | 1.7.2 | MIT |
| HtmlSanitizer | 9.2.1039 | MIT |
| Markdig | 1.4.0 | BSD-2-Clause |
| Microsoft.AspNetCore.Authorization | 9.0.0 | MIT |
| Microsoft.AspNetCore.Components.Analyzers | 9.0.0 | MIT |
| Microsoft.AspNetCore.Components.Forms | 9.0.0 | MIT |
| Microsoft.AspNetCore.Components.Web | 9.0.0 | MIT |
| Microsoft.AspNetCore.Components.WebView.Wpf | 9.0.0 | MIT |
| Microsoft.AspNetCore.Components.WebView | 9.0.0 | MIT |
| Microsoft.AspNetCore.Components | 9.0.0 | MIT |
| Microsoft.AspNetCore.Metadata | 9.0.0 | MIT |
| Microsoft.Data.Sqlite.Core | 9.0.20 | MIT |
| Microsoft.Data.Sqlite | 9.0.20 | MIT |
| Microsoft.Extensions.Configuration.Abstractions | 9.0.0 | MIT |
| Microsoft.Extensions.Configuration.Binder | 9.0.0 | MIT |
| Microsoft.Extensions.Configuration.FileExtensions | 9.0.0 | MIT |
| Microsoft.Extensions.Configuration.Json | 9.0.0 | MIT |
| Microsoft.Extensions.Configuration | 9.0.0 | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | 9.0.0 | MIT |
| Microsoft.Extensions.DependencyInjection | 9.0.0 | MIT |
| Microsoft.Extensions.FileProviders.Abstractions | 9.0.0 | MIT |
| Microsoft.Extensions.FileProviders.Composite | 9.0.0 | MIT |
| Microsoft.Extensions.FileProviders.Embedded | 9.0.0 | MIT |
| Microsoft.Extensions.FileProviders.Physical | 9.0.0 | MIT |
| Microsoft.Extensions.FileSystemGlobbing | 9.0.0 | MIT |
| Microsoft.Extensions.Logging.Abstractions | 9.0.0 | MIT |
| Microsoft.Extensions.Logging | 9.0.0 | MIT |
| Microsoft.Extensions.Options | 9.0.0 | MIT |
| Microsoft.Extensions.Primitives | 9.0.0 | MIT |
| Microsoft.JSInterop | 9.0.0 | MIT |
| Microsoft.Web.WebView2 | 1.0.2792.45 | Package license file: LICENSE.txt |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Apache-2.0 |
| SQLitePCLRaw.core | 2.1.12 | Apache-2.0 |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Apache-2.0 |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | Apache-2.0 |
| System.Memory | 4.5.3 | MIT (Microsoft license URL) |
| PdfPig | 0.1.16 | Apache-2.0 |

## Included notices

- `PdfPig` 0.1.16 is distributed under Apache-2.0. The corresponding upstream license, including its external-component notices, is included as [`PdfPig-LICENSE.txt`](PdfPig-LICENSE.txt). See the [official PdfPig package](https://www.nuget.org/packages/PdfPig), [upstream documentation](https://github.com/UglyToad/PdfPig/blob/master/docs/index.md) and [upstream license](https://github.com/UglyToad/PdfPig/blob/master/LICENSE).
- `System.Memory` 4.5.3 uses the MIT License; its license and third-party notices are included under [`third-party-licenses`](third-party-licenses/). See the [Microsoft .NET license](https://github.com/dotnet/corefx/blob/master/LICENSE.TXT).
- `Microsoft.Web.WebView2` license and notice texts are included under [`third-party-licenses`](third-party-licenses/).
- Bundled font license texts and notices are included alongside the six fonts in `src/DBLC/wwwroot/fonts/gm-expression/`.
- The DBLC sigil, favicon, app icon and dice artwork are ASCII/Codex-generated artwork retained for this release candidate.

NuGet dependency versions are restored during build and are not vendored in this source snapshot.
## Bundled fonts

The optional GM presentation fonts are accompanied by license texts in `src/DBLC/wwwroot/fonts/gm-expression/`: Special Elite (Apache-2.0), Caveat, UnifrakturMaguntia, IM Fell English, Bangers, and Creepster (SIL Open Font License 1.1). See that directory's notices and individual license files.

## DBLC project license

DBLC is licensed under the MIT License; see [`LICENSE`](LICENSE). This project license does not replace third-party component licenses or grant rights to materials excluded from this source snapshot.
