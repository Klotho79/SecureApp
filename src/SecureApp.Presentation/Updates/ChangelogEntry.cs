namespace SecureApp.Presentation.Updates;

/// <summary>
/// One release's worth of changelog items, read from the bundled <c>Resources/Raw/changelog.json</c>
/// (2026-10-19, user's own ask: "Log do apky... jsem chtel" — the release log belongs IN the app
/// itself, readable offline, not only as a document outside it). Append a new entry here with every
/// release — nothing reads this from the relay, it ships with the build.
/// </summary>
public sealed record ChangelogEntry(string Version, int Code, string Date, List<string> Items);
