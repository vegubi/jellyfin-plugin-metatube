using Jellyfin.Plugin.MetaTube.Configuration;
using Jellyfin.Plugin.MetaTube.Helpers;
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
        // _logger.LogInformation("--- METATUBE PLUGIN PATH DEBUGGING ---");
        // _logger.LogInformation("ConfigurationDirectoryPath: {0}", _applicationPaths.ConfigurationDirectoryPath.ToString());

        // string pluginConfigFolder = Path.Combine(_applicationPaths.PluginConfigurationsPath, GetType().Namespace);
        // string targetFilePath = Path.Combine(pluginConfigFolder, "substitutions_actor.txt");

        // try
        // {
        //     // Ensure the folder exists. Docker will safely create it inside /config/...
        //     if (!Directory.Exists(pluginConfigFolder))
        //     {
        //         Directory.CreateDirectory(pluginConfigFolder);
        //         _logger.LogInformation("Created missing plugin configuration folder at: {0}", pluginConfigFolder);
        //     }

        //     // Ensure the text file exists. If missing, create it with sample instructions
        //     if (!File.Exists(targetFilePath))
        //     {
        //         File.WriteAllText(targetFilePath, "John Doe=Jonathan Doe\nJane Doe=Jennifer Doe");
        //         _logger.LogInformation("Created placeholder file at: {0}", targetFilePath);
        //     }

        //     // Read the content of the file and output it to the log
        //     string fileContent = File.ReadAllText(targetFilePath);

        //     _logger.LogInformation("--- START OF substitutions_actor.txt ---");
        //     _logger.LogInformation("{0}", fileContent);
        //     _logger.LogInformation("--- END OF substitutions_actor.txt ---");
        // }
        // catch (Exception ex)
        // {
        //     _logger.LogError("Failed to handle substitutions file: {0}", ex.Message);
        // }

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
}