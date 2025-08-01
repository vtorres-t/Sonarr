using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMediaFileRepository : IBasicRepository<EpisodeFile>
    {
        List<EpisodeFile> GetFilesBySeries(int seriesId);
        List<EpisodeFile> GetFilesBySeriesIds(List<int> seriesIds);
        List<EpisodeFile> GetFilesBySeason(int seriesId, int seasonNumber);
        List<EpisodeFile> GetFilesWithoutMediaInfo();
        List<EpisodeFile> GetFilesWithRelativePath(int seriesId, string relativePath);
        void DeleteForSeries(List<int> seriesIds);
    }

    public class MediaFileRepository : BasicRepository<EpisodeFile>, IMediaFileRepository
    {
        public MediaFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<EpisodeFile> GetFilesBySeries(int seriesId)
        {
            return Query(c => c.SeriesId == seriesId).ToList();
        }

        public List<EpisodeFile> GetFilesBySeriesIds(List<int> seriesIds)
        {
            return Query(c => seriesIds.Contains(c.SeriesId)).ToList();
        }

        public List<EpisodeFile> GetFilesBySeason(int seriesId, int seasonNumber)
        {
            return Query(c => c.SeriesId == seriesId && c.SeasonNumber == seasonNumber).ToList();
        }

        public List<EpisodeFile> GetFilesWithoutMediaInfo()
        {
            return Query(c => c.MediaInfo == null).ToList();
        }

        public List<EpisodeFile> GetFilesWithRelativePath(int seriesId, string relativePath)
        {
            return Query(c => c.SeriesId == seriesId && c.RelativePath == relativePath)
                        .ToList();
        }

        public void DeleteForSeries(List<int> seriesIds)
        {
            Delete(x => seriesIds.Contains(x.SeriesId));
        }
    }
}
