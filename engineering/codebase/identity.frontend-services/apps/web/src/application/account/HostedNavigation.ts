/** Leaves the hosted pages for a server route such as `/connect/authorize`, which only a full load reaches. */
export const hostedNavigation = {
  assign(url: string) {
    window.location.assign(url);
  },
} as const;
