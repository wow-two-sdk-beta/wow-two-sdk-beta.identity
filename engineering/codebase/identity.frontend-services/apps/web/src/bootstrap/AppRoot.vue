<script setup lang="ts">
import { RouteAnnouncer } from '@wow-two-beta/fe/router';
import { RouterView } from 'vue-router';

/** Composes the Identity service's one app: its hosted pages and its console, each a route. */
defineOptions({ name: 'AppRoot' });
</script>

<template>
  <RouteAnnouncer />
  <RouterView />
</template>
