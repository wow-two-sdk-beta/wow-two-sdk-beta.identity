/** Represents the running backend: its service name and version. */
export interface SystemStatus {
  readonly service: string;
  readonly version: string;
}
