import { createAction } from 'redux-actions';
import { filterBuilderTypes, filterBuilderValueTypes, filterTypes, sortDirections } from 'Helpers/Props';
import getFilterTypePredicate from 'Helpers/Props/getFilterTypePredicate';
import { createThunk, handleThunks } from 'Store/thunks';
import sortByProp from 'Utilities/Array/sortByProp';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionFilterReducer from './Creators/Reducers/createSetClientSideCollectionFilterReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';

//
// Variables

export const section = 'releases';
export const episodeSection = 'releases.episode';
export const seasonSection = 'releases.season';

let abortCurrentRequest = null;

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  items: [],
  sortKey: 'customFormatScore',
  sortDirection: sortDirections.DESCENDING,
  sortPredicates: {
    age: function(item, direction) {
      return item.ageMinutes;
    },

    peers: function(item, direction) {
      const seeders = item.seeders || 0;
      const leechers = item.leechers || 0;

      return seeders * 1000000 + leechers;
    },

    languages: function(item, direction) {
      if (item.languages.length > 1) {
        return 10000;
      }

      return item.languages[0]?.id ?? 0;
    },

    rejections: function(item, direction) {
      const rejections = item.rejections;
      const releaseWeight = item.releaseWeight;

      if (rejections.length !== 0) {
        return releaseWeight + 1000000;
      }

      return releaseWeight;
    }
  },

  filters: [
    {
      key: 'all',
      label: () => translate('All'),
      filters: [
        {
          key: 'customFormatScore',
          value: 1,
          type: filterTypes.GREATER_THAN_OR_EQUAL
        }
      ]
    },
    {
      key: 'recent',
      label: () => translate('Recent'),
      filters: [
        {
          key: 'age',
          value: 3,
          type: filterTypes.LESS_THAN_OR_EQUAL
        },
        {
          key: 'customFormatScore',
          value: 1,
          type: filterTypes.GREATER_THAN_OR_EQUAL
        }
      ]
    },
    {
      key: 'season-pack',
      label: () => translate('SeasonPack'),
      filters: [
        {
          key: 'fullSeason',
          value: true,
          type: filterTypes.EQUAL
        },
        {
          key: 'customFormatScore',
          value: 1,
          type: filterTypes.GREATER_THAN_OR_EQUAL
        }
      ]
    },
    {
      key: 'not-season-pack',
      label: () => translate('NotSeasonPack'),
      filters: [
        {
          key: 'fullSeason',
          value: false,
          type: filterTypes.EQUAL
        },
        {
          key: 'customFormatScore',
          value: 1,
          type: filterTypes.GREATER_THAN_OR_EQUAL
        }
      ]
    }
  ],

  filterPredicates: {
    quality: function(item, value, type) {
      const qualityId = item.quality.quality.id;

      if (type === filterTypes.EQUAL) {
        return qualityId === value;
      }

      if (type === filterTypes.NOT_EQUAL) {
        return qualityId !== value;
      }

      // Default to false
      return false;
    },

    languages: function(item, filterValue, type) {
      const predicate = getFilterTypePredicate(type);
      const languages = item.languages.map((language) => language.name);

      return predicate(languages, filterValue);
    },

    rejectionCount: function(item, value, type) {
      const rejectionCount = item.rejections.length;

      switch (type) {
        case filterTypes.EQUAL:
          return rejectionCount === value;

        case filterTypes.GREATER_THAN:
          return rejectionCount > value;

        case filterTypes.GREATER_THAN_OR_EQUAL:
          return rejectionCount >= value;

        case filterTypes.LESS_THAN:
          return rejectionCount < value;

        case filterTypes.LESS_THAN_OR_EQUAL:
          return rejectionCount <= value;

        case filterTypes.NOT_EQUAL:
          return rejectionCount !== value;

        default:
          return false;
      }
    },

    peers: function(item, value, type) {
      const seeders = item.seeders || 0;
      const leechers = item.leechers || 0;
      const peers = seeders + leechers;

      switch (type) {
        case filterTypes.EQUAL:
          return peers === value;

        case filterTypes.GREATER_THAN:
          return peers > value;

        case filterTypes.GREATER_THAN_OR_EQUAL:
          return peers >= value;

        case filterTypes.LESS_THAN:
          return peers < value;

        case filterTypes.LESS_THAN_OR_EQUAL:
          return peers <= value;

        case filterTypes.NOT_EQUAL:
          return peers !== value;

        default:
          return false;
      }
    }
  },

  filterBuilderProps: [
    {
      name: 'title',
      label: () => translate('Title'),
      type: filterBuilderTypes.STRING
    },
    {
      name: 'age',
      label: () => translate('Age'),
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'protocol',
      label: () => translate('Protocol'),
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.PROTOCOL
    },
    {
      name: 'indexerId',
      label: () => translate('Indexer'),
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.INDEXER
    },
    {
      name: 'size',
      label: () => translate('Size'),
      type: filterBuilderTypes.NUMBER,
      valueType: filterBuilderValueTypes.BYTES
    },
    {
      name: 'seeders',
      label: () => translate('Seeders'),
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'peers',
      label: () => translate('Peers'),
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'quality',
      label: () => translate('Quality'),
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.QUALITY
    },
    {
      name: 'languages',
      label: () => translate('Languages'),
      type: filterBuilderTypes.ARRAY,
      optionsSelector: function(items) {
        const genreList = items.reduce((acc, release) => {
          release.languages.forEach((language) => {
            acc.push({
              id: language.name,
              name: language.name
            });
          });

          return acc;
        }, []);

        return genreList.sort(sortByProp('name'));
      }
    },
    {
      name: 'customFormatScore',
      label: () => translate('CustomFormatScore'),
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'rejectionCount',
      label: () => translate('RejectionCount'),
      type: filterBuilderTypes.NUMBER
    },
    {
      name: 'fullSeason',
      label: () => translate('SeasonPack'),
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.BOOL
    },
    {
      name: 'episodeRequested',
      label: () => translate('EpisodeRequested'),
      type: filterBuilderTypes.EXACT,
      valueType: filterBuilderValueTypes.BOOL
    }
  ],

  episode: {
    selectedFilterKey: 'all'
  },

  season: {
    selectedFilterKey: 'season-pack'
  }
};

