using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.Anirena
{
    public class Anirena : HttpIndexerBase<AnirenaSettings>
    {
        public override string Name => "Anirena";

        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;

        public Anirena(IHttpClient httpClient, IIndexerStatusService indexerStatusService, IConfigService configService, IParsingService parsingService, Logger logger, ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, configService, parsingService, logger, localizationService)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new AnirenaRequestGenerator() { Settings = Settings };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new TorrentRssParser() { UseGuidInfoUrl = true, SizeElementName = "size", InfoHashElementName = "infoHash", PeersElementName = "leechers", CalculatePeersAsSum = true, SeedsElementName = "seeders" };
        }
    }
}
