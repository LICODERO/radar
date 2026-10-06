namespace Radar.Server.Infrastructure.Localization;

public enum Lang { Pl, En }

/// <summary>Every user-facing message the API can return. Endpoints pick the text by the language the UI sends.</summary>
public enum Msg
{
    // common
    NoScan, UnknownRepo, RepoDirMissing,
    // security
    HostDenied, OriginDenied, TokenDenied,
    // settings
    PathRequired, PathInvalid, PathIsRoot, PathMissing,
    // scans
    PickScanDir, ScanRunning, ScanToolUnsupported,
    // files
    FileNotInScan, FileMissing, FileForbidden,
    // run
    RunUnsupported, UnknownTool, UnknownGap, GapAbsent, RepoOutsideRoot, TerminalFailed,
    // agents
    DescriptionRequired, DescriptionTooLong, GenerationBusy, AgentNameTakenInRepo, ContentRequired, AgentInvalid, AgentExists, WriteForbidden,
    AgentSourceMissing, CopySourceUnreadable, CopyNoTargets, CopyTooMany,
    // agent file validation
    FileTooBig, FrontmatterMissing, NameInvalid, DescriptionMissing, DescriptionLong, BodyMissing,
    // visibility
    VisibilityBadTarget, VisibilityWriteFailed, VisibilityGitFailed, VisibilityCannotHide,
    // vault
    VaultNotReady, VaultNotEmpty, VaultInsideRepo, VaultAlready, VaultNotAVault, VaultTooNew, VaultWriteFailed,
    ProjectNameTaken, ClaudeLocalForbidden, ClaudeLocalDamaged,
    SkillLinked, SkillForeign, SkillModified, SkillOutdated, SkillWriteFailed,
    // claude CLI
    ClaudeNotFound, ClaudeStartFailed, ClaudeExited, ClaudeTimeout, ClaudeUnexpected, ClaudeReportedError, ClaudeEmpty
}

