# DBLC

DBLC is a Windows tabletop role-playing workspace where a local AI model acts as Game Master. The Chronicle is the table: play is recorded there, while campaign records and reference tools are available alongside it. DBLC is designed for one human owner; an optional mobile browser view can connect to the same desktop instance.

> DBLC is pretty much 100% vibe-coded with Codex, with a few generated assets edited by me. If you'd like to support the project, you can find me on [Ko-fi](https://ko-fi.com/babakrak).

DBLC keeps the campaign at home: a local model runs the game, and an optional mobile browser can connect to the desktop-hosted app over a private network such as Tailscale. The GM has tools for campaign records, reference books, dice, presentation options, campaign time and the optional Surprise Machine. You control the GM Profile and protected instructions; Chronicle messages are recorded as they happen rather than edited or regenerated afterward.

Separate Workbenches let you discuss plans outside the main Session. When you end one, you can review and approve facts or notes to keep. Session summaries can carry forward into the next Session, while older history remains stored for retrieval. Around the existing context threshold, DBLC summarizes older Session material for the model while keeping the original history available locally. A local privacy PIN can restrict opening DBLC or restoring it from the system tray; it is an access lock, not data encryption.

The dice roller supports d4 through d20. You can select a die for your next roll without recording it immediately; the roll enters the Chronicle when you submit it with a message or send an empty turn. Models with vision can receive attached images, and can inspect imported maps when requested and supported by the local server.

## Screenshots

These screenshots use a fictional demonstration campaign and an original generated map.

![Chronicle with fictional player and NPC dialogue](screenshots/01-chronicle.jpg)

