import React, { useCallback, useMemo } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { OptionChanged } from 'Helpers/Hooks/useOptionsStore';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import {
  QueueOptions,
  setQueueOption,
  useQueueOption,
} from './queueOptionsStore';
import styles from './RemoveQueueItemModal.css';

interface RemoveQueueItemModalProps {
  isOpen: boolean;
  sourceTitle?: string;
  canChangeCategory: boolean;
  canIgnore: boolean;
  isPending: boolean;
  selectedCount?: number;
  onRemovePress(): void;
  onModalClose: () => void;
}

function RemoveQueueItemModal(props: RemoveQueueItemModalProps) {
  const {
    isOpen,
    sourceTitle = '',
    canIgnore,
    canChangeCategory,
    isPending,
    selectedCount,
    onRemovePress,
    onModalClose,
  } = props;

  const multipleSelected = selectedCount && selectedCount > 1;
  const { removalMethod, blocklistMethod } = useQueueOption('removalOptions');

  const { title, message } = useMemo(() => {
    if (!selectedCount) {
      return {
        title: translate('RemoveQueueItem', { sourceTitle }),
        message: translate('RemoveQueueItemConfirmation', { sourceTitle }),
      };
    }

    if (selectedCount === 1) {
      return {
        title: translate('RemoveSelectedItem'),
        message: translate('RemoveSelectedItemQueueMessageText'),
      };
    }

    return {
      title: translate('RemoveSelectedItems'),
      message: translate('RemoveSelectedItemsQueueMessageText', {
        selectedCount,
      }),
    };
  }, [sourceTitle, selectedCount]);

  const removalMethodOptions = useMemo(() => {
    const options: EnhancedSelectInputValue<string>[] = [
      {
        key: 'removeFromClient',
        value: translate('RemoveFromDownloadClient'),
        hint: multipleSelected
          ? translate('RemoveMultipleFromDownloadClientHint')
          : translate('RemoveFromDownloadClientHint'),
      },
      {
        key: 'changeCategory',
        value: translate('ChangeCategory'),
        isDisabled: !canChangeCategory,
        hint: multipleSelected
          ? translate('ChangeCategoryMultipleHint')
          : translate('ChangeCategoryHint'),
      },
      {
        key: 'ignore',
        value: multipleSelected
          ? translate('IgnoreDownloads')
          : translate('IgnoreDownload'),
        isDisabled: !canIgnore,
        hint: multipleSelected
          ? translate('IgnoreDownloadsHint')
          : translate('IgnoreDownloadHint'),
      },
    ];

    return options;
  }, [canChangeCategory, canIgnore, multipleSelected]);

  const blocklistMethodOptions = useMemo(() => {
    const options: EnhancedSelectInputValue<string>[] = [
      {
        key: 'doNotBlocklist',
        value: translate('DoNotBlocklist'),
        hint: translate('DoNotBlocklistHint'),
      },
      {
        key: 'blocklistAndSearch',
        value: translate('BlocklistAndSearch'),
        isDisabled: isPending,
        hint: multipleSelected
          ? translate('BlocklistAndSearchMultipleHint')
          : translate('BlocklistAndSearchHint'),
      },
      {
        key: 'blocklistOnly',
        value: translate('BlocklistOnly'),
        hint: multipleSelected
          ? translate('BlocklistMultipleOnlyHint')
          : translate('BlocklistOnlyHint'),
      },
    ];

    return options;
  }, [isPending, multipleSelected]);

  const handleRemovalOptionInputChange = useCallback(
    ({ name, value }: OptionChanged<QueueOptions['removalOptions']>) => {
      setQueueOption('removalOptions', {
        removalMethod,
        blocklistMethod,
        [name]: value,
      });
    },
    [removalMethod, blocklistMethod]
  );

  const handleConfirmRemove = useCallback(() => {
    onRemovePress();
  }, [onRemovePress]);

  const handleModalClose = useCallback(() => {
    onModalClose();
  }, [onModalClose]);

  return (
    <Modal isOpen={isOpen} size={sizes.MEDIUM} onModalClose={handleModalClose}>
      <ModalContent onModalClose={handleModalClose}>
        <ModalHeader>{title}</ModalHeader>

        <ModalBody>
          <div className={styles.message}>{message}</div>

          {isPending ? null : (
            <FormGroup>
              <FormLabel>{translate('RemoveQueueItemRemovalMethod')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="removalMethod"
                value={removalMethod}
                values={removalMethodOptions}
                isDisabled={!canChangeCategory && !canIgnore}
                helpTextWarning={translate(
                  'RemoveQueueItemRemovalMethodHelpTextWarning'
                )}
                // @ts-expect-error - The typing for inputs needs more work
                onChange={handleRemovalOptionInputChange}
              />
            </FormGroup>
          )}

          <FormGroup>
            <FormLabel>
              {multipleSelected
                ? translate('BlocklistReleases')
                : translate('BlocklistRelease')}
            </FormLabel>

            <FormInputGroup
              type={inputTypes.SELECT}
              name="blocklistMethod"
              value={blocklistMethod}
              values={blocklistMethodOptions}
              helpText={translate('BlocklistReleaseHelpText')}
              // @ts-expect-error - The typing for inputs needs more work
              onChange={handleRemovalOptionInputChange}
            />
          </FormGroup>
        </ModalBody>

        <ModalFooter>
          <Button onPress={handleModalClose}>{translate('Close')}</Button>

          <Button kind={kinds.DANGER} onPress={handleConfirmRemove}>
            {translate('Remove')}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default RemoveQueueItemModal;
