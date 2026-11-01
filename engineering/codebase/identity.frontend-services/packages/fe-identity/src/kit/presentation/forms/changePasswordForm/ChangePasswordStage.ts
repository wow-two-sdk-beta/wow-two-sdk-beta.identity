/** Defines where a password change stands. */
export const ChangePasswordStage = {
  /** Refers to the change not started: the instance has not said how it is confirmed. */
  Idle: 'idle',
  /** Refers to the change form, with the code field under the `Code` confirmation. */
  Form: 'form',
  /** Refers to the `Link` confirmation: only the emailed link opens the form. */
  LinkSent: 'link-sent',
  /** Refers to the password changed. */
  Done: 'done',
} as const;
export type ChangePasswordStage = (typeof ChangePasswordStage)[keyof typeof ChangePasswordStage];
