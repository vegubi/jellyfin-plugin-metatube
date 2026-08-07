using Jellyfin.Plugin.MetaTube.Helpers;
using Jellyfin.Plugin.MetaTube.Translation;

using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MetaTube.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string Server { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public bool EnableCollections { get; set; } = false;

    public bool EnableDirectors { get; set; } = true;

    public bool EnableRatings { get; set; } = true;

    public bool EnableTrailers { get; set; } = false;

    public bool EnableRealActorNames { get; set; } = false;

    public bool EnableBadges { get; set; } = false;

    public string BadgeUrl { get; set; } = "zimu.png";

    public double PrimaryImageRatio { get; set; } = -1;

    public int DefaultImageQuality { get; set; } = 90;

    public bool EnableMovieProviderFilter { get; set; } = false;

    public string RawMovieProviderFilter
    {
        get => _movieProviderFilter?.Any() == true ? string.Join(',', _movieProviderFilter) : string.Empty;
        set => _movieProviderFilter = value?.Split(',').Select(s => s.Trim()).Where(s => s.Any())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public List<string> GetMovieProviderFilter()
    {
        return _movieProviderFilter;
    }

    private List<string> _movieProviderFilter;

    public bool EnableTemplate { get; set; } = false;

    public string NameTemplate { get; set; } = DefaultNameTemplate;

    public string TaglineTemplate { get; set; } = DefaultTaglineTemplate;

    public static string DefaultNameTemplate => "{number} {title}";

    public static string DefaultTaglineTemplate => "配信開始日 {date}";

    public bool EnableTitleTranslation { get; set; } = false;

    public bool EnableSummaryTranslation { get; set; } = false;

    public bool EnableActorTranslation { get; set; } = false;

    public bool EnableGenreTranslation { get; set; } = false;

    public TranslationEngine TranslationEngine { get; set; } = TranslationEngine.Baidu;

    public string BaiduAppId { get; set; } = string.Empty;

    public string BaiduAppKey { get; set; } = string.Empty;

    public string GoogleApiKey { get; set; } = string.Empty;

    public string GoogleApiUrl { get; set; } = string.Empty;

    public string DeepLApiKey { get; set; } = string.Empty;

    public string DeepLApiUrl { get; set; } = string.Empty;

    public string OpenAiApiKey { get; set; } = string.Empty;

    public string OpenAiApiUrl { get; set; } = string.Empty;

    public string OpenAiModel { get; set; } = string.Empty;

}