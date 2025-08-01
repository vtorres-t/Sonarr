import React from 'react';
import { createAction } from 'redux-actions';
import Icon from 'Components/Icon';
import { filterBuilderTypes, filterBuilderValueTypes, filterTypes, icons, sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import serverSideCollectionHandlers from 'Utilities/State/serverSideCollectionHandlers';
import translate from 'Utilities/String/translate';
import { updateItem } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import createServerSideCollectionHandlers from './Creators/createServerSideCollectionHandlers';
import createClearReducer from './Creators/Reducers/createClearReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'history';

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  pageSize: 20,
  sortKey: 'date',
  sortDirection: sortDirections.DESCENDING,
  items: [],

  columns: [
    {
      name: 'eventType',
      columnLabel: () => translate('EventType'),
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'series.sortTitle',
      label: () => translate('Series'),
      isSortable: true,
      isVisible: true
    },
    {
      name: 'episode',
      label: () => translate('Episode'),
      isVisible: true
    },
    {
      name: 'episodes.title',
      label: () => translate('EpisodeTitle'),
      isVisible: true
    },
    {
      name: 'languages',
      label: () => translate('Languages'),
      isVisible: false
    },
    {
      name: 'quality',
      label: () => translate('Quality'),
      isVisible: true
    },
    {
      name: 'customFormats',
      label: () => translate('Formats'),
      isSortable: false,
      isVisible: true
    },
    {
      name: 'customFormatScore',
      columnLabel: () => translate('CustomFormatScore'),
      label: React.createElement(Icon, {
        name: icons.SCORE,
        title: () => translate('CustomFormatScore')
      }),
      isVisible: true
    },
    {
      name: 'date',
      label: () => translate('Date'),
      isSortable: true,
      isVisible: true
    },
    {
      name: 'downloadClient',
      label: () => translate('DownloadClient'),
      isVisible: false
    },
    {
      name: 'indexer',
      label: () => translate('Indexer'),
      isVisible: false
    },
    {
      name: 'releaseGroup',
      label: () => translate('ReleaseGroup'),
      isVisible: false
    },
    {
      name: 'sourceTitle',
      label: () => translate('SourceTitle'),
      isVisible: false
    },
    {
      name: 'details',
      columnLabel: () => translate('Details'),
      isVisible: true,
      isModifiable: false
    }
  ],

  selectedFilterKey: 'all',

  filters: [
    {
      key: 'all',
      label: () => translate('All'),
      filters: []
    },
    {
      key: 'grabbed',
      label: () => translate('Grabbed'),
      filters: [
        {
          key: 'eventType',
          value: '1',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'imported',
      label: () => translate('Imported'),
      filters: [
        {
          key: 'eventType',
          value: '3',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'failed',
      label: () => translate('Failed'),
      filters: [
        {
          key: 'eventType',
          value: '4',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'deleted',
      label: () => translate('Deleted'),
      filters: [
        {
          key: 'eventType',
          value: '5',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'renamed',
      label: () => translate('Renamed'),
      filters: [
        {
          key: 'eventType',
          value: '6',
          type: filterTypes.EQUAL
        }
      ]
    },
    {
      key: 'ignored',
      label: () => translate('Ignored'),
      filters: [
        {
          key: 'eventType',
          value: '7',
          type: filterTypes.EQUAL
        }
      ]
    }
  ],

  filterBuilderProps: [
    {
      name: 'eventType',
      label: () => translate('EventType'),
      type: filterBuilderTypes.EQUAL,
      valueType: filterBuilderValueTypes.HISTORY_EVENT_TYPE
    },
    {
      name: 'seriesIds',
      label: () => translate('Series'),
      type: filterBuilderTypes.EQUAL,
      valueType: filterBuilderValueTypes.SERIES
    },
    {
      name: 'quality',
      label: () => translate('Quality'),
      type: filterBuilderTypes.EQUAL,
      valueType: filterBuilderValueTypes.QUALITY
    },
    {
      name: 'languages',
      label: () => translate('Languages'),
      type: filterBuilderTypes.CONTAINS,
      valueType: filterBuilderValueTypes.LANGUAGE
    }
  ]

};

export const persistState = [
  'history.pageSize',
  'history.sortKey',
  'history.sortDirection',
  'history.selectedFilterKey',
  'history.columns'
];

//
// Actions Types

export const FETCH_HISTORY = 'history/fetchHistory';
export const GOTO_FIRST_HISTORY_PAGE = 'history/gotoHistoryFirstPage';
export const GOTO_PREVIOUS_HISTORY_PAGE = 'history/gotoHistoryPreviousPage';
export const GOTO_NEXT_HISTORY_PAGE = 'history/gotoHistoryNextPage';
export const GOTO_LAST_HISTORY_PAGE = 'history/gotoHistoryLastPage';
export const GOTO_HISTORY_PAGE = 'history/gotoHistoryPage';
export const SET_HISTORY_SORT = 'history/setHistorySort';
export const SET_HISTORY_FILTER = 'history/setHistoryFilter';
export const SET_HISTORY_TABLE_OPTION = 'history/setHistoryTableOption';
export const CLEAR_HISTORY = 'history/clearHistory';
export const MARK_AS_FAILED = 'history/markAsFailed';

//
// Action Creators

export const fetchHistory = createThunk(FETCH_HISTORY);
export const gotoHistoryFirstPage = createThunk(GOTO_FIRST_HISTORY_PAGE);
export const gotoHistoryPreviousPage = createThunk(GOTO_PREVIOUS_HISTORY_PAGE);
export const gotoHistoryNextPage = createThunk(GOTO_NEXT_HISTORY_PAGE);
export const gotoHistoryLastPage = createThunk(GOTO_LAST_HISTORY_PAGE);
export const gotoHistoryPage = createThunk(GOTO_HISTORY_PAGE);
export const setHistorySort = createThunk(SET_HISTORY_SORT);
export const setHistoryFilter = createThunk(SET_HISTORY_FILTER);
export const setHistoryTableOption = createAction(SET_HISTORY_TABLE_OPTION);
export const clearHistory = createAction(CLEAR_HISTORY);
export const markAsFailed = createThunk(MARK_AS_FAILED);

//
// Action Handlers

export const actionHandlers = handleThunks({
  ...createServerSideCollectionHandlers(
    section,
    '/history',
    fetchHistory,
    {
      [serverSideCollectionHandlers.FETCH]: FETCH_HISTORY,
      [serverSideCollectionHandlers.FIRST_PAGE]: GOTO_FIRST_HISTORY_PAGE,
      [serverSideCollectionHandlers.PREVIOUS_PAGE]: GOTO_PREVIOUS_HISTORY_PAGE,
      [serverSideCollectionHandlers.NEXT_PAGE]: GOTO_NEXT_HISTORY_PAGE,
      [serverSideCollectionHandlers.LAST_PAGE]: GOTO_LAST_HISTORY_PAGE,
      [serverSideCollectionHandlers.EXACT_PAGE]: GOTO_HISTORY_PAGE,
      [serverSideCollectionHandlers.SORT]: SET_HISTORY_SORT,
      [serverSideCollectionHandlers.FILTER]: SET_HISTORY_FILTER
    }),

  [MARK_AS_FAILED]: function(getState, payload, dispatch) {
    const id = payload.id;

    dispatch(updateItem({
      section,
      id,
      isMarkingAsFailed: true
    }));

    const promise = createAjaxRequest({
      url: `/history/failed/${id}`,
      method: 'POST',
      dataType: 'json'
    }).request;

    promise.done(() => {
      dispatch(updateItem({
        section,
        id,
        isMarkingAsFailed: false,
        markAsFailedError: null
      }));
    });

    promise.fail((xhr) => {
      dispatch(updateItem({
        section,
        id,
        isMarkingAsFailed: false,
        markAsFailedError: xhr
      }));
    });
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_HISTORY_TABLE_OPTION]: createSetTableOptionReducer(section),

  [CLEAR_HISTORY]: createClearReducer(section, {
    isFetching: false,
    isPopulated: false,
    error: null,
    items: [],
    totalPages: 0,
    totalRecords: 0
  })

}, defaultState, section);
