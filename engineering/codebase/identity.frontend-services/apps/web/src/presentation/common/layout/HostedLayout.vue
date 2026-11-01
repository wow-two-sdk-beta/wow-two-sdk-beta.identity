<script setup lang="ts">
import { SwapTransition } from '@wow-two-beta/fe/foundation/primitives';
import { Card } from '@wow-two-beta/fe/presentation/display';
import { computed } from 'vue';
import { RouterView } from 'vue-router';
import { provideHostedOptions, readHostedPageLink, useSignInOptions } from '../../../application/account';
import type { SignInRequest } from '../../../domain/account';

/**
 * Frames the hosted pages in the instance's look and keeps that frame while people move between them: its fe sdk theme
 * by name, its background image (a same-origin path or an HTTPS URL) or the theme's own backdrop, the brand and one
 * opaque card that stays readable over either. The card's body is the routed page, swapped with a fade and a height
 * animation. The sign-in declaration loads here once and reaches the pages below.
 */
defineOptions({ name: 'HostedLayout' });

const props = defineProps<{
  /** The app the pages serve, decoded from the route; names the client the declaration is loaded for. */
  request: SignInRequest;
  /** Opens a hosted page in the app, in place of the full load its link would make. */
  onNavigate: (path: string) => void;
}>();

const { options, failure } = useSignInOptions(() => props.request.clientId);
provideHostedOptions({ options, failure });

/** A registry theme id as a class name; anything else falls back to the document's theme. */
const themeClass = computed(() => {
  const theme = options.value?.theme ?? '';
  return /^[a-z0-9-]+$/.test(theme) ? `theme-${theme}` : '';
});

/** Whether `url` is a path on this origin or an absolute HTTPS URL, the two the service accepts in its settings. */
function isBackgroundUrl(url: string): boolean {
  if (url.startsWith('/')) return !url.startsWith('//') && !url.includes('\\');
  try {
    return new URL(url).protocol === 'https:';
  } catch {
    return false;
  }
}

/** A same-origin or HTTPS image only; anything else keeps the theme's own backdrop. */
const backgroundImage = computed(() => {
  const url = options.value?.backgroundImageUrl;
  return url && isBackgroundUrl(url) ? `url("${encodeURI(url).replace(/"/g, '%22')}")` : null;
});

/**
 * Turns a plain click on a link to another hosted page into in-app navigation, so the card persists. Links stay plain
 * anchors in the pages and the kit, with real addresses; a modified click, a new-tab target, a download and any link
 * that leaves the hosted pages keep the browser's own behavior.
 */
function followHostedLink(event: MouseEvent) {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)
    return;
  const link = event.target instanceof Element ? event.target.closest('a[href]') : null;
  if (!(link instanceof HTMLAnchorElement) || (link.target && link.target !== '_self') || link.hasAttribute('download'))
    return;
  const path = readHostedPageLink(link.href, window.location.origin);
  if (!path) return;
  event.preventDefault();
  props.onNavigate(path);
}
</script>

<template>
  <div
    :class="[
      themeClass,
      'min-h-dvh bg-background font-sans text-foreground',
      backgroundImage ? 'bg-cover bg-fixed bg-center' : 'surface-ambient',
    ]"
    :style="backgroundImage ? { backgroundImage } : undefined"
  >
    <main class="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center px-4 py-10" @click="followHostedLink">
      <Card class="hosted-card p-6 shadow-xl sm:p-8">
        <p class="mb-1 text-sm font-medium text-muted-foreground">{{ options?.brandName ?? 'Identity' }}</p>
        <RouterView v-slot="{ Component, route }">
          <SwapTransition>
            <component :is="Component" :key="route.path" />
          </SwapTransition>
        </RouterView>
      </Card>
    </main>
  </div>
</template>