![Imported fictional map in DBLC's image viewer](screenshots/03-imported-map-fictional-demo.jpg)

## What DBLC includes

### Chronicle and play

- **Live Chronicle:** streamed GM narration, recent activity status, Markdown, player messages and image attachments, manual dice rolls, and a searchable/retrievable campaign history.
- **Character Speakers:** GM responses can identify NPC dialogue with speaker names and saved portraits. Optional narration fonts and presentation effects can be configured for the Chronicle.
- **Campaigns and Sessions:** keep multiple campaigns and Sessions, summarize or end a Session, carry a saved summary into the next Session, duplicate or export a Chronicle, and browse archived history.
- **Workbenches:** open a disposable conversation alongside a Session for out-of-character planning. Temporary context compaction helps long Workbenches continue; Review & End lets you approve specific notes or Chronicle facts for retention, while Discard leaves the parent Session unchanged.
- **Dice roller:** roll supported dice into the Chronicle, or select a die for the next roll without immediately recording a result.

### Campaign workspace

- **Player Sheet and Workspace Notes:** edit campaign documents with Markdown and embedded images. On desktop, either document can be opened in its own window and returned to the main workspace.
- **GM Notes:** keep campaign-scoped house rules, procedures and rulings; search notes as needed; export them as versioned JSON or readable Markdown; and preview and confirm JSON imports. GM responses can show restrained links to notes actually retrieved or cited.
- **NPC catalogue:** save NPC notes, portraits and portrayal cues; use Character Speakers to show an NPC as the voice of a GM response. Portraits can be positioned for display.
- **Bestiary:** maintain campaign creature entries, notes, portraits and defeated counts. Proposed Player Sheet and Bestiary changes use review/approval flows.
- **Agenda and campaign time:** schedule and review events, browse a month grid, use Gregorian or configured custom calendars, and let the GM advance the authoritative campaign clock when fictional time passes.
- **Open Threads:** track unresolved campaign matters with player-visible or GM-private visibility.
- **Surprise Machine:** an optional, configurable campaign pacing feature; it is disabled unless enabled in settings.

### References and maps

- **Rules library:** choose a local folder of PDFs. DBLC builds searchable book catalogues and a compact index from available metadata, then supplies requested passages on demand. Optional Book Companion JSON files add structured sections. The GM should consult source text before giving a rules-based answer.
- **Source references:** when the GM cites material it actually retrieved, Chronicle references can point back to the relevant book passage, GM Note or archived Chronicle excerpt.
- **Imported maps:** add PNG, JPEG or WebP maps to a campaign, manage their titles, and browse thumbnails. Open maps in a viewer with zoom and pan. A vision-capable model can inspect the original map image on request when the local server supports image input; maps are not automatically sent to the model.

### Customization and access

- **GM Profile and Skills:** customize the GM Profile and add, edit or remove ordinary `SKILL.md` files. Six starter Skills cover rules research, adjudication, combat, advancement, campaign memory and NPC portrayal; the GM discovers and loads them only when useful. See the [Skills guide](docs/skills/README.md).
- **Desktop and mobile:** use DBLC's desktop workspace or optionally connect a responsive browser interface to the same desktop-hosted campaign. Mobile access is disabled by default and must be deliberately configured.
- **Desktop convenience and privacy:** optionally keep DBLC running in the system tray when its window closes, and require a local PIN when opening the app or restoring it from the tray. These controls are local application features, not a hosted account or cloud service.

DBLC supplies tabletop tools, not a rules engine. The selected model and its chat template determine how reliably it follows instructions and calls tools. Some features, including image inspection, require model/server support.

## Requirements

- Windows 10 or 11.
- .NET 9 SDK to run from source.
- Microsoft Edge WebView2 Runtime for the desktop interface.
- A separately installed local model server with an OpenAI-compatible chat-completions API. DBLC's default endpoint is `http://127.0.0.1:8081/v1`; change it in Settings if your server uses another address.
- A model and server chat template that support tool calls for GM tools. Image inspection also requires a vision-capable model and server configuration.

## Run on Windows

1. Install the requirements above.
2. Start your local model server and note its API endpoint. Keep it bound to your own machine unless you intentionally configure secure network access.
3. Download or clone this repository, then open `DBLC.sln` in Visual Studio or run these commands from the repository folder:

   ```powershell
   dotnet restore DBLC.sln
   dotnet run --project src/DBLC/DBLC.csproj
   ```

4. In DBLC Settings, enter the server endpoint and choose your local Library folder if you have reference PDFs. Select a discovered model and start a Session.

To build a self-contained Windows x64 folder from source:

```powershell
$freshBuild = Join-Path (Get-Location) 'outputs/FreshBuild'
if (Test-Path -LiteralPath $freshBuild) { Remove-Item -LiteralPath $freshBuild -Recurse -Force }
dotnet publish src/DBLC/DBLC.csproj -c Release -r win-x64 --self-contained true -o $freshBuild
```

Run `outputs/FreshBuild/DBLC.exe`. WebView2 is still required. This is a development publish folder, not an installer or signed package.

## Local data and privacy

Settings and campaign data are stored under `%LOCALAPPDATA%\DBLC` by default. The selected Library remains in its chosen folder. DBLC does not ship your campaigns, saves, PDFs, Book Companions or model weights. Runtime files are excluded from source control; keep your own backups of campaign data.

For a separate test profile, set `DBLC_DATA_ROOT` to a disposable folder before launching DBLC. The optional mobile host is off by default and is not a public hosting service. Do not expose it directly to the public internet.

## Guides and notices

- [Agent Skills](docs/skills/README.md)
- [Book Companion processing specification](docs/DBLC_BOOK_PROCESSING_SPEC.md)
- [Mobile browser access](docs/MOBILE_TAILSCALE_STAGE1.md)
- [Surprise Machine guide](docs/SURPRISE_MACHINE_README.md)
- [Third-party notices](THIRD-PARTY-NOTICES.md)
- [MIT License](LICENSE)

## Previously explored features

### GM Sketch Maps — developed, later removed

DBLC previously included a GM-driven sketch-map system capable of creating and modifying maps during gameplay.

The feature was functional and ambitious, but introduced additional tool complexity and operational demands that were poorly suited to smaller local language models.

It was removed to prioritize reliable GM behavior, simpler tool use and faster gameplay—not because the concept lacked merit.

Imported image maps remain supported.

**Design philosophy:** DBLC prioritizes features that local models can use reliably over features that merely demonstrate technical capability.

## Limitations

DBLC is currently distributed as a portable Windows application and as MIT-licensed source code. There is no signed installer. Model behavior and tool reliability vary by model, chat template and server version. DBLC is MIT-licensed; third-party components retain their own terms. See `LICENSE` and `THIRD-PARTY-NOTICES.md`.
