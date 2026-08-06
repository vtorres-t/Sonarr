import React, { useCallback } from 'react';
import { CalendarView } from 'Calendar/calendarViews';
import Button, { ButtonProps } from 'Components/Link/Button';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';

interface CalendarHeaderViewButtonProps
  extends Omit<ButtonProps, 'children' | 'onPress'> {
  view: CalendarView;
  selectedView: CalendarView;
  onPress: (view: CalendarView) => void;
}

function CalendarHeaderViewButton({
  view,
  selectedView,
  onPress,
  ...otherProps
}: CalendarHeaderViewButtonProps) {
  const handlePress = useCallback(() => {
    onPress(view);
  }, [view, onPress]);

  return (
    <Button
      isDisabled={selectedView === view}
      {...otherProps}
      onPress={handlePress}
    >
      {translate(titleCase(view))}
    </Button>
  );
}

export default CalendarHeaderViewButton;
