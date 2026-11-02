/** Mirrors the be contract's profile field mode: whether an invitation sign-up asks for a profile field. */
export const ProfileFieldMode = {
  /** The field is not asked for. */
  Off: 'off',
  /** The field is asked for and may be left empty. */
  Optional: 'optional',
  /** The field is asked for and must be given. */
  Required: 'required',
} as const;

/** Whether a sign-up asks for a profile field: `off`, `optional` or `required`. */
export type ProfileFieldMode = (typeof ProfileFieldMode)[keyof typeof ProfileFieldMode];
