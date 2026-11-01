import { useLocale } from '@wow-two-beta/fe/foundation/i18n';

/** Resolves one kit message: its key under `IdentityKit.`, the English fallback, and the values it interpolates. */
export type AccountText = (key: string, fallback: string, values?: Readonly<Record<string, string | number>>) => string;

/**
 * Returns the kit's message resolver: the consumer's `LocaleProvider` messages for `IdentityKit.{key}`, else the
 * English fallback. Call it in `setup()`.
 */
export function useAccountText(): AccountText {
  const locale = useLocale();
  return (key, fallback, values) => locale.t(`IdentityKit.${key}`, values, fallback);
}
