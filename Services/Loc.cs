using System.ComponentModel;
using System.Globalization;

namespace CasualtiesModManager.Services;

/// <summary>
/// Локализация: английский по умолчанию, русский — в настройках.
/// В коде: Loc.T("Key") или Loc.T("Key", args). В XAML: {loc:Tr Key} (обновляется на лету).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public static string Lang { get; private set; } = "en";
    public static bool IsRussian => Lang == "ru";

    /// <summary>Срабатывает после смены языка — окно обновляет то, что собирается в коде.</summary>
    public static event Action? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Индексатор для привязок из XAML: {Binding [Key], Source={x:Static Loc.Instance}}.</summary>
    public string this[string key] => T(key);

    public static string Normalize(string? lang) => lang == "ru" ? "ru" : "en";

    /// <summary>Задаёт язык при запуске (без событий).</summary>
    public static void Init(string? lang) => Lang = Normalize(lang);

    public static void SetLanguage(string lang)
    {
        var n = Normalize(lang);
        if (n == Lang) return;
        Lang = n;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    public static string T(string key) =>
        Table.TryGetValue(key, out var v) ? (IsRussian ? v.Ru : v.En) : key;

    public static string T(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>Слово во множественном числе: Plural(2, "mod") → «mods» / «мода».</summary>
    public static string Plural(int n, string word)
    {
        var forms = T("Plural_" + word).Split('|'); // one|few|many
        if (!IsRussian) return n == 1 ? forms[0] : forms[^1];

        n = Math.Abs(n) % 100;
        var d = n % 10;
        if (n is > 10 and < 20) return forms[2];
        if (d == 1) return forms[0];
        if (d is >= 2 and <= 4) return forms[1];
        return forms[2];
    }

    private static readonly Dictionary<string, (string En, string Ru)> Table = new()
    {
        // ---------- Общее ----------
        ["Quoted"] = ("“{0}”", "«{0}»"),
        ["Plural_mod"] = ("mod|mods|mods", "мод|мода|модов"),
        ["Size_B"] = ("B", "Б"),
        ["Size_KB"] = ("KB", "КБ"),
        ["Size_MB"] = ("MB", "МБ"),

        // ---------- Шапка: игра и BepInEx ----------
        ["Game_Label"] = ("GAME", "ИГРА"),
        ["Game_PickTip"] = ("Choose the folder with CasualtiesUnknown.exe", "Выбрать папку с CasualtiesUnknown.exe"),
        ["Game_NotFound"] = ("Game not found", "Игра не найдена"),
        ["Game_Found"] = ("Game found", "Игра найдена"),
        ["Game_PathPrompt"] = ("Choose the folder with CasualtiesUnknown.exe", "Укажи папку с CasualtiesUnknown.exe"),
        ["Bep_Installed"] = ("BepInEx installed", "BepInEx установлен"),
        ["Bep_Install"] = ("Install BepInEx", "Установить BepInEx"),
        ["W_NonAscii"] = (
            "The game path contains non-ASCII characters: BepInEx may fail to load mods. If mods don't work, move the game.",
            "В пути к игре есть не-ASCII символы: BepInEx может не загрузить моды. Если не работает — перенеси игру."),

        // ---------- Вкладки и кнопки ----------
        ["Tab_Mods"] = ("Mods", "Моды"),
        ["Tab_Nexus"] = ("Nexus", "Nexus"),
        ["Tip_Settings"] = ("Settings", "Настройки"),
        ["Btn_AddMod"] = ("Add mod", "Добавить мод"),
        ["Btn_ByLink"] = ("From link", "По ссылке"),
        ["Tip_ByLink"] = (
            "Download a mod from GitHub, Nexus Mods or a direct link — with auto-updates",
            "Скачать мод с GitHub, Nexus Mods или по прямой ссылке — с автообновлением"),
        ["Btn_Export"] = ("Export", "Экспорт"),
        ["Tip_Export"] = (
            "Save mods into a single file for friends (the selected ones, or all if nothing is selected)",
            "Сохранить моды в один файл для друзей (выбранные, а если ничего не выбрано, то все)"),
        ["Btn_Import"] = ("Import", "Импорт"),
        ["Tip_Import"] = ("Open a mod pack from a friend (.cumods)", "Открыть сборку модов от друга (.cumods)"),
        ["Tip_Rescan"] = ("Find mods already installed in the game folder", "Найти моды, уже установленные в папке игры"),
        ["Tip_OpenFolder"] = ("Open the game folder", "Открыть папку игры"),
        ["Btn_Play"] = ("Play", "Играть"),
        ["Btn_UpdateAll"] = ("Update all", "Обновить все"),
        ["Btn_UpdateAllN"] = ("Update all ({0})", "Обновить все ({0})"),
        ["Tip_UpdateAll"] = ("Install all updates that were found", "Установить все найденные обновления"),
        ["Btn_Updates"] = ("Updates", "Обновления"),
        ["Tip_CheckUpdates"] = ("Check for new versions of your mods", "Проверить, вышли ли новые версии модов"),
        ["Btn_UpdateTo"] = ("Update → {0}", "Обновить → {0}"),
        ["Tip_UpdateOne"] = ("Install the new version", "Установить новую версию"),
        ["Toggle_On"] = ("ON", "ВКЛ"),
        ["Toggle_Off"] = ("OFF", "ВЫКЛ"),
        ["Tip_Source"] = (
            "Update source (GitHub, Nexus or a direct link)",
            "Источник обновлений (GitHub, Nexus или прямая ссылка)"),
        ["Tip_Remove"] = ("Remove mod", "Удалить мод"),
        ["Search_Placeholder"] = ("Search mods…", "Поиск модов…"),
        ["Empty_Title"] = ("Drop mods here", "Перетащи сюда моды"),
        ["Empty_Sub"] = (".dll or .zip files, and .cumods packs from friends", ".dll или .zip, а также сборки .cumods от друзей"),
        ["Btn_PickFiles"] = ("Choose files…", "Выбрать файлы…"),
        ["Status_Ready"] = ("Ready", "Готов к работе"),
        ["Drop_Release"] = ("Release to add", "Отпусти, чтобы добавить"),

        // ---------- Диалоги ----------
        ["Dlg_Cancel"] = ("Cancel", "Отмена"),
        ["Dlg_Done"] = ("Done", "Готово"),
        ["Dlg_Confirm"] = ("Confirmation", "Подтверждение"),
        ["Dlg_PickGameTitle"] = ("Folder with CasualtiesUnknown.exe", "Папка с CasualtiesUnknown.exe"),
        ["Dlg_PickModsTitle"] = ("Choose mods", "Выбери моды"),
        ["Dlg_ModsFilter"] = (
            "Mods and packs (*.dll;*.zip;*.cumods)|*.dll;*.zip;*.cumods",
            "Моды и сборки (*.dll;*.zip;*.cumods)|*.dll;*.zip;*.cumods"),
        ["Dlg_RemoveConfirm"] = ("Remove {0}? Its files will be removed from the game.", "Удалить {0}? Файлы будут убраны из игры."),
        ["Dlg_SavePackTitle"] = ("Save pack", "Сохранить сборку"),
        ["Dlg_OpenPackTitle"] = ("Open pack", "Открыть сборку"),
        ["Dlg_PackFilter"] = ("Mod pack (*{0})|*{0}", "Сборка модов (*{0})|*{0}"),
        ["Dlg_AddLinkTitle"] = ("Add mod from link", "Добавить мод по ссылке"),
        ["Dlg_AddLinkHint"] = (
            "A GitHub repository link (we take the latest release), a Nexus Mods mod page, or a direct link to a .dll / .zip. " +
            "The manager remembers the source and finds updates by itself.",
            "Ссылка на репозиторий GitHub (возьмём последний релиз), страницу мода на Nexus Mods или прямая ссылка на .dll / .zip. " +
            "Менеджер запомнит источник и будет сам находить обновления."),
        ["Dlg_SourceTitle"] = ("Update source: {0}", "Источник обновлений: {0}"),
        ["Dlg_SourceHint"] = (
            "A GitHub repository link, a Nexus Mods mod page, or a direct link to a .dll / .zip. Leave the field empty to remove the source. " +
            "The installed version is unknown, so the manager will immediately offer to update to the latest one.",
            "Ссылка на репозиторий GitHub, страницу мода на Nexus Mods или прямая ссылка на .dll / .zip. Оставь поле пустым, чтобы убрать источник. " +
            "Версия уже установленного мода неизвестна, поэтому менеджер сразу предложит обновиться до последней."),
        ["Dlg_KeyTitle"] = ("Nexus Mods API key", "API-ключ Nexus Mods"),
        ["Dlg_KeyHint"] = (
            "Your personal key: nexusmods.com → profile icon → Site preferences → API Keys → “Personal API Key”. " +
            "The key is stored only on this computer and is not included in packs (.cumods).",
            "Личный ключ: nexusmods.com → иконка профиля → Site preferences → вкладка API Keys → «Personal API Key». " +
            "Ключ хранится только на этом компьютере и в сборки (.cumods) не попадает."),
        ["Dlg_KeyEmpty"] = ("Paste the key", "Вставь ключ"),

        // ---------- Настройки ----------
        ["Set_Title"] = ("Settings", "Настройки"),
        ["Set_Language"] = ("Language", "Язык"),
        ["Set_LanguageHint"] = ("Takes effect immediately.", "Применяется сразу."),
        ["Set_Close"] = ("Close", "Закрыть"),

        // ---------- Статусы главного окна ----------
        ["S_GameNotFoundHint"] = (
            "Game not found — click the red chip and choose the folder",
            "Игра не найдена — нажми на красную плашку и укажи папку"),
        ["S_InstallBepHint"] = ("Install BepInEx first — click the yellow chip", "Сначала установи BepInEx — нажми на жёлтую плашку"),
        ["S_FoundInGame"] = ("Found in the game folder: {0} {1}", "Найдено в папке игры: {0} {1}"),
        ["S_NoNewMods"] = ("No new mods found in the game folder", "Новых модов в папке игры не найдено"),
        ["S_ScanFailed"] = ("Couldn't scan the game folder: {0}", "Не удалось просканировать папку игры: {0}"),
        ["S_PickGameFirst"] = ("Choose the game folder first", "Сначала укажи папку игры"),
        ["S_Count"] = ("{0} {1} · {2} enabled", "{0} {1} · включено {2}"),
        ["S_CountPlain"] = ("{0} {1}", "{0} {1}"),
        ["S_NoExeInFolder"] = ("This folder doesn't contain {0}", "В этой папке нет {0}"),
        ["S_GameFolderUpdated"] = ("Game folder updated", "Папка игры обновлена"),
        ["S_BepAlready"] = ("BepInEx is already installed", "BepInEx уже установлен"),
        ["S_BepDownloading"] = ("Downloading BepInEx…", "Скачиваю BepInEx…"),
        ["S_BepInstalled"] = (
            "BepInEx installed. Run the game once so it creates its config files.",
            "BepInEx установлен. Запусти игру один раз, чтобы он создал конфиги."),
        ["S_Error"] = ("Error: {0}", "Ошибка: {0}"),
        ["S_BepFailed"] = ("Couldn't download BepInEx: {0}", "Не удалось скачать BepInEx: {0}"),
        ["S_NoGameFolder"] = ("No game folder selected", "Папка игры не выбрана"),
        ["S_Added"] = ("Added: {0}", "Добавлено: {0}"),
        ["S_ModEnabled"] = ("{0} enabled", "{0} включён"),
        ["S_ModDisabled"] = ("{0} disabled", "{0} выключен"),
        ["S_Removed"] = ("Removed: {0}", "Удалено: {0}"),
        ["S_NothingToExport"] = ("Nothing to export — add at least one mod", "Нечего экспортировать — добавь хотя бы один мод"),
        ["S_PackSaved"] = ("Pack saved ({0} {1}). Send the file to your friends.", "Сборка сохранена ({0} {1}). Отправь файл друзьям."),
        ["S_ExportFailed"] = ("Export failed: {0}", "Ошибка экспорта: {0}"),
        ["S_NoSources"] = (
            "None of the mods has an update source — click the link icon on a card or add a mod with “From link”",
            "Ни у одного мода не задан источник — нажми значок ссылки на карточке или добавь мод кнопкой «По ссылке»"),
        ["S_CheckingUpdates"] = ("Checking for updates…", "Проверяю обновления…"),
        ["S_UpdatesAvailable"] = ("Updates available: {0}", "Доступны обновления: {0}"),
        ["S_SomeCheckFailed"] = ("Couldn't check some mods — see the cards for details", "Часть модов проверить не удалось — подробности на карточках"),
        ["S_AllUpToDate"] = ("All mods are up to date", "Все моды актуальны"),
        ["S_UpdateCheckError"] = ("Update check failed: {0}", "Ошибка проверки обновлений: {0}"),
        ["S_Updating"] = ("Updating {0}…", "Обновляю {0}…"),
        ["S_Updated"] = ("{0} updated to {1}", "{0} обновлён до {1}"),
        ["S_ModUpdateFailed"] = ("Update failed: {0}", "Не удалось обновить: {0}"),
        ["S_UpdateFailedShort"] = ("{0}: update failed", "{0}: не удалось обновить"),
        ["S_UpdatedCount"] = ("Mods updated: {0}", "Обновлено модов: {0}"),
        ["S_UpdatedPartial"] = ("Updated: {0} of {1}", "Обновлено: {0} из {1}"),
        ["S_DownloadingMod"] = ("Downloading mod…", "Скачиваю мод…"),
        ["S_AddedMod"] = ("Added: {0}", "Добавлено: {0}"),
        ["S_SourceRemoved"] = ("Source removed from {0}", "Источник у {0} убран"),
        ["S_Launching"] = ("Launching the game…", "Запускаю игру…"),
        ["S_LaunchFailed"] = ("Couldn't launch: {0}", "Не удалось запустить: {0}"),
        ["S_Busy"] = ("Please wait for the current operation to finish", "Подожди, пока закончится текущая операция"),

        // ---------- Статусы Nexus ----------
        ["S_CheckingKey"] = ("Checking the Nexus key…", "Проверяю ключ Nexus…"),
        ["S_NexusAs"] = ("Nexus: {0} ({1})", "Nexus: {0} ({1})"),
        ["S_NxmOff"] = ("nxm link handling turned off", "Приём nxm-ссылок выключен"),
        ["S_NxmOn"] = (
            "nxm link handling turned on: download buttons on Nexus now open this manager",
            "Приём nxm-ссылок включён: кнопки скачивания на Nexus теперь открывают этот менеджер"),
        ["S_NxmFailed"] = ("Couldn't change the nxm handler: {0}", "Не удалось изменить обработчик nxm: {0}"),
        ["S_NexusClickSlow"] = (
            "{0}: click “Slow download” on the page that just opened — the manager will download and install the mod by itself",
            "{0}: на открывшейся странице нажми «Slow download» — менеджер скачает и поставит мод сам"),
        ["S_NexusEnableNxm"] = (
            "{0}: turn on “Accept nxm links” in the Nexus tab, otherwise the manager won't get the file. Or download the .zip and drop it into the window.",
            "{0}: включи «Приём nxm-ссылок» на вкладке Nexus, иначе менеджер не получит файл. Или скачай .zip и перетащи в окно."),
        ["S_NexusAlready"] = (
            "{0} is already in the list — use “Updates” to look for new versions",
            "{0} уже есть в списке — обновления ищи кнопкой «Обновления»"),
        ["S_NexusDownloading"] = ("Downloading {0} from Nexus…", "Скачиваю {0} с Nexus…"),
        ["S_NexusDownloadingAny"] = ("Downloading the mod from Nexus…", "Скачиваю мод с Nexus…"),
        ["S_NexusInstalled"] = ("Installed from Nexus: {0}", "Установлено с Nexus: {0}"),
        ["Nx_ModN"] = ("Mod #{0}", "Мод #{0}"),

        // ---------- Вкладка Nexus ----------
        ["Nx_NeedKey"] = ("API key needed", "Нужен API-ключ"),
        ["Nx_KeySet"] = ("key set", "ключ задан"),
        ["Nx_Premium"] = ("Premium", "Premium"),
        ["Nx_FreeAccount"] = ("free account", "бесплатный аккаунт"),
        ["Nx_AccountFree"] = ("free", "бесплатный"),
        ["Nx_Account"] = ("Nexus: {0} · {1}", "Nexus: {0} · {1}"),
        ["Tip_Account"] = ("Enter or change your personal Nexus Mods API key", "Указать или сменить личный API-ключ Nexus Mods"),
        ["Nx_NxmOn"] = ("Accept nxm links: on", "Приём nxm-ссылок: вкл"),
        ["Nx_NxmOff"] = ("Accept nxm links: off", "Приём nxm-ссылок: выкл"),
        ["Tip_Nxm"] = (
            "The manager catches nxm:// links from the download button on Nexus. While this is on, it replaces Vortex / Mod Organizer as the link handler.",
            "Менеджер ловит ссылки nxm:// с кнопки скачивания на сайте Nexus. Это заменит Vortex / Mod Organizer в роли обработчика ссылок, пока приём включён."),
        ["Nx_FreeNote"] = (
            "Free account: after “Install” the mod page opens on Nexus. Click “Slow download” (or “Mod manager download”) there and the manager downloads and installs the mod by itself. Accepting nxm links must be turned on for this. With Premium the mod installs right away.",
            "Бесплатный аккаунт: после «Установить» откроется страница мода на Nexus. Нажми там «Slow download» (или «Mod manager download»), и менеджер сам скачает и поставит мод. Для этого должен быть включён приём nxm-ссылок. У Premium мод ставится сразу."),
        ["Nx_Popular"] = ("Popular", "Популярные"),
        ["Nx_Newest"] = ("New", "Новые"),
        ["Nx_Updated"] = ("Updated", "Обновлённые"),
        ["Nx_Endorsed"] = ("Most liked", "По лайкам"),
        ["Nx_Install"] = ("Install", "Установить"),
        ["Nx_Installing"] = ("Installing…", "Устанавливаю…"),
        ["Nx_Installed"] = ("Installed", "Установлен"),
        ["Tip_OpenPage"] = ("Open the mod page on Nexus Mods", "Открыть страницу мода на Nexus Mods"),
        ["Nx_Downloads"] = ("{0} downloads", "скачиваний {0}"),
        ["Nx_Likes"] = ("{0} likes", "лайков {0}"),
        ["Nx_Loading"] = ("Loading the catalog…", "Загружаю каталог…"),
        ["Nx_NoKeyHint"] = (
            "To browse the catalog you need a personal Nexus Mods API key. Click the “API key needed” chip.",
            "Чтобы смотреть каталог, нужен личный API-ключ Nexus Mods. Нажми плашку «Нужен API-ключ»."),
        ["Nx_NoKeyStatus"] = ("No API key set", "API-ключ не задан"),
        ["Nx_NothingFound"] = ("Nothing found", "Ничего не найдено"),
        ["Nx_Fallback"] = (
            "Searching the whole catalog is unavailable right now — showing Nexus featured lists (10 mods each)",
            "Поиск по всему каталогу сейчас недоступен — показаны подборки Nexus (по 10 модов)"),
        ["Nx_Shown"] = ("Showing {0} of {1}", "Показано {0} из {1}"),
        ["Nx_More"] = ("Show more", "Показать ещё"),
        ["Nx_BrowserFailed"] = ("Couldn't open the browser: {0}", "Не удалось открыть браузер: {0}"),

        // ---------- Ошибки ----------
        ["E_OnlyDllZip"] = ("Only .dll and .zip files are supported", "Поддерживаются только .dll и .zip"),
        ["E_NoPendingUpdate"] = ("No update available", "Нет доступного обновления"),
        ["E_NoSource"] = ("This mod has no source", "У мода нет источника"),
        ["E_UnsupportedFile"] = ("Downloaded an unsupported file: {0}", "Скачан неподдерживаемый файл: {0}"),
        ["E_NexusFileGone"] = ("This file is no longer on Nexus", "Этого файла больше нет на Nexus"),
        ["E_NexusNoFiles"] = ("This mod has no files on Nexus", "У мода на Nexus нет файлов"),
        ["E_NexusBadType"] = (
            "The file “{0}” is not a .dll or .zip — the manager can't unpack it. Download and install it manually.",
            "Файл «{0}» не .dll и не .zip — менеджер такое не распаковывает. Скачай и поставь вручную."),
        ["E_NoGameFolder"] = ("No game folder selected", "Папка игры не выбрана"),
        ["E_BepMissing"] = ("BepInEx is not installed — click “Install BepInEx”", "BepInEx не установлен — нажми «Установить BepInEx»"),
        ["E_NotAPack"] = ("This is not a pack file (manifest.json is missing)", "Это не файл сборки (нет manifest.json)"),
        ["E_BadArchivePath"] = ("Invalid path in the archive: {0}", "Недопустимый путь в архиве: {0}"),
        ["E_NexusNeedKey"] = ("A Nexus Mods API key is required", "Нужен API-ключ Nexus Mods"),
        ["E_NexusKeyRejected"] = (
            "Nexus rejected the API key — check it (Nexus tab → account chip)",
            "Nexus не принял API-ключ — проверь его (вкладка Nexus → плашка аккаунта)"),
        ["E_NexusFreeNoDirect"] = (
            "Nexus doesn't give free accounts a direct download link — the file has to be downloaded through the website",
            "Nexus не отдаёт ссылку на скачивание бесплатным аккаунтам напрямую — файл нужно скачать через сайт"),
        ["E_NexusNotFound"] = ("Mod or file not found on Nexus", "Мод или файл не найден на Nexus"),
        ["E_NexusRateLimit"] = ("Nexus request limit reached, try again later", "Лимит запросов Nexus исчерпан, попробуй позже"),
        ["E_NexusHttp"] = ("Nexus returned error {0}", "Nexus ответил ошибкой {0}"),
        ["E_GraphQlBad"] = ("Unexpected GraphQL response", "Неожиданный ответ GraphQL"),
        ["E_NexusNoLink"] = ("Nexus didn't return a download link", "Nexus не вернул ссылку на скачивание"),
        ["E_NotNxm"] = ("This is not an nxm link", "Это не nxm-ссылка"),
        ["E_NxmNotMod"] = (
            "The manager only accepts nxm links to single mods (not collections)",
            "Менеджер умеет принимать только nxm-ссылки на отдельные моды (не коллекции)"),
        ["E_NxmWrongGame"] = ("This link is for another game ({0})", "Эта ссылка для другой игры ({0})"),
        ["E_NoExePath"] = ("Couldn't determine the program path", "Не удалось определить путь к программе"),
        ["E_DotnetRun"] = (
            "Running via “dotnet run” — build the .exe (dotnet publish) and turn on link handling from it",
            "Запущено через «dotnet run» — собери .exe (dotnet publish) и включи приём ссылок из него"),
        ["E_NotALink"] = ("This doesn't look like a link", "Это не похоже на ссылку"),
        ["E_BadLink"] = (
            "Enter a GitHub repository link, a Nexus Mods mod page, or a direct link to a .dll / .zip",
            "Нужна ссылка на репозиторий GitHub, страницу мода на Nexus Mods или прямая ссылка на .dll / .zip"),
        ["E_NoReleases"] = (
            "The repository has no releases — paste a direct link to a .dll / .zip",
            "У репозитория нет релизов — вставь прямую ссылку на .dll / .zip"),
        ["E_GitHubRate"] = ("GitHub request limit reached, try again in an hour", "Лимит запросов GitHub, попробуй через час"),
        ["E_NoAssets"] = ("The release has no .dll or .zip files", "В релизе нет файлов .dll или .zip"),

        // ---------- Карточка мода ----------
        ["S_ModCheckFailed"] = ("Update check failed: {0}", "Проверка обновления не удалась: {0}"),
        ["Src_Link"] = ("Link", "Ссылка"),
        ["Src_Known"] = ("{0} · {1}", "{0} · {1}"),
        ["Src_Unknown"] = ("{0} · version unknown", "{0} · версия неизвестна"),

        // ---------- Настройки: тема, обновления, диагностика ----------
        ["Set_Theme"] = ("Theme", "Тема"),
        ["Theme_Dark"] = ("Dark", "Тёмная"),
        ["Theme_Light"] = ("Light", "Светлая"),
        ["Theme_System"] = ("System", "Как в системе"),
        ["Set_Updates"] = ("Updates", "Обновления"),
        ["Set_Version"] = ("Version {0}", "Версия {0}"),
        ["Set_CheckUpdates"] = ("Check for updates", "Проверить обновления"),
        ["Set_AutoCheck"] = ("Check for updates on startup", "Проверять обновления при запуске"),
        ["Set_Diagnostics"] = ("Bug report", "Багрепорт"),
        ["Set_DiagHint"] = (
            "Copies versions, game and BepInEx state, the mod list and recent log errors. API keys are not included.",
            "Копирует версии, состояние игры и BepInEx, список модов и последние ошибки из лога. API-ключи не включаются."),
        ["Set_CopyReport"] = ("Copy report", "Скопировать отчёт"),
        ["Set_Copied"] = ("Copied to clipboard", "Скопировано в буфер обмена"),
        ["Set_CopyFailed"] = ("Couldn't copy: {0}", "Не удалось скопировать: {0}"),

        ["Upd_Checking"] = ("Checking for updates…", "Проверяю обновления…"),
        ["Upd_UpToDate"] = ("You have the latest version", "Установлена последняя версия"),
        ["Upd_Available"] = ("New version {0} is available", "Доступна новая версия {0}"),
        ["Upd_Button"] = ("Update manager → {0}", "Обновить менеджер → {0}"),
        ["Upd_Failed"] = ("Update check failed: {0}", "Не удалось проверить обновление: {0}"),
        ["Upd_Confirm"] = (
            "Install version {0} and restart the manager?\n\nYour mods and settings are kept.",
            "Установить версию {0} и перезапустить менеджер?\n\nМоды и настройки сохранятся."),
        ["Upd_ConfirmTitle"] = ("Manager update", "Обновление менеджера"),
        ["Upd_Downloading"] = ("Downloading manager {0}… {1}%", "Скачиваю менеджер {0}… {1}%"),
        ["Upd_Installing"] = ("Installing…", "Устанавливаю…"),
        ["Upd_Restarting"] = ("Restarting…", "Перезапускаю…"),
        ["Upd_Error"] = ("Couldn't update the manager: {0}", "Не удалось обновить менеджер: {0}"),
        ["E_GitHubStatus"] = ("GitHub returned error {0}", "GitHub ответил ошибкой {0}"),
        ["E_NoAppAsset"] = (
            "Release {0} has no manager file (.exe or .zip)",
            "В релизе {0} нет файла менеджера (.exe или .zip)"),
        ["E_SelfUpdateUnsupported"] = (
            "Self-update only works when run from the built .exe",
            "Автообновление работает только из собранного .exe"),
        ["E_HashMismatch"] = ("The downloaded file is damaged (checksum mismatch)", "Скачанный файл повреждён (не совпала контрольная сумма)"),
        ["E_NoExeInZip"] = ("The archive doesn't contain {0}", "В архиве нет файла {0}"),
    };
}
