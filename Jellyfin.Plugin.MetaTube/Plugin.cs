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

    internal static void LogTranslation(string message) => Instance?.AppendTranslationLog(message);

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
        string foundFilename,
        bool isActor,
        bool enableTranslation,
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

        var foundFilePath = Path.Combine(GetPluginConfigFolder(), foundFilename);
        var existingFoundLines = File.Exists(foundFilePath)
            ? File.ReadAllLines(foundFilePath).Select(line => line.Trim()).Where(line => !string.IsNullOrWhiteSpace(line)).ToList()
            : new List<string>();

        var remainingFoundLines = new List<string>();
        var existingFoundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in existingFoundLines)
        {
            var key = ParseFoundKey(line);
            if (!string.IsNullOrWhiteSpace(key) && !TryGetSubstitutionValue(key, substitutionTable, out _, out _))
            {
                remainingFoundLines.Add(line);
                existingFoundKeys.Add(key);
            }
        }

        foreach (var item in uniqueItems)
        {
            if (TryGetSubstitutionValue(item, substitutionTable, out _, out _))
                continue;

            string translation = null;
            if (enableTranslation)
            {
                try
                {
                    var translated = await TranslationHelper.TranslateTextAsync(item, "en", cancellationToken);
                    if (!string.IsNullOrWhiteSpace(translated) &&
                        !string.Equals(translated, item, StringComparison.OrdinalIgnoreCase))
                    {
                        translation = translated;
                        Plugin.LogTranslation($"{(isActor ? "Actor" : "Genre")} translation: {item} => {translated}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Failed to translate metadata entry '{0}': {1}", item, ex.Message);
                }
            }

            translations[item] = translation;

            if (!existingFoundKeys.Contains(item))
            {
                remainingFoundLines.Add(FormatMetadataEntry(item, isActor, translation));
                existingFoundKeys.Add(item);
            }
        }

        File.WriteAllLines(foundFilePath, remainingFoundLines);

        return translations;
    }

    private static string ParseFoundKey(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        var parts = line.Split('=', 2);
        return parts[0].Trim();
    }

    internal static bool TryGetSubstitutionValue(string item, SubstitutionTable substitutionTable,
        out string substitutionValue, out bool isBlank)
    {
        substitutionValue = null;
        isBlank = false;

        if (item == null)
            return false;

        if (substitutionTable.TryGetValue(item, out var value))
        {
            substitutionValue = value?.Trim();
            isBlank = string.IsNullOrWhiteSpace(substitutionValue);
            return true;
        }

        var blankKey = $"__{item}";
        if (substitutionTable.TryGetValue(blankKey, out value))
        {
            substitutionValue = null;
            isBlank = true;
            return true;
        }

        return false;
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