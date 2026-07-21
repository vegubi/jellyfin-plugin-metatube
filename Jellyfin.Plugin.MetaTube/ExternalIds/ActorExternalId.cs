using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;


namespace Jellyfin.Plugin.MetaTube.ExternalIds;

public class ActorExternalId : BaseExternalId
{
    public override ExternalIdMediaType? Type => ExternalIdMediaType.Person;
    public override bool Supports(IHasProviderIds item)
    {
        return item is Person;
    }
}