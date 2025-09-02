import { keepPreviousData, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo, useState } from 'react';
import { useSelector } from 'react-redux';
import { CustomFilter, Filter, FilterBuilderProp } from 'App/State/AppState';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import usePage from 'Helpers/Hooks/usePage';
import usePagedApiQuery from 'Helpers/Hooks/usePagedApiQuery';
import { filterBuilderValueTypes } from 'Helpers/Props';
import { createCustomFiltersSelector } from 'Store/Selectors/createClientSideCollectionSelector';
import Queue from 'typings/Queue';
import getQueryString from 'Utilities/Fetch/getQueryString';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import translate from 'Utilities/String/translate';
import { useQueueOptions } from './queueOptionsStore';

interface BulkQueueData {
  ids: number[];
}

export const FILTERS: Filter[] = [
  {
    key: 'all',
    label: () => translate('All'),
    filters: [],
  },
];

export const FILTER_BUILDER: FilterBuilderProp<Queue>[] = [
  {
    name: 'seriesIds',
    label: () => translate('Series'),
    type: 'equal',
    valueType: filterBuilderValueTypes.SERIES,
  },
  {
    name: 'quality',
    label: () => translate('Quality'),
    type: 'equal',
    valueType: filterBuilderValueTypes.QUALITY,
  },
  {
    name: 'languages',
    label: () => translate('Languages'),
    type: 'contains',
    valueType: filterBuilderValueTypes.LANGUAGE,
  },
  {
    name: 'protocol',
    label: () => translate('Protocol'),
    type: 'equal',
    valueType: filterBuilderValueTypes.PROTOCOL,
  },
  {
    name: 'status',
    label: () => translate('Status'),
    type: 'equal',
    valueType: filterBuilderValueTypes.QUEUE_STATUS,
  },
];

const useQueue = () => {
  const { page, goToPage } = usePage('queue');
  const {
    includeUnknownSeriesItems,
    pageSize,
    selectedFilterKey,
    sortKey,
    sortDirection,
  } = useQueueOptions();
  const customFilters = useSelector(
    createCustomFiltersSelector('queue')
  ) as CustomFilter[];

  const filters = useMemo(() => {
    return findSelectedFilters(selectedFilterKey, FILTERS, customFilters);
  }, [selectedFilterKey, customFilters]);

  const { refetch, ...query } = usePagedApiQuery<Queue>({
    path: '/queue',
    page,
    pageSize,
    filters,
    queryParams: {
      includeUnknownSeriesItems,
    },
    sortKey,
    sortDirection,
    queryOptions: {
      placeholderData: keepPreviousData,
    },
  });

  const handleGoToPage = useCallback(
    (page: number) => {
      goToPage(page);
    },
    [goToPage]
  );

  return {
    ...query,
    goToPage: handleGoToPage,
    page,
    refetch,
  };
};

export default useQueue;

export const useFilters = () => {
  return FILTERS;
};

const useRemovalOptions = () => {
  const { removalOptions } = useQueueOptions();

  return {
    remove: removalOptions.removalMethod === 'removeFromClient',
    changeCategory: removalOptions.removalMethod === 'changeCategory',
    blocklist: removalOptions.blocklistMethod !== 'doNotBlocklist',
    skipRedownload: removalOptions.blocklistMethod === 'blocklistOnly',
  };
};

export const useRemoveQueueItem = (id: number) => {
  const queryClient = useQueryClient();
  const removalOptions = useRemovalOptions();

  const { mutate, isPending } = useApiMutation<unknown, void>({
    path: `/queue/${id}${getQueryString(removalOptions)}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
    },
  });

  return {
    removeQueueItem: mutate,
    isRemoving: isPending,
  };
};

export const useRemoveQueueItems = () => {
  const queryClient = useQueryClient();
  const removalOptions = useRemovalOptions();

  const { mutate, isPending } = useApiMutation<unknown, BulkQueueData>({
    path: `/queue/bulk${getQueryString(removalOptions)}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
    },
  });

  return {
    removeQueueItems: mutate,
    isRemoving: isPending,
  };
};

export const useGrabQueueItem = (id: number) => {
  const queryClient = useQueryClient();
  const [grabError, setGrabError] = useState<string | null>(null);

  const { mutate, isPending } = useApiMutation<unknown, void>({
    path: `/queue/grab/${id}`,
    method: 'POST',
    mutationOptions: {
      onMutate: () => {
        setGrabError(null);
      },
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
      onError: () => {
        setGrabError('Error grabbing queue item');
      },
    },
  });

  return {
    grabQueueItem: mutate,
    isGrabbing: isPending,
    grabError,
  };
};

export const useGrabQueueItems = () => {
  const queryClient = useQueryClient();

  // Explicitly define the types for the mutation so we can pass in no arguments to mutate as expected.
  const { mutate, isPending } = useApiMutation<unknown, BulkQueueData>({
    path: '/queue/grab/bulk',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
    },
  });

  return {
    grabQueueItems: mutate,
    isGrabbing: isPending,
  };
};
