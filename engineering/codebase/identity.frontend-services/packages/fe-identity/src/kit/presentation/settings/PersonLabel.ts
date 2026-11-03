import type { AccountText } from '../../account/hooks';

/** Names a person a settings list shows: their display name, else a neutral word, never an id. */
export function personLabel(t: AccountText, displayName: string | null): string {
  return displayName?.trim() || t('Settings.unnamedPerson', 'Unnamed person');
}
