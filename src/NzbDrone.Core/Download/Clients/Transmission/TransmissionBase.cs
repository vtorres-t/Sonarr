using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.TorrentInfo;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Transmission
{
    public abstract class TransmissionBase : TorrentClientBase<TransmissionSettings>
    {
        public abstract bool SupportsLabels { get; }

        protected readonly ITransmissionProxy _proxy;

        public TransmissionBase(ITransmissionProxy proxy,
            ITorrentFileInfoReader torrentFileInfoReader,
            IHttpClient httpClient,
            IConfigService configService,
            IDiskProvider diskProvider,
            IRemotePathMappingService remotePathMappingService,
            ILocalizationService localizationService,
            IBlocklistService blocklistService,
            Logger logger)
            : base(torrentFileInfoReader, httpClient, configService, diskProvider, remotePathMappingService, localizationService, blocklistService, logger)
        {
            _proxy = proxy;
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            var configFunc = new Lazy<TransmissionConfig>(() => _proxy.GetConfig(Settings));
            var torrents = _proxy.GetTorrents(null, Settings);

            var items = new List<DownloadClientItem>();

            foreach (var torrent in torrents)
            {
                var outputPath = new OsPath(torrent.DownloadDir);

                if (Settings.TvCategory.IsNotNullOrWhiteSpace() && SupportsLabels && torrent.Labels is { Count: > 0 })
                {
                    if (!torrent.Labels.Contains(Settings.TvCategory, StringComparer.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }
                }
                else
                {
                    if (Settings.TvDirectory.IsNotNullOrWhiteSpace())
                    {
                        if (!new OsPath(Settings.TvDirectory).Contains(outputPath))
                        {
                            continue;
                        }
                    }
                    else if (Settings.TvCategory.IsNotNullOrWhiteSpace())
                    {
                        var directories = outputPath.FullPath.Split('\\', '/');
                        if (!directories.Contains(Settings.TvCategory))
                        {
                            continue;
                        }
                    }
                }

                outputPath = _remotePathMappingService.RemapRemoteToLocal(Settings.Host, outputPath);

                var item = new DownloadClientItem
                {
                    DownloadId = torrent.HashString.ToUpper(),
                    Category = Settings.TvCategory,
                    Title = torrent.Name,
                    DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, Settings.TvImportedCategory.IsNotNullOrWhiteSpace() && SupportsLabels),
                    OutputPath = GetOutputPath(outputPath, torrent),
                    TotalSize = torrent.TotalSize,
                    RemainingSize = torrent.LeftUntilDone,
                    SeedRatio = torrent.DownloadedEver <= 0 ? 0 : (double)torrent.UploadedEver / torrent.DownloadedEver
                };

                if (torrent.Eta >= 0)
                {
                    try
                    {
                        item.RemainingTime = TimeSpan.FromSeconds(torrent.Eta);
                    }
                    catch (OverflowException)
                    {
                        item.RemainingTime = TimeSpan.FromMilliseconds(torrent.Eta);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        item.RemainingTime = TimeSpan.FromMilliseconds(torrent.Eta);
                    }
                }

                if (!torrent.ErrorString.IsNullOrWhiteSpace())
                {
                    item.Status = DownloadItemStatus.Warning;
                    item.Message = _localizationService.GetLocalizedString("DownloadClientItemErrorMessage", new Dictionary<string, object>
                    {
                        { "clientName", Name },
                        { "message", torrent.ErrorString }
                    });
                }
                else if (torrent.TotalSize == 0)
                {
                    item.Status = DownloadItemStatus.Queued;
                }
                else if (torrent.LeftUntilDone == 0 && (torrent.Status == TransmissionTorrentStatus.Stopped ||
                                                        torrent.Status == TransmissionTorrentStatus.Seeding ||
                                                        torrent.Status == TransmissionTorrentStatus.SeedingWait))
                {
                    item.Status = DownloadItemStatus.Completed;
                }
                else if (torrent.IsFinished && torrent.Status != TransmissionTorrentStatus.Check &&
                         torrent.Status != TransmissionTorrentStatus.CheckWait)
                {
                    item.Status = DownloadItemStatus.Completed;
                }
                else if (torrent.Status == TransmissionTorrentStatus.Queued)
                {
                    item.Status = DownloadItemStatus.Queued;
                }
                else
                {
                    item.Status = DownloadItemStatus.Downloading;
                }

                item.CanBeRemoved = item.DownloadClientInfo.RemoveCompletedDownloads && HasReachedSeedLimit(torrent, item.SeedRatio, configFunc);
                item.CanMoveFiles = item.CanBeRemoved && torrent.Status == TransmissionTorrentStatus.Stopped;

                items.Add(item);
            }

            return items;
        }

        protected bool HasReachedSeedLimit(TransmissionTorrent torrent, double? ratio, Lazy<TransmissionConfig> config)
        {
            var isStopped = torrent.Status == TransmissionTorrentStatus.Stopped;
            var isSeeding = torrent.Status == TransmissionTorrentStatus.Seeding;

            if (torrent.SeedRatioMode == 1)
            {
                if (isStopped && ratio.HasValue && ratio >= torrent.SeedRatioLimit)
                {
                    return true;
                }
            }
            else if (torrent.SeedRatioMode == 0)
            {
                if (isStopped && config.Value.SeedRatioLimited && ratio >= config.Value.SeedRatioLimit)
                {
                    return true;
                }
            }

            // Transmission doesn't support SeedTimeLimit, use/abuse seed idle limit, but only if it was set per-torrent.
            if (torrent.SeedIdleMode == 1)
            {
                if ((isStopped || isSeeding) && torrent.SecondsSeeding > torrent.SeedIdleLimit * 60)
                {
                    return true;
                }
            }
            else if (torrent.SeedIdleMode == 0)
            {
                // The global idle limit is a real idle limit, if it's configured then 'Stopped' is enough.
                if (isStopped && config.Value.IdleSeedingLimitEnabled)
                {
                    return true;
                }
            }

            return false;
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            _proxy.RemoveTorrent(item.DownloadId.ToLower(), deleteData, Settings);
        }

        public override DownloadClientInfo GetStatus()
        {
            string destDir;

            if (Settings.TvDirectory.IsNotNullOrWhiteSpace())
            {
                destDir = Settings.TvDirectory;
            }
            else
            {
                var config = _proxy.GetConfig(Settings);
                destDir = config.DownloadDir;

                if (Settings.TvCategory.IsNotNullOrWhiteSpace())
                {
                    destDir = $"{destDir}/{Settings.TvCategory}";
                }
            }

            return new DownloadClientInfo
            {
                IsLocalhost = Settings.Host == "127.0.0.1" || Settings.Host == "localhost",
                OutputRootFolders = new List<OsPath> { _remotePathMappingService.RemapRemoteToLocal(Settings.Host, new OsPath(destDir)) }
            };
        }

        protected override string AddFromMagnetLink(RemoteEpisode remoteEpisode, string hash, string magnetLink)
        {
            _proxy.AddTorrentFromUrl(magnetLink, GetDownloadDirectory(), Settings);
            _proxy.SetTorrentSeedingConfiguration(hash, remoteEpisode.SeedConfiguration, Settings);

            var isRecentEpisode = remoteEpisode.IsRecentEpisode();

            if ((isRecentEpisode && Settings.RecentTvPriority == (int)TransmissionPriority.First) ||
                (!isRecentEpisode && Settings.OlderTvPriority == (int)TransmissionPriority.First))
            {
                _proxy.MoveTorrentToTopInQueue(hash, Settings);
            }

            return hash;
        }

        protected override string AddFromTorrentFile(RemoteEpisode remoteEpisode, string hash, string filename, byte[] fileContent)
        {
            _proxy.AddTorrentFromData(fileContent, GetDownloadDirectory(), Settings);
            _proxy.SetTorrentSeedingConfiguration(hash, remoteEpisode.SeedConfiguration, Settings);

            var isRecentEpisode = remoteEpisode.IsRecentEpisode();

            if ((isRecentEpisode && Settings.RecentTvPriority == (int)TransmissionPriority.First) ||
                (!isRecentEpisode && Settings.OlderTvPriority == (int)TransmissionPriority.First))
            {
                _proxy.MoveTorrentToTopInQueue(hash, Settings);
            }

            return hash;
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            failures.AddIfNotNull(TestConnection());
            if (failures.HasErrors())
            {
                return;
            }

            failures.AddIfNotNull(TestGetTorrents());
        }

        protected virtual OsPath GetOutputPath(OsPath outputPath, TransmissionTorrent torrent)
        {
            return outputPath + torrent.Name.Replace(":", "_");
        }

        protected string GetDownloadDirectory()
        {
            if (Settings.TvDirectory.IsNotNullOrWhiteSpace())
            {
                return Settings.TvDirectory;
            }

            if (!Settings.TvCategory.IsNotNullOrWhiteSpace())
            {
                return null;
            }

            var config = _proxy.GetConfig(Settings);
            var destDir = config.DownloadDir;

            return $"{destDir.TrimEnd('/')}/{Settings.TvCategory}";
        }

        protected ValidationFailure TestConnection()
        {
            try
            {
                return ValidateVersion();
            }
            catch (DownloadClientAuthenticationException ex)
            {
                _logger.Error(ex, ex.Message);
                return new NzbDroneValidationFailure("Username", _localizationService.GetLocalizedString("DownloadClientValidationAuthenticationFailure"))
                {
                    DetailedDescription = _localizationService.GetLocalizedString("DownloadClientValidationAuthenticationFailureDetail", new Dictionary<string, object> { { "clientName", Name } })
                };
            }
            catch (DownloadClientUnavailableException ex)
            {
                _logger.Error(ex, ex.Message);

                return new NzbDroneValidationFailure("Host", _localizationService.GetLocalizedString("DownloadClientValidationUnableToConnect", new Dictionary<string, object> { { "clientName", Name } }))
                       {
                           DetailedDescription = ex.Message
                       };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to test");

                return new NzbDroneValidationFailure(string.Empty, _localizationService.GetLocalizedString("DownloadClientValidationUnknownException", new Dictionary<string, object> { { "exception", ex.Message } }));
            }
        }

        protected abstract ValidationFailure ValidateVersion();

        private ValidationFailure TestGetTorrents()
        {
            try
            {
                _proxy.GetTorrents(null, Settings);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get torrents");
                return new NzbDroneValidationFailure(string.Empty, _localizationService.GetLocalizedString("DownloadClientValidationTestTorrents", new Dictionary<string, object> { { "exceptionMessage", ex.Message } }));
            }

            return null;
        }

        protected bool HasClientVersion(int major, int minor)
        {
            var rawVersion = _proxy.GetClientVersion(Settings);

            var versionResult = Regex.Match(rawVersion, @"(?<!\(|(\d|\.)+)(\d|\.)+(?!\)|(\d|\.)+)").Value;
            var clientVersion = Version.Parse(versionResult);

            return clientVersion >= new Version(major, minor);
        }
    }
}