public static class Messages
{
    private static readonly Dictionary<Msg, (string Pl, string En)> Catalog = new()
    {
        [Msg.NoScan] = ("Brak zapisanego skanu.", "No saved scan."),
        [Msg.UnknownRepo] = ("Nieznane repozytorium.", "Unknown repository."),
        [Msg.RepoDirMissing] = ("Katalog repozytorium już nie istnieje. Uruchom skan ponownie.", "The repository directory no longer exists. Run the scan again."),

        [Msg.HostDenied] = ("Niedozwolony nagłówek Host.", "Host header not allowed."),
        [Msg.OriginDenied] = ("Niedozwolone źródło żądania.", "Request origin not allowed."),
        [Msg.TokenDenied] = ("Brak lub nieprawidłowy token sesji.", "Missing or invalid session token."),

        [Msg.PathRequired] = ("Podaj ścieżkę katalogu.", "Enter a directory path."),
        [Msg.PathInvalid] = ("Nieprawidłowa ścieżka.", "Invalid path."),
        [Msg.PathIsRoot] = ("Nie można skanować katalogu głównego dysku.", "The drive root cannot be scanned."),
        [Msg.PathMissing] = ("Katalog nie istnieje.", "The directory does not exist."),

        [Msg.PickScanDir] = ("Wybierz istniejący katalog skanu.", "Choose an existing scan directory."),
        [Msg.ScanRunning] = ("Skan już trwa.", "A scan is already running."),

        [Msg.FileNotInScan] = ("Plik nie należy do wyniku skanu.", "The file is not part of the scan result."),
        [Msg.FileMissing] = ("Plik już nie istnieje. Uruchom skan ponownie.", "The file no longer exists. Run the scan again."),
        [Msg.FileForbidden] = ("Odczyt tego pliku jest zabroniony.", "Reading this file is not allowed."),

        [Msg.RunUnsupported] = ("Uruchamianie terminala nie jest dostępne na tym systemie. Skopiuj polecenie.", "Opening a terminal is not available on this system. Copy the command instead."),
        [Msg.UnknownTool] = ("Nieznane narzędzie.", "Unknown tool."),
        [Msg.UnknownGap] = ("Nieznany typ luki.", "Unknown gap type."),
        [Msg.GapAbsent] = ("Ta luka nie występuje w wyniku skanu.", "This gap is not present in the scan result."),
        [Msg.RepoOutsideRoot] = ("Katalog repozytorium jest poza katalogiem skanu.", "The repository directory is outside the scan directory."),
        [Msg.TerminalFailed] = ("Nie udało się otworzyć terminala: {0}", "Could not open the terminal: {0}"),

        [Msg.DescriptionRequired] = ("Opisz agenta własnymi słowami.", "Describe the agent in your own words."),
        [Msg.DescriptionTooLong] = ("Opis jest za długi (max {0} znaków).", "The description is too long (max {0} characters)."),
        [Msg.GenerationBusy] = ("Trwa już inne generowanie.", "Another generation is already in progress."),
        [Msg.AgentNameTakenInRepo] = ("Agent o tej nazwie już istnieje w tym repozytorium. Zmień nazwę.", "An agent with this name already exists in this repository. Change the name."),
        [Msg.ContentRequired] = ("Brak treści pliku.", "The file content is missing."),
        [Msg.AgentInvalid] = ("Plik agenta jest niepoprawny.", "The agent file is invalid."),
        [Msg.AgentExists] = ("Agent o tej nazwie już istnieje. Zmień nazwę.", "An agent with this name already exists. Change the name."),
        [Msg.AgentSourceMissing] = ("Nie ma takiego agenta w repozytorium źródłowym.", "The source repository has no such agent."),
        [Msg.CopySourceUnreadable] = ("Nie można odczytać pliku agenta źródłowego.", "The source agent file cannot be read."),
        [Msg.CopyNoTargets] = ("Wybierz repozytoria, do których skopiować agenta.", "Choose the repositories to copy the agent to."),
        [Msg.CopyTooMany] = ("Za dużo repozytoriów naraz (max {0}).", "Too many repositories at once (max {0})."),
        [Msg.WriteForbidden] = ("Zapis w tym katalogu jest zabroniony (dowiązanie poza repozytorium).", "Writing in this directory is not allowed (a link pointing outside the repository)."),

        [Msg.FileTooBig] = ("Plik jest za duży (limit 64 KB).", "The file is too large (64 KB limit)."),
        [Msg.FrontmatterMissing] = ("Brak poprawnego frontmattera: plik ma zaczynać się od bloku --- ... ---.", "Missing valid frontmatter: the file must start with a --- ... --- block."),
        [Msg.NameInvalid] = ("Pole name musi być małymi literami, cyframi i myślnikami (np. migration-reviewer), 2-64 znaki.", "The name field must use lowercase letters, digits and hyphens (e.g. migration-reviewer), 2-64 characters."),
        [Msg.DescriptionMissing] = ("Pole description jest wymagane (min. 10 znaków): kiedy używać agenta.", "The description field is required (min. 10 characters): when to use the agent."),
        [Msg.DescriptionLong] = ("Pole description jest za długie (max 1024 znaki).", "The description field is too long (max 1024 characters)."),
        [Msg.BodyMissing] = ("Brak treści instrukcji po frontmatterze.", "No instructions after the frontmatter."),

        [Msg.VisibilityBadTarget] = ("Widoczność może być tylko publiczna albo prywatna.", "Visibility can only be public or private."),
        [Msg.VisibilityWriteFailed] = ("Nie udało się zapisać listy prywatnych plików (.git/info/exclude).", "Could not write the private list (.git/info/exclude)."),
        [Msg.VisibilityGitFailed] = ("Git odmówił zmiany: {0}", "Git refused the change: {0}"),

        [Msg.VisibilityCannotHide] = ("Nie mogę ukryć nowego pliku przed gitem (brak gita, uszkodzony blok w .git/info/exclude), więc go nie tworzę. Wybierz publiczny albo to napraw.", "Cannot hide the new file from git (no git, or a damaged block in .git/info/exclude), so I am not creating it. Pick public, or fix that."),

        [Msg.VaultNotReady] = ("Second brain nie jest skonfigurowany albo jego folder jest niedostępny.", "The second brain is not set up, or its folder is unavailable."),
        [Msg.VaultNotEmpty] = ("Wybrany folder nie jest pusty. Wskaż pusty lub nowy folder.", "The chosen folder is not empty. Choose an empty or new folder."),
        [Msg.VaultInsideRepo] = ("Folder second brain nie może leżeć wewnątrz repozytorium git.", "The second brain folder cannot be inside a git repository."),
        [Msg.VaultAlready] = ("W tym folderze jest już second brain. Wskaż go jako istniejący.", "This folder already holds a second brain. Link to it as an existing one."),
        [Msg.VaultNotAVault] = ("To nie jest folder second brain (brak pliku .radar-vault.json).", "This is not a second brain folder (.radar-vault.json is missing)."),
        [Msg.VaultTooNew] = ("Ten second brain pochodzi z nowszej wersji R.A.D.A.R.", "This second brain was created by a newer version of R.A.D.A.R."),
        [Msg.VaultWriteFailed] = ("Nie udało się zapisać plików w tym folderze.", "Could not write the files in this folder."),
        [Msg.ProjectNameTaken] = ("Inny projekt używa już folderu o tej nazwie w second brain.", "Another project already uses a folder with this name in the second brain."),
        [Msg.ClaudeLocalForbidden] = ("Nie można zapisać CLAUDE.local.md (dowiązanie poza repozytorium albo katalog).", "CLAUDE.local.md cannot be written (a link outside the repository, or a directory)."),
        [Msg.ClaudeLocalDamaged] = ("Znaczniki bloku second brain w CLAUDE.local.md są uszkodzone. Popraw je ręcznie.", "The second brain block markers in CLAUDE.local.md are damaged. Fix them by hand."),
        [Msg.SkillLinked] = ("Skill jest dowiązaniem symbolicznym (np. do ai-toolkit). R.A.D.A.R. go nie zmienia.", "The skill is a symbolic link (for example to ai-toolkit). R.A.D.A.R. leaves it alone."),
        [Msg.SkillForeign] = ("W tym miejscu jest już inny skill o tej nazwie. R.A.D.A.R. go nie nadpisze.", "A different skill with this name is already there. R.A.D.A.R. will not overwrite it."),
        [Msg.SkillModified] = ("Skill został zmieniony ręcznie. R.A.D.A.R. go nie nadpisze.", "The skill was edited by hand. R.A.D.A.R. will not overwrite it."),
        [Msg.SkillOutdated] = ("Zainstalowany skill jest w starszej wersji. Potwierdź aktualizację.", "The installed skill is an older version. Confirm the update."),
        [Msg.SkillWriteFailed] = ("Nie udało się zapisać skilla.", "Could not write the skill."),

        [Msg.ScanToolUnsupported] = ("Skan dla tego narzędzia AI nie jest jeszcze dostępny. Na razie skanowany jest tylko Claude Code.", "Scanning for this AI tool is not available yet. Only Claude Code is scanned for now."),
        [Msg.ClaudeNotFound] = ("Nie znaleziono polecenia claude w PATH serwera. Zainstaluj Claude Code i zaloguj się.", "The claude command was not found in the server PATH. Install Claude Code and sign in."),
        [Msg.ClaudeStartFailed] = ("Nie udało się uruchomić claude.", "Could not start claude."),
        [Msg.ClaudeExited] = ("claude zakończył się błędem ({0}): {1}", "claude exited with an error ({0}): {1}"),
        [Msg.ClaudeTimeout] = ("claude nie odpowiedział w ciągu 150 s.", "claude did not respond within 150 s."),
        [Msg.ClaudeUnexpected] = ("Nieoczekiwana odpowiedź claude.", "Unexpected response from claude."),
        [Msg.ClaudeReportedError] = ("claude zgłosił błąd: {0}", "claude reported an error: {0}"),
        [Msg.ClaudeEmpty] = ("Pusta odpowiedź claude.", "Empty response from claude.")
    };

    public static string Get(Lang lang, Msg msg, params object[] args)
    {
        var (pl, en) = Catalog[msg];
        var text = lang == Lang.En ? en : pl;
        return args.Length == 0 ? text : string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
    }

    /// <summary>Language from an Accept-Language header: the first supported tag wins; Polish when none is.</summary>
    public static Lang Parse(string? acceptLanguage)
    {
        foreach (var part in (acceptLanguage ?? "").Split(','))
        {
            var tag = part.Split(';')[0].Trim().ToLowerInvariant();
            var primary = tag.Split('-')[0];
            if (primary == "en") return Lang.En;
            if (primary == "pl") return Lang.Pl;
        }
        return Lang.Pl;
    }
}

/// <summary>Messages in the language of the current request.</summary>
public sealed class RequestMessages(IHttpContextAccessor accessor)
{
    public Lang Lang => Messages.Parse(accessor.HttpContext?.Request.Headers.AcceptLanguage.ToString());

    public string this[Msg msg] => Messages.Get(Lang, msg);

    public string T(Msg msg, params object[] args) => Messages.Get(Lang, msg, args);
}
