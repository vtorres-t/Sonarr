using System;
using System.Collections.Generic;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Update
{
    public interface IUpdatePackageProvider
    {
        UpdatePackage GetLatestUpdate(string branch, Version currentVersion);
        List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null);
    }

    public class UpdatePackageProvider : IUpdatePackageProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly IPlatformInfo _platformInfo;
        private readonly IMainDatabase _mainDatabase;

        public UpdatePackageProvider(IHttpClient httpClient, ISonarrCloudRequestBuilder requestBuilder, IPlatformInfo platformInfo, IMainDatabase mainDatabase)
        {
            _platformInfo = platformInfo;
            _requestBuilder = requestBuilder.Services;
            _httpClient = httpClient;
            _mainDatabase = mainDatabase;
        }

        public UpdatePackage GetLatestUpdate(string branch, Version currentVersion)
        {/*
            var request = _requestBuilder.Create()
                                         .Resource("/update/{branch}")
                                         .AddQueryParam("version", currentVersion)
                                         .AddQueryParam("os", OsInfo.Os.ToString().ToLowerInvariant())
                                         .AddQueryParam("arch", RuntimeInformation.OSArchitecture)
                                         .AddQueryParam("runtime", "netcore")
                                         .AddQueryParam("runtimeVer", _platformInfo.Version)
                                         .AddQueryParam("dbType", _mainDatabase.DatabaseType)
                                         .AddQueryParam("includeMajorVersion", true)
                                         .SetSegment("branch", branch);

            var update = _httpClient.Get<UpdatePackageAvailable>(request.Build()).Resource;

            if (!update.Available)
            {
                return null;
            }

            return update.UpdatePackage;*/
            return null;
        }

        public List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion)
        {
            /*
            var request = _requestBuilder.Create()
                                         .Resource("/update/{branch}/changes")
                                         .AddQueryParam("version", currentVersion)
                                         .AddQueryParam("os", OsInfo.Os.ToString().ToLowerInvariant())
                                         .AddQueryParam("arch", RuntimeInformation.OSArchitecture)
                                         .AddQueryParam("runtime", "netcore")
                                         .AddQueryParam("runtimeVer", _platformInfo.Version)
                                         .SetSegment("branch", branch);

            if (previousVersion != null && previousVersion != currentVersion)
            {
                request.AddQueryParam("prevVersion", previousVersion);
            }

            var updates = _httpClient.Get<List<UpdatePackage>>(request.Build());

            return updates.Resource;*/

            return new List<UpdatePackage>();
        }
    }
}
