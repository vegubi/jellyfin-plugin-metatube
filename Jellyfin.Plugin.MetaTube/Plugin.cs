using Jellyfin.Plugin.MetaTube.Configuration;
using Jellyfin.Plugin.MetaTube.Helpers;
using Jellyfin.Plugin.MetaTube.Translation;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Logging;

using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.MetaTube;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<Plugin> _logger;
    private readonly IApplicationPaths _applicationPaths;
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger) : base(applicationPaths,
        xmlSerializer)
    {
        Instance = this;
        _logger = logger;
        _applicationPaths = applicationPaths;
    }
    public const string ProviderName = "MetaTube";
    public const string ProviderId = "MetaTube";
    public override string Name => ProviderName;
    public override string Description => "MetaTube Plugin for Jellyfin/Emby";
    public override Guid Id => Guid.Parse("01cc53ec-c415-4108-bbd4-a684a9801a32");
    public static Plugin Instance { get; private set; }
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = "MetaTube",
                EnableInMainMenu = true,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
            }
        };
    }

    // Helper method to resolve path and safely load a substitution table
    private SubstitutionTable GetSubstitutionTableFromFile(string filename, string defaultContent = "")
    {
        string pluginConfigFolder = Path.Combine(_applicationPaths.PluginConfigurationsPath, GetType().Namespace);
        string targetFilePath = Path.Combine(pluginConfigFolder, filename);

        try
        {
            if (!Directory.Exists(pluginConfigFolder))
            {
                Directory.CreateDirectory(pluginConfigFolder);
            }

            if (!File.Exists(targetFilePath))
            {
                File.WriteAllText(targetFilePath, defaultContent);
                _logger.LogInformation("Created missing substitution file: {0}", targetFilePath);
            }

            string fileContent = File.ReadAllText(targetFilePath);
            return SubstitutionTable.Parse(fileContent);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to read substitution file {0}: {1}", filename, ex.Message);
            return SubstitutionTable.Parse(string.Empty); // Fallback to an empty table on error
        }
    }

    // Expose the custom file-based substitution tables publicly
    public SubstitutionTable GetActorSubstitutionTableFromFile() =>
        GetSubstitutionTableFromFile("substitutions_actor.txt", "John Doe=Jonathan Doe\nJane Doe=Jennifer Doe");

    public SubstitutionTable GetTitleSubstitutionTableFromFile() =>
        GetSubstitutionTableFromFile("substitutions_title.txt", "Original Title=New Title");

    public SubstitutionTable GetGenreSubstitutionTableFromFile() =>
        GetSubstitutionTableFromFile("substitutions_genre.txt", "Original Genre=New Genre");

    private string GetPluginConfigFolder()
    {
        var pluginConfigFolder = Path.Combine(_applicationPaths.PluginConfigurationsPath, GetType().Namespace);
        if (!Directory.Exists(pluginConfigFolder))
        {
            Directory.CreateDirectory(pluginConfigFolder);
        }

        return pluginConfigFolder;
    }

    private string GetTranslationLogFilePath() => Path.Combine(GetPluginConfigFolder(), "translation_log.txt");

    private void AppendTranslationLog(string message)
    {
        var logFilePath = GetTranslationLogFilePath();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        File.AppendAllText(logFilePath, $"[{timestamp}] {message}{Environment.NewLine}");
    }

    private readonly object _fileLock = new object();

    public async Task<Dictionary<string, string>> TrackAndLogMetadataAsync(
        IEnumerable<string> incomingItems,
        SubstitutionTable substitutionTable,
        string listFilename,
        string newFilename,
        bool isActor,
        CancellationToken cancellationToken)
    {
        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (incomingItems == null)
            return translations;

        var uniqueItems = incomingItems
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!uniqueItems.Any())
            return translations;

        foreach (var item in uniqueItems)
        {
            if (substitutionTable.TryGetValue(item, out var substitutionValue))
            {
                translations[item] = substitutionValue?.Trim();
                continue;
            }

            try
            {
                var translated = await TranslationHelper.TranslateTextAsync(item, "en", cancellationToken);
                if (!string.IsNullOrWhiteSpace(translated) &&
                    !string.Equals(translated, item, StringComparison.OrdinalIgnoreCase))
                {
                    translations[item] = translated;
                    AppendTranslationLog($"{(isActor ? "Actor" : "Genre")} translation: {item} => {translated}");
                }
                else
                {
                    translations[item] = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to translate metadata entry '{0}': {1}", item, ex.Message);
                translations[item] = null;
            }
        }

        string pluginConfigFolder = GetPluginConfigFolder();
        string listFilePath = Path.Combine(pluginConfigFolder, listFilename);
        string newFilePath = Path.Combine(pluginConfigFolder, newFilename);

        lock (_fileLock)
        {
            // 1. Load existing track list into a case-insensitive set
            var existingListItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(listFilePath))
            {
                foreach (var line in File.ReadLines(listFilePath))
                {
                    var key = line.Split('=', 2).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(key)) existingListItems.Add(key);
                }
            }

            // 2. Load existing new items list into a case-insensitive set
            var existingNewItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(newFilePath))
            {
                foreach (var line in File.ReadLines(newFilePath))
                {
                    var key = line.Split('=', 2).FirstOrDefault()?.Trim();
                    if (!string.IsNullOrWhiteSpace(key)) existingNewItems.Add(key);
                }
            }

            var newEntriesForList = new List<string>();
            var newEntriesForNewFile = new List<string>();

            foreach (var item in uniqueItems)
            {
                translations.TryGetValue(item, out var translation);
                var formattedEntry = FormatMetadataEntry(item, isActor, translation);

                // If it's completely brand new to our lifetime tracker list
                if (!existingListItems.Contains(item))
                {
                    newEntriesForList.Add(formattedEntry);
                    existingListItems.Add(item); // Avoid duplicates within the same batch
                }

                // Check if it lacks a translation mapping in your active substitution file
                if (!substitutionTable.ContainsKey(item))
                {
                    // If it isn't already noted down in the "new items needing translation" scratchpad
                    if (!existingNewItems.Contains(item))
                    {
                        newEntriesForNewFile.Add(formattedEntry);
                        existingNewItems.Add(item);
                    }
                }
            }

            // 3. Commit changes securely to disk
            if (newEntriesForList.Any())
            {
                File.AppendAllLines(listFilePath, newEntriesForList);
                _logger.LogInformation("Logged {0} new unique items to tracker file: {1}", newEntriesForList.Count, listFilename);
            }

            if (newEntriesForNewFile.Any())
            {
                File.AppendAllLines(newFilePath, newEntriesForNewFile);
                _logger.LogWarning("Logged {0} missing translations to patch file: {1}", newEntriesForNewFile.Count, newFilename);
            }
        }

        return translations;
    }

    private static string FormatMetadataEntry(string key, bool isActor, string translation)
    {
        if (isActor)
        {
            if (!string.IsNullOrWhiteSpace(translation) &&
                !string.Equals(key, translation, StringComparison.OrdinalIgnoreCase))
            {
                return $"{key}={key} ({translation})";
            }

            return $"{key}={key}";
        }

        if (!string.IsNullOrWhiteSpace(translation) &&
            !string.Equals(key, translation, StringComparison.OrdinalIgnoreCase))
        {
            return $"{key}={translation}";
        }

        return $"{key}={key}";
    }
}