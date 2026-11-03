/** Holds one choice of the trusted contact select: a member's id (or the none value) and the name it shows. */
export interface TrustedContactOption {
  /** The member's id, or the select's none value. */
  readonly value: string;
  /** The name the option shows. */
  readonly label: string;
}