export const persistState = [
  'releases.episode.selectedFilterKey',
  'releases.episode.customFilters',
  'releases.season.selectedFilterKey',
  'releases.season.customFilters'
];

//
// Actions Types

export const FETCH_RELEASES = 'releases/fetchReleases';
export const CANCEL_FETCH_RELEASES = 'releases/cancelFetchReleases';
export const SET_RELEASES_SORT = 'releases/setReleasesSort';
export const CLEAR_RELEASES = 'releases/clearReleases';
export const GRAB_RELEASE = 'releases/grabRelease';
export const UPDATE_RELEASE = 'releases/updateRelease';
export const SET_EPISODE_RELEASES_FILTER = 'releases/setEpisodeReleasesFilter';
export const SET_SEASON_RELEASES_FILTER = 'releases/setSeasonReleasesFilter';

//
// Action Creators

export const fetchReleases = createThunk(FETCH_RELEASES);
export const cancelFetchReleases = createThunk(CANCEL_FETCH_RELEASES);
export const setReleasesSort = createAction(SET_RELEASES_SORT);
export const clearReleases = createAction(CLEAR_RELEASES);
export const grabRelease = createThunk(GRAB_RELEASE);
export const updateRelease = createAction(UPDATE_RELEASE);
export const setEpisodeReleasesFilter = createAction(SET_EPISODE_RELEASES_FILTER);
export const setSeasonReleasesFilter = createAction(SET_SEASON_RELEASES_FILTER);

//
// Helpers

const fetchReleasesHelper = createFetchHandler(section, '/release');

//
// Action Handlers

export const actionHandlers = handleThunks({

  [FETCH_RELEASES]: function(getState, payload, dispatch) {
    const abortRequest = fetchReleasesHelper(getState, payload, dispatch);

    abortCurrentRequest = abortRequest;
  },

  [CANCEL_FETCH_RELEASES]: function(getState, payload, dispatch) {
    if (abortCurrentRequest) {
      abortCurrentRequest = abortCurrentRequest();
    }
  },

  [GRAB_RELEASE]: function(getState, payload, dispatch) {
    const guid = payload.guid;

    dispatch(updateRelease({ guid, isGrabbing: true }));

    const promise = createAjaxRequest({
      url: '/release',
      method: 'POST',
      dataType: 'json',
      contentType: 'application/json',
      data: JSON.stringify(payload)
    }).request;

    promise.done((data) => {
      dispatch(updateRelease({
        guid,
        isGrabbing: false,
        isGrabbed: true,
        grabError: null
      }));
    });

    promise.fail((xhr) => {
      const grabError = xhr.responseJSON && xhr.responseJSON.message || 'Failed to add to download queue';

      dispatch(updateRelease({
        guid,
        isGrabbing: false,
        isGrabbed: false,
        grabError
      }));
    });
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [CLEAR_RELEASES]: (state) => {
    const {
      episode,
      season,
      ...otherDefaultState
    } = defaultState;

    return Object.assign({}, state, otherDefaultState);
  },

  [UPDATE_RELEASE]: (state, { payload }) => {
    const guid = payload.guid;
    const newState = Object.assign({}, state);
    const items = newState.items;
    const index = items.findIndex((item) => item.guid === guid);

    // Don't try to update if there isn't a matching item (the user closed the modal)

    if (index >= 0) {
      const item = Object.assign({}, items[index], payload);

      newState.items = [...items];
      newState.items.splice(index, 1, item);
    }

    return newState;
  },

  [SET_RELEASES_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_EPISODE_RELEASES_FILTER]: createSetClientSideCollectionFilterReducer(episodeSection),
  [SET_SEASON_RELEASES_FILTER]: createSetClientSideCollectionFilterReducer(seasonSection)

}, defaultState, section);
