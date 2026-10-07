# EpubManager

Build EPUB 3 files from online stories. The core package knows how to write EPUBs; each site is a separate plugin package, so you only install what you use.

## Install

```
dotnet add package EpubManager
dotnet add package EpubManager.Writers.Literotica
```

Targets .NET Standard 2.0.

## Usage

```csharp
using EpubManager.ContentSources;

IStoryWriter writer = EpubManager.EpubManager.Writers.Literotica
    ?? throw new InvalidOperationException("Install EpubManager.Writers.Literotica");

await writer.CreateEpubFromStoryAsync("https://www.literotica.com/s/some-story-slug", "./epubs");
await writer.CreateEpubFromSeriesAsync("https://www.literotica.com/series/se/123456", "./epubs");
```

Both methods take optional `coverOverwrite`, `raw` (write the unzipped folder instead of an `.epub`), and a `CancellationToken`. Series also take `startIndex` / `endIndex` (inclusive; `endIndex` 0 means through the last part).

What the writer does for you: builds in a private temp folder (concurrent builds are safe), packages the zip with `mimetype` first, retries transient download failures, embeds remote inline images, and keeps going with a logged warning if a cover or image can't be added. Rebuilding the same story gives the same book identifier, so e-readers don't treat it as a new book.

To build an EPUB from your own chapter files, create an `EpubStory` and call `StoryWriter.CreateEpubAsync(story, onLog, outputDirectory, raw, cancellationToken)` (or the synchronous `CreateEpub`). Use `StoryWriter.StableId(seed)` for a repeatable `Identifier`.

## Options

Pass an `EpubOptions` to either create method to set the language, a synopsis, and the look of the book. Anything left unset keeps the writer's default.

```csharp
using EpubManager.ContentSources;

await writer.CreateEpubFromStoryAsync(url, "./epubs", options: new EpubOptions
{
    Language = "French",                       // BCP 47 code ("fr", "pt-BR") or a name; unknown names become "und" with a warning
    Description = "A short synopsis.

Blank lines start a new paragraph.",
    Style = new EpubStyle
    {
        Font = EpubFont.SansSerif,             // Serif (default), SansSerif, Monospace
        FontSizePercent = 110,                 // 50-300, default 100
        LineHeight = 1.5,                      // 1.0-3.0, default 1.2
        TextAlign = EpubTextAlign.Justify,     // Left (default), Justify
        ParagraphStyle = EpubParagraphStyle.Indented, // Spaced (default), Indented
        ChapterHeadingAlign = EpubHeadingAlign.Center, // Left (default), Center
        SceneBreak = "~"                       // text for scene breaks ("* * *" default); "" draws a thin line
    }
});
```

The description is stored as the book's `dc:description` and shown on the title page. Style values are validated up front (`ArgumentOutOfRangeException`), and the default style ships the stock stylesheet unchanged. When building from your own `EpubStory`, set `Language`, `Description` and `Style` on it directly.

## How plugins are found

`EpubManager.Writers` discovers plugins on first use. It looks at loaded assemblies and at `EpubManager.Writers.*.dll` files next to the application, and registers every `IStoryWriter` with a public parameterless constructor under its class name.

```csharp
EpubManager.EpubManager.Writers.Available;        // names of what was found
EpubManager.EpubManager.Writers.Get("Literotica"); // null if not installed
EpubManager.EpubManager.Writers.Register("MySite", new MySiteWriter()); // manual
```

### Single-file and trimmed publishes

Discovery cannot see plugins that are bundled inside a single-file executable and never referenced in code, and the trimmer can remove them. Either:

- Copy the plugin DLL (and its dependencies) next to the executable, or
- Reference the plugin type and register it yourself: `Writers.Register("Literotica", new Literotica())`.

## Writing a plugin

1. Create a class library named `EpubManager.Writers.<Site>` that references `EpubManager`.
2. Add a public class named `<Site>` implementing `IStoryWriter`, with a parameterless constructor. Fetch the story, write each chapter to a file inside `StoryWriter.NewTempDirectory()` (delete it when done), build an `EpubStory` (set `Identifier = StoryWriter.StableId(...)` from the source URL), and pass it to `StoryWriter.CreateEpubAsync`. Wrap network fetches in `EpubManagerClient.WithRetryAsync`.
3. Publish it as a NuGet package. Users install it and it is picked up automatically.

`EpubManager.Writers.Literotica` in this repository is a working example.

## Repository layout

| Project | Purpose |
| --- | --- |
| `EpubManager` | Core: EPUB writer, shared HTTP client, plugin registry. |
| `EpubManager.Writers.Literotica` | Literotica plugin, built on [LiteroticaApi](https://github.com/RiisDev/LiteroticaApi). |

## License

MIT
