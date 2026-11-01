<script setup lang="ts">
import { ColorModeProvider } from '@wow-two-beta/fe/foundation/primitives';
import { RouteAnnouncer } from '@wow-two-beta/fe/router';
import { RouterView } from 'vue-router';

/** Composes the Identity service's one app: its hosted pages and its console, each a route, in the OS colour mode. */
defineOptions({ name: 'AppRoot' });
</script>

<template>
  <ColorModeProvider>
    <RouteAnnouncer />
    <RouterView />
  </ColorModeProvider>
</template>
